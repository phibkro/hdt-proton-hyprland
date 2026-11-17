//! Hyprland control gating with a short compositor TTL and identity-bound X11 links.
use crate::config::Config;
use anyhow::{Context, Result, ensure};
use serde::Deserialize;
use std::{
    ffi::OsString,
    fs,
    path::Path,
    process::Command,
    sync::{
        Arc,
        atomic::{AtomicBool, Ordering},
    },
    thread,
    time::{Duration, SystemTime, UNIX_EPOCH},
};

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct Regions {
    session: String,
    #[serde(rename = "observedMS")]
    observed_ms: f64,
    width: i64,
    height: i64,
    rectangles: Vec<[i64; 4]>,
}
#[derive(Deserialize)]
struct Client {
    title: String,
    class: String,
    address: String,
    pid: u32,
    size: [i64; 2],
    at: [i64; 2],
}
#[derive(Deserialize)]
struct Cursor {
    x: f64,
    y: f64,
}
#[derive(Deserialize)]
struct Link {
    overlay: u32,
    game: u32,
    original: u32,
}
fn command(program: impl AsRef<std::ffi::OsStr>, arguments: &[OsString]) -> Result<String> {
    let mut process = Command::new(program);
    process.args(arguments);
    output(process)
}
fn output(mut process: Command) -> Result<String> {
    let output = process.output()?;
    ensure!(
        output.status.success(),
        "{}",
        String::from_utf8_lossy(&output.stderr).trim()
    );
    String::from_utf8(output.stdout).context("Command returned invalid UTF-8")
}
fn hypr(arguments: &[&str]) -> Result<String> {
    command(
        "hyprctl",
        &arguments.iter().map(OsString::from).collect::<Vec<_>>(),
    )
}
fn lua(script: &str) -> Result<()> {
    hypr(&["eval", script]).map(|_| ())
}
fn read_regions(path: &Path) -> Result<Option<Regions>> {
    match fs::read(path) {
        Ok(bytes) => Ok(Some(serde_json::from_slice(&bytes)?)),
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => Ok(None),
        Err(error) => Err(error.into()),
    }
}
fn valid_rectangles(rectangles: &[[i64; 4]], width: i64, height: i64) -> bool {
    rectangles.len() <= 2048
        && rectangles.iter().all(|r| {
            r[0] >= 0
                && r[1] >= 0
                && r[2] > 0
                && r[3] > 0
                && r[0].checked_add(r[2]).is_some_and(|x| x <= width)
                && r[1].checked_add(r[3]).is_some_and(|y| y <= height)
        })
}
struct Links {
    entries: Vec<(u32, Link)>,
    class: String,
    namespace: crate::launch::Session,
}
impl Drop for Links {
    fn drop(&mut self) {
        let mut failures = Vec::new();
        for script in [
            "hdtProtonControls=nil; hdtProtonControlTicks=0; if hdtProtonControlRule then hdtProtonControlRule:set_enabled(false) end; hdtProtonControlActive=false",
            "if hdtProtonStackTimer then hdtProtonStackTimer:set_enabled(true) end",
        ] {
            if let Err(e) = lua(script) {
                failures.push(e.to_string());
            }
        }
        if let Ok(exe) = std::env::current_exe() {
            for (pid, link) in self.entries.iter().rev() {
                if let Err(e) = self
                    .namespace
                    .link_command(
                        &exe,
                        &[
                            "overlay-link".into(),
                            "restore".into(),
                            pid.to_string().into(),
                            self.class.clone().into(),
                            link.overlay.to_string().into(),
                            link.game.to_string().into(),
                            link.original.to_string().into(),
                        ],
                    )
                    .and_then(output)
                {
                    failures.push(e.to_string());
                }
            }
        }
        for failure in failures {
            eprintln!("HDT control cleanup failed: {failure}");
        }
    }
}
fn tick(cfg: &Config, nonce: &str, links: &mut Links, pair: &mut String) -> Result<()> {
    let Some(data) = read_regions(&cfg.project_root.join("evidence/input-region.json"))? else {
        return Ok(());
    };
    if data.session != nonce {
        return Ok(());
    }
    let clients: Vec<Client> = serde_json::from_str(&hypr(&["-j", "clients"])?)?;
    let identify = |title| {
        clients
            .iter()
            .filter(|c| c.title == title && c.class == cfg.window_class)
            .collect::<Vec<_>>()
    };
    let overlays = identify("HearthstoneOverlay");
    let mains = identify("Hearthstone Deck Tracker");
    ensure!(
        overlays.len() == 1 && mains.len() == 1 && overlays[0].pid == mains[0].pid,
        "Ambiguous HDT identity"
    );
    let overlay = overlays[0];
    let games = identify("Hearthstone");
    if games.len() == 1 {
        let game = games[0];
        let next = format!(
            "{}:{}:{}:{}",
            overlay.pid, overlay.address, game.pid, game.address
        );
        if next != *pair {
            let link: Link = serde_json::from_str(&output(links.namespace.link_command(
                &std::env::current_exe()?,
                &[
                    "overlay-link".into(),
                    "apply".into(),
                    overlay.pid.to_string().into(),
                    game.pid.to_string().into(),
                    cfg.window_class.clone().into(),
                ],
            )?)?)?;
            ensure!(
                link.overlay != 0 && link.game != 0,
                "Invalid overlay relationship readback"
            );
            links.entries.push((overlay.pid, link));
            lua("if hdtProtonStackTimer then hdtProtonStackTimer:set_enabled(false) end")?;
            *pair = next;
            println!("HDT overlay linked to game stack; periodic raising disabled.");
        }
    }
    let [width, height] = overlay.size;
    let now = SystemTime::now().duration_since(UNIX_EPOCH)?.as_secs_f64() * 1000.;
    let fresh = data.observed_ms.is_finite()
        && (now - data.observed_ms).abs() < 1500.
        && width == data.width
        && height == data.height;
    let rectangles = if fresh {
        data.rectangles.as_slice()
    } else {
        &[]
    };
    ensure!(
        valid_rectangles(rectangles, width, height),
        "Invalid control rectangles"
    );
    let values = rectangles
        .iter()
        .map(|r| format!("{{{},{},{},{}}}", r[0], r[1], r[2], r[3]))
        .collect::<Vec<_>>()
        .join(",");
    lua(&format!(
        "hdtProtonControls={{{values}}}; hdtProtonControlTicks=40"
    ))?;
    let cursor: Cursor = serde_json::from_str(&hypr(&["-j", "cursorpos"])?)?;
    let hit = rectangles.iter().any(|r| {
        cursor.x >= overlay.at[0] as f64 + r[0] as f64
            && cursor.y >= overlay.at[1] as f64 + r[1] as f64
            && cursor.x < overlay.at[0] as f64 + (r[0] + r[2]) as f64
            && cursor.y < overlay.at[1] as f64 + (r[1] + r[3]) as f64
    });
    fs::write(
        cfg.project_root.join("evidence/control-input.json"),
        serde_json::to_vec(
            &serde_json::json!({"observedUTC":crate::timestamp::Timestamp::now().utc_millis(),"fresh":fresh,"controls":rectangles.len(),"pointerOverControl":hit}),
        )?,
    )?;
    Ok(())
}
pub fn run(cfg: &Config, args: &[OsString]) -> Result<()> {
    let nonce = args
        .first()
        .and_then(|s| s.to_str())
        .filter(|s| !s.is_empty())
        .context("Expected control-region session nonce")?;
    let namespace = crate::launch::controller_session(&cfg.prefix, args)?;
    let stopped = Arc::new(AtomicBool::new(false));
    let mut registrations = Vec::new();
    for signal in [signal_hook::consts::SIGTERM, signal_hook::consts::SIGINT] {
        registrations.push(signal_hook::flag::register(signal, Arc::clone(&stopped))?);
    }
    let mut links = Links {
        entries: Vec::new(),
        class: cfg.window_class.clone(),
        namespace,
    };
    let mut pair = String::new();
    let mut previous = String::new();
    while !stopped.load(Ordering::Relaxed) {
        match tick(cfg, nonce, &mut links, &mut pair) {
            Ok(()) => previous.clear(),
            Err(e) => {
                let message = e.to_string();
                if message != previous {
                    eprintln!("HDT control input pending: {message}");
                }
                previous = message;
            }
        }
        thread::sleep(Duration::from_millis(200));
    }
    drop(links);
    for registration in registrations {
        signal_hook::low_level::unregister(registration);
    }
    Ok(())
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn only_missing_region_input_is_normal_waiting() -> Result<()> {
        let temporary = tempfile::tempdir()?;
        let missing = temporary.path().join("missing");
        assert!(read_regions(&missing)?.is_none());
        assert!(command(&missing, &[]).is_err());
        fs::write(&missing, b"malformed")?;
        assert!(read_regions(&missing).is_err());
        Ok(())
    }
    #[test]
    fn rejects_overflow_and_outside_controls() {
        assert!(!valid_rectangles(&[[i64::MAX, 0, 1, 1]], 100, 100));
        assert!(!valid_rectangles(&[[99, 0, 2, 1]], 100, 100));
        assert!(valid_rectangles(&[[0, 0, 100, 100]], 100, 100));
    }
}
