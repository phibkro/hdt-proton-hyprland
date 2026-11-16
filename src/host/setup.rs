use crate::{
    config::Config,
    status::{clients, command_output},
};
use anyhow::{Context, Result, bail};
use quick_xml::{Reader, events::Event};
use std::{
    fs::{self, DirBuilder, OpenOptions},
    io::{self, Write},
    os::unix::fs::{DirBuilderExt, OpenOptionsExt},
    path::Path,
    thread,
    time::Duration,
};

pub fn private_dir(path: &Path) -> Result<()> {
    DirBuilder::new().recursive(true).mode(0o700).create(path)?;
    Ok(())
}

fn backup(source: &Path, target: &Path, missing_ok: bool) -> Result<()> {
    let mut input = match fs::File::open(source) {
        Ok(f) => f,
        Err(e) if missing_ok && e.kind() == io::ErrorKind::NotFound => return Ok(()),
        Err(e) => return Err(e.into()),
    };
    private_dir(target.parent().context("Backup needs a parent")?)?;
    match OpenOptions::new()
        .write(true)
        .create_new(true)
        .mode(0o600)
        .open(target)
    {
        Ok(mut output) => {
            io::copy(&mut input, &mut output)?;
            output.sync_all()?;
        }
        Err(e) if e.kind() == io::ErrorKind::AlreadyExists => {}
        Err(e) => return Err(e.into()),
    }
    Ok(())
}

fn replace_file(path: &Path, bytes: &[u8]) -> Result<()> {
    let parent = path.parent().context("Output needs parent")?;
    let temporary = parent.join(format!(".hdt-{}.tmp", uuid::Uuid::new_v4()));
    let result = (|| -> Result<()> {
        let mut file = OpenOptions::new()
            .create_new(true)
            .write(true)
            .mode(0o600)
            .open(&temporary)?;
        file.write_all(bytes)?;
        file.sync_all()?;
        if let Ok(meta) = fs::metadata(path) {
            fs::set_permissions(&temporary, meta.permissions())?;
        }
        fs::rename(&temporary, path)?;
        Ok(())
    })();
    if result.is_err() {
        let _ = fs::remove_file(&temporary);
    }
    result
}

pub fn ensure_stopped() -> Result<()> {
    for entry in fs::read_dir("/proc")? {
        let entry = entry?;
        if !entry
            .file_name()
            .as_encoded_bytes()
            .iter()
            .all(u8::is_ascii_digit)
        {
            continue;
        }
        match fs::read_to_string(entry.path().join("comm")) {
            Ok(s) if s.trim() == "HearthstoneDeck" => {
                bail!("Close all HDT instances before changing its configuration")
            }
            Ok(_) => {}
            Err(e)
                if [io::ErrorKind::NotFound, io::ErrorKind::PermissionDenied]
                    .contains(&e.kind()) => {}
            Err(e) => return Err(e.into()),
        }
    }
    Ok(())
}

pub fn patch_visibility(xml: &str) -> Result<String> {
    let keys = [
        "HideInMenu",
        "HideInBackground",
        "HideMenuOverlayInBackground",
    ];
    let mut reader = Reader::from_str(xml);
    let mut target: Option<(String, usize, String)> = None;
    let mut changes = Vec::new();
    loop {
        let before = reader.buffer_position() as usize;
        match reader.read_event()? {
            Event::Start(e) => {
                let key = e.name().as_ref().to_owned();
                if keys.contains(&key.as_str()) {
                    if target.is_some() {
                        bail!("Nested visibility settings");
                    }
                    target = Some((key, before, String::new()));
                }
            }
            Event::Text(e) => {
                if let Some((_, _, text)) = &mut target {
                    text.push_str(e.as_ref());
                }
            }
            Event::End(e) => {
                if let Some((key, _, _)) = &target
                    && e.name().as_ref() == key.as_str()
                {
                    let (key, start, text) = target.take().context("Missing visibility setting")?;
                    if !["true", "false"].contains(&text.trim()) {
                        bail!("Invalid {key} setting");
                    }
                    changes.push((key, start, reader.buffer_position() as usize));
                }
            }
            Event::Eof => break,
            _ => {}
        }
    }
    for key in keys {
        if changes.iter().filter(|(k, _, _)| k == key).count() != 1 {
            bail!("Expected exactly one {key} setting");
        }
    }
    let mut result = xml.to_owned();
    for (key, start, end) in changes.into_iter().rev() {
        result.replace_range(start..end, &format!("<{key}>false</{key}>"));
    }
    Ok(result)
}

pub fn configure_overlay(cfg: &Config) -> Result<()> {
    ensure_stopped()?;
    let path = cfg.hdt_data.join("config.xml");
    let xml = fs::read_to_string(&path)?;
    let updated = patch_visibility(&xml)?;
    backup(
        &path,
        &cfg.project_root
            .join("backups/hdt-before-render/config-before-background-test.xml"),
        false,
    )?;
    replace_file(&path, updated.as_bytes())?;
    println!(
        "Set HDT menu visibility and disabled HDT background hiding; compositor owns overlay stacking."
    );
    Ok(())
}

pub fn patch_plugins(xml: &str) -> Result<String> {
    const ENTRY: &str = "<PluginSettings><FileName>Plugins/ProtonCompatibility.dll</FileName><IsEnabled>true</IsEnabled><Name>Proton desktop compatibility</Name></PluginSettings>";
    let mut reader = Reader::from_str(xml);
    let mut block = None;
    let mut filename = String::new();
    let mut reading_filename = false;
    let mut ours = Vec::new();
    let mut root_end = None;
    let mut empty_root = None;
    let mut roots = 0;
    loop {
        let before = reader.buffer_position() as usize;
        match reader.read_event()? {
            Event::Start(e) => match e.name().as_ref() {
                "ArrayOfPluginSettings" => roots += 1,
                "PluginSettings" => {
                    if block.is_some() {
                        bail!("Nested plugin settings");
                    }
                    block = Some(before);
                    filename.clear();
                }
                "FileName" if block.is_some() => reading_filename = true,
                _ => {}
            },
            Event::Text(e) if reading_filename => {
                filename.push_str(&quick_xml::escape::unescape(e.as_ref())?)
            }
            Event::End(e) => match e.name().as_ref() {
                "FileName" => reading_filename = false,
                "PluginSettings" => {
                    let start = block.take().context("Unmatched plugin setting")?;
                    if filename == "Plugins/ProtonCompatibility.dll" {
                        ours.push((start, reader.buffer_position() as usize));
                    }
                }
                "ArrayOfPluginSettings" => root_end = Some(before),
                _ => {}
            },
            Event::Empty(e) if e.name().as_ref() == "ArrayOfPluginSettings" => {
                roots += 1;
                empty_root = Some((before, reader.buffer_position() as usize));
            }
            Event::Eof => break,
            _ => {}
        }
    }
    if roots != 1 {
        bail!("Unexpected plugin settings schema");
    }
    if ours.len() > 1 {
        bail!("Duplicate local plugin settings");
    }
    let mut result = xml.to_owned();
    if let Some((start, end)) = ours.first() {
        result.replace_range(*start..*end, ENTRY);
    } else if let Some((start, end)) = empty_root {
        let opening = xml[start..end]
            .strip_suffix("/>")
            .context("Invalid empty plugin root")?
            .trim_end();
        result.replace_range(
            start..end,
            &format!("{opening}>{ENTRY}</ArrayOfPluginSettings>"),
        );
    } else {
        let position = root_end.context("Unexpected plugin settings schema")?;
        result.insert_str(position, ENTRY);
    }
    Ok(result)
}

pub fn install_plugin(cfg: &Config) -> Result<()> {
    let compiled = fs::read(cfg.project_root.join("evidence/ProtonCompatibility.dll"))
        .context("Build the compatibility plugin first")?;
    let trackers: Vec<_> = clients()?
        .into_iter()
        .filter(|c| c.title == "Hearthstone Deck Tracker" && c.class == cfg.window_class)
        .collect();
    if trackers.len() > 1 {
        bail!("Multiple HDT windows; refusing to guess");
    }
    if let Some(tracker) = trackers.first() {
        if fs::read_to_string(format!("/proc/{}/comm", tracker.pid))?.trim() != "HearthstoneDeck" {
            bail!("HDT process identity changed");
        }
        let hex = tracker
            .address
            .strip_prefix("0x")
            .context("Invalid tracker address")?;
        if hex.is_empty() || !hex.bytes().all(|b| b.is_ascii_hexdigit()) {
            bail!("Invalid tracker address");
        }
        command_output(&[
            "hyprctl",
            "dispatch",
            &format!(
                "hl.dsp.window.close({{window=\"address:{}\"}})",
                tracker.address
            ),
        ])?;
        let mut stopped = false;
        for _ in 0..50 {
            match fs::read_to_string(format!("/proc/{}/comm", tracker.pid)) {
                Err(e) if e.kind() == io::ErrorKind::NotFound => {
                    stopped = true;
                    break;
                }
                Err(e) => return Err(e.into()),
                Ok(_) => thread::sleep(Duration::from_millis(200)),
            }
        }
        if !stopped {
            bail!("HDT did not exit; nothing installed. Close HDT manually and retry hdt-setup.");
        }
    }
    ensure_stopped()?;
    let settings = cfg.hdt_data.join("plugins.xml");
    let updated = patch_plugins(&fs::read_to_string(&settings)?)?;
    let backups = cfg.project_root.join("backups/hdt-before-render");
    backup(
        &settings,
        &backups.join("plugins-before-compatibility.xml"),
        false,
    )?;
    private_dir(&cfg.hdt_data.join("Plugins"))?;
    let plugin = cfg.hdt_data.join("Plugins/ProtonCompatibility.dll");
    backup(
        &plugin,
        &backups.join("ProtonCompatibility-before-setup.dll"),
        true,
    )?;
    replace_file(&plugin, &compiled)?;
    replace_file(&settings, updated.as_bytes())?;
    println!("Installed and enabled local HDT compatibility plugin.");
    Ok(())
}

pub fn overlay_rule(cfg: &Config, disabled: bool) -> Result<i32> {
    let source = if disabled {
        "if hdtProtonOverlayRule then hdtProtonOverlayRule:set_enabled(false) end\nif hdtProtonMainRule then hdtProtonMainRule:set_enabled(false) end\nif hdtProtonStackTimer then hdtProtonStackTimer:set_enabled(false) end\nif hdtProtonStackSubscription then hdtProtonStackSubscription:remove(); hdtProtonStackSubscription=nil end\nif hdtProtonControlRule then hdtProtonControlRule:set_enabled(false) end\nif hdtProtonControlTimer then hdtProtonControlTimer:set_enabled(false) end\nhdtProtonControls=nil; hdtProtonControlTicks=0; hdtProtonControlActive=false".to_owned()
    } else {
        fs::read_to_string(cfg.source_root.join("src/hyprland/overlay-rules.lua"))?
            .replace("__STEAM_WINDOW_CLASS__", &cfg.window_class)
    };
    let lua = source
        .lines()
        .filter(|l| !l.trim_start().starts_with("--"))
        .collect::<Vec<_>>()
        .join(" ");
    Ok(std::process::Command::new("hyprctl")
        .args(["eval", &lua])
        .status()?
        .code()
        .unwrap_or(1))
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn visibility_updates_only_unique_valid_fields() {
        let xml = "<Config><Other>true</Other><HideInMenu>true</HideInMenu><HideInBackground>false</HideInBackground><HideMenuOverlayInBackground>true</HideMenuOverlayInBackground></Config>";
        let patched = patch_visibility(xml).unwrap();
        assert!(patched.contains("<Other>true</Other>"));
        assert!(!patched.contains("<HideInMenu>true"));
        assert!(
            patch_visibility(&xml.replace("</Config>", "<HideInMenu>false</HideInMenu></Config>"))
                .is_err()
        );
        assert!(patch_visibility("<Config/>").is_err());
    }
    #[test]
    fn plugin_upsert_is_unique_and_preserves_other_plugins() {
        let empty = "<?xml version=\"1.0\"?><ArrayOfPluginSettings attr=\"keep\" />";
        let first = patch_plugins(empty).unwrap();
        assert!(first.contains("attr=\"keep\""));
        assert_eq!(first, patch_plugins(&first).unwrap());
        let other = "<ArrayOfPluginSettings><PluginSettings><FileName>other.dll</FileName></PluginSettings></ArrayOfPluginSettings>";
        let new = patch_plugins(other).unwrap();
        assert!(new.contains("other.dll"));
        let duplicate=new.replace("</ArrayOfPluginSettings>","<PluginSettings><FileName>Plugins/ProtonCompatibility.dll</FileName></PluginSettings></ArrayOfPluginSettings>");
        assert!(patch_plugins(&duplicate).is_err());
        assert!(patch_plugins("<Wrong/>").is_err());
    }
}
