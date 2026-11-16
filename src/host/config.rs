use anyhow::{Context, Result, bail};
use serde::{Deserialize, Serialize};
use std::{
    collections::BTreeMap,
    ffi::OsString,
    fs,
    path::{Component, Path, PathBuf},
};

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Config {
    pub source_root: PathBuf,
    pub project_root: PathBuf,
    pub steam_root: PathBuf,
    pub prefix: PathBuf,
    pub hdt_data: PathBuf,
    pub app_directory: PathBuf,
    pub native_directory: PathBuf,
    pub app_id: String,
    pub window_class: String,
}

#[derive(Default, Deserialize)]
#[serde(deny_unknown_fields, rename_all = "camelCase")]
struct LocalConfig {
    steam_app_id: Option<String>,
    steam_root: Option<String>,
}

pub fn parse_app_id(value: &str) -> Result<String> {
    if value.is_empty()
        || value.starts_with('0')
        || !value.bytes().all(|b| b.is_ascii_digit())
        || value.parse::<u32>().is_err()
    {
        bail!("Set HS_APP_ID to the numeric Steam shortcut app ID (uint32).");
    }
    Ok(value.to_owned())
}

fn absolute(path: &Path, cwd: &Path) -> PathBuf {
    let input = if path.is_absolute() {
        path.to_path_buf()
    } else {
        cwd.join(path)
    };
    let mut out = PathBuf::new();
    for component in input.components() {
        match component {
            Component::ParentDir => {
                out.pop();
            }
            Component::CurDir => {}
            other => out.push(other),
        }
    }
    out
}

impl Config {
    pub fn from_env() -> Result<Self> {
        Self::load(&std::env::vars_os().collect(), &std::env::current_dir()?)
    }

    pub fn load(env: &BTreeMap<OsString, OsString>, cwd: &Path) -> Result<Self> {
        let get = |key: &str| -> Result<Option<String>> {
            env.get(&OsString::from(key))
                .map(|s| {
                    s.clone()
                        .into_string()
                        .map_err(|_| anyhow::anyhow!("{key} must be UTF-8"))
                })
                .transpose()
        };
        let source_root = absolute(
            Path::new(
                &get("HDT_SOURCE_ROOT")?.unwrap_or_else(|| env!("CARGO_MANIFEST_DIR").to_owned()),
            ),
            cwd,
        );
        let configured_root = get("HDT_PROJECT_DIR")?;
        if configured_root
            .as_ref()
            .is_some_and(|p| !Path::new(p).is_absolute())
        {
            bail!("HDT_PROJECT_DIR must be an absolute directory outside the Nix store.");
        }
        let project_root = absolute(
            configured_root
                .as_ref()
                .map_or(source_root.as_path(), Path::new),
            cwd,
        );
        if project_root.starts_with("/nix/store") {
            bail!("Set HDT_PROJECT_DIR to a writable directory outside the Nix store.");
        }
        let local_path = project_root.join("hdt.local.json");
        let local: LocalConfig = match fs::read(&local_path) {
            Ok(bytes) => serde_json::from_slice(&bytes)
                .context("hdt.local.json accepts only string steamAppId and optional steamRoot")?,
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => LocalConfig::default(),
            Err(e) => return Err(e).context("Read local configuration"),
        };
        let app_id = parse_app_id(
            &get("HS_APP_ID")?
                .or(local.steam_app_id)
                .context("Set HS_APP_ID or steamAppId in hdt.local.json")?,
        )?;
        let home = get("HOME")?
            .filter(|x| !x.is_empty())
            .context("HOME is required")?;
        let steam = get("STEAM_ROOT")?
            .or(local.steam_root)
            .unwrap_or_else(|| format!("{home}/.local/share/Steam"));
        if !Path::new(&steam).is_absolute() {
            bail!("STEAM_ROOT must be an absolute path.");
        }
        let steam_root = absolute(Path::new(&steam), cwd);
        let prefix = steam_root
            .join("steamapps/compatdata")
            .join(&app_id)
            .join("pfx");
        let hdt_data =
            prefix.join("drive_c/users/steamuser/AppData/Roaming/HearthstoneDeckTracker");
        let app_directory = absolute(
            &get("HDT_APP_DIR")?.map_or_else(|| project_root.join("app"), PathBuf::from),
            cwd,
        );
        let native_directory =
            get("HDT_NATIVE_DIR")?.map_or_else(|| project_root.join("evidence"), PathBuf::from);
        if !native_directory.is_absolute() {
            bail!("HDT_NATIVE_DIR must be an absolute path.");
        }
        let window_class = format!("steam_app_{app_id}");
        Ok(Self {
            source_root,
            project_root,
            steam_root,
            prefix,
            hdt_data,
            app_directory,
            native_directory,
            app_id,
            window_class,
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn id_contract_rejects_paths_coercion_and_overflow() {
        for good in ["1", "3964018117", "4294967295"] {
            assert_eq!(parse_app_id(good).unwrap(), good);
        }
        for bad in [
            "",
            "0",
            "01",
            "-1",
            "1.0",
            " 1",
            "1 ",
            "../pfx",
            "4294967296",
            "steam_app_1",
            "１",
        ] {
            assert!(parse_app_id(bad).is_err(), "accepted {bad}");
        }
    }
    #[test]
    fn explicit_config_precedence_and_store_refusal() {
        let root = tempfile::tempdir().unwrap();
        fs::write(
            root.path().join("hdt.local.json"),
            r#"{"steamAppId":"123","steamRoot":"/saved/steam"}"#,
        )
        .unwrap();
        let mut env = BTreeMap::from([
            (OsString::from("HOME"), OsString::from("/home/test")),
            (
                OsString::from("HDT_PROJECT_DIR"),
                root.path().as_os_str().to_os_string(),
            ),
        ]);
        let cfg = Config::load(&env, root.path()).unwrap();
        assert_eq!(cfg.app_id, "123");
        assert_eq!(cfg.steam_root, Path::new("/saved/steam"));
        env.insert("HS_APP_ID".into(), "456".into());
        assert_eq!(Config::load(&env, root.path()).unwrap().app_id, "456");
        env.insert(
            "HDT_PROJECT_DIR".into(),
            "/nix/store/payload/../data".into(),
        );
        assert!(Config::load(&env, root.path()).is_err());
        env.insert("HDT_PROJECT_DIR".into(), "relative".into());
        assert!(Config::load(&env, root.path()).is_err());
    }
}
