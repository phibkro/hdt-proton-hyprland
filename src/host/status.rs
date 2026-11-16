use crate::config::Config;
use anyhow::{Context, Result, bail};
use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};
use serde_json::{Value, json};
use std::{fs, io, path::Path, process::Command, time::SystemTime};

#[derive(Debug, Deserialize, Serialize, Clone)]
pub struct Client {
    pub title: String,
    pub class: String,
    pub pid: u32,
    #[serde(default)]
    pub address: String,
    #[serde(default)]
    pub at: [i32; 2],
    #[serde(default)]
    pub size: [i32; 2],
    #[serde(default)]
    pub xwayland: bool,
    #[serde(default)]
    pub fullscreen: i32,
}

pub fn command_output(args: &[&str]) -> Result<String> {
    let (program, rest) = args.split_first().context("Missing command")?;
    let result = Command::new(program)
        .args(rest)
        .output()
        .with_context(|| format!("Execute {program}"))?;
    if !result.status.success() {
        bail!("{program} failed with {}", result.status);
    }
    String::from_utf8(result.stdout).context("Command output is not UTF-8")
}

pub fn clients() -> Result<Vec<Client>> {
    serde_json::from_str(&command_output(&["hyprctl", "-j", "clients"])?)
        .context("Malformed compositor client snapshot")
}

pub fn optional(path: &Path) -> Result<Option<String>> {
    match fs::read_to_string(path) {
        Ok(s) => Ok(Some(s)),
        Err(e) if e.kind() == io::ErrorKind::NotFound => Ok(None),
        Err(e) => Err(e).with_context(|| format!("Read {}", path.display())),
    }
}

fn render_settings(pid: Option<u32>) -> Result<Value> {
    let Some(pid) = pid else {
        return Ok(Value::Null);
    };
    let Some(raw) = optional(Path::new(&format!("/proc/{pid}/environ")))? else {
        return Ok(Value::Null);
    };
    let fields: Vec<_> = raw.split('\0').collect();
    Ok(json!({
        "wineGdiRequested": fields.iter().any(|x| x.strip_prefix("WINE_D3D_CONFIG=").is_some_and(|settings| settings.split(';').any(|entry| entry == "renderer=gdi"))),
        "dxvkShaderModel1Requested": fields.iter().any(|x| x.starts_with("DXVK_CONFIG=") && x.contains("d3d9.shaderModel = 1")),
        "builtinD3d9Requested": fields.iter().any(|x| x.starts_with("WINEDLLOVERRIDES=") && x.strip_prefix("WINEDLLOVERRIDES=").unwrap_or("").split(';').any(|r| r == "d3d9=b")),
        "alphaFloorRequested": fields.contains(&"HDT_ALPHA_FLOOR=1"),
        "selectiveInputRequested": fields.iter().any(|x| x.starts_with("HDT_INPUT_REGION_FILE="))
    }))
}

fn match_summary(xml: &str) -> Result<Vec<Value>> {
    use quick_xml::{Reader, events::Event};
    let mut reader = Reader::from_str(xml);
    let mut matches = Vec::new();
    let mut current = None;
    let mut field = None;
    loop {
        match reader.read_event()? {
            Event::Start(e) => {
                let name = e.name().as_ref().to_owned();
                if name == "Game" {
                    current = Some(serde_json::Map::from_iter(
                        ["GameMode", "Result", "Turns", "Format"]
                            .into_iter()
                            .map(|k| (k.to_owned(), Value::Null)),
                    ));
                } else if current.is_some()
                    && ["GameMode", "Result", "Turns", "Format"].contains(&name.as_str())
                {
                    field = Some(name);
                }
            }
            Event::Text(e) => {
                if let (Some(current), Some(field)) = (&mut current, &field) {
                    current.insert(
                        field.clone(),
                        Value::String(quick_xml::escape::unescape(e.as_ref())?.into_owned()),
                    );
                }
            }
            Event::End(e) => {
                if e.name().as_ref() == "Game"
                    && let Some(value) = current.take()
                {
                    matches.push(Value::Object(value));
                }
                field = None;
            }
            Event::Eof => break,
            _ => {}
        }
    }
    Ok(matches)
}

pub fn run(cfg: &Config) -> Result<()> {
    let windows: Vec<_> = clients()?
        .into_iter()
        .filter(|c| {
            c.class == cfg.window_class
                && [
                    "Hearthstone",
                    "HearthstoneOverlay",
                    "Hearthstone Deck Tracker",
                    "Battle.net",
                ]
                .contains(&c.title.as_str())
        })
        .collect();
    #[derive(Deserialize)]
    struct Monitor {
        name: String,
        width: u32,
        height: u32,
        scale: f64,
    }
    let monitors: Vec<Monitor> =
        serde_json::from_str(&command_output(&["hyprctl", "-j", "monitors"])?)?;
    let logs = cfg
        .prefix
        .join("drive_c/Program Files (x86)/Hearthstone/Logs");
    let mut sessions = fs::read_dir(&logs)?
        .map(|x| x.map(|e| e.file_name()))
        .collect::<io::Result<Vec<_>>>()?;
    sessions.retain(|s| s.to_string_lossy().starts_with("Hearthstone_"));
    sessions.sort();
    let latest = sessions.last();
    let mut files = Vec::new();
    if let Some(latest) = latest {
        for name in ["Power.log", "Arena.log", "LoadingScreen.log"] {
            match fs::metadata(logs.join(latest).join(name)) {
                Ok(m) => {
                    let date: DateTime<Utc> = m.modified()?.into();
                    files.push(json!({"name":name,"bytes":m.len(),"modifiedUTC":date.to_rfc3339_opts(chrono::SecondsFormat::Millis,true)}));
                }
                Err(e) if e.kind() == io::ErrorKind::NotFound => {
                    files.push(json!({"name":name,"absent":true}))
                }
                Err(e) => return Err(e.into()),
            }
        }
    }
    let tracker = windows
        .iter()
        .find(|c| c.title == "Hearthstone Deck Tracker");
    let maps = match tracker {
        Some(c) => optional(Path::new(&format!("/proc/{}/maps", c.pid)))?,
        None => None,
    };
    let deck_stats = optional(&cfg.hdt_data.join("DeckStats.xml"))?;
    let matches = deck_stats.as_deref().map(match_summary).transpose()?;
    let hdt = optional(&cfg.hdt_data.join("Logs/hdt_log.txt"))?;
    let control = optional(&cfg.project_root.join("evidence/control-input.json"))?
        .map(|s| serde_json::from_str::<Value>(&s))
        .transpose()?;
    let control = control.map(|c| { let fresh = c.get("observedUTC").and_then(Value::as_str).and_then(|s| DateTime::parse_from_rfc3339(s).ok()).is_some_and(|t| (Utc::now()-t.with_timezone(&Utc)).num_milliseconds().abs() < 2000); json!({"heartbeatFresh":fresh,"regionSnapshotFresh":c.get("fresh"),"controls":c.get("controls"),"pointerOverControl":c.get("pointerOverControl")}) });
    let observed: DateTime<Utc> = SystemTime::now().into();
    let result = json!({
        "observedUTC": observed.to_rfc3339_opts(chrono::SecondsFormat::Millis,true),
        "windows": windows.iter().map(|c|json!({"title":c.title,"position":c.at,"size":c.size,"xwayland":c.xwayland,"fullscreen":c.fullscreen})).collect::<Vec<_>>(),
        "monitors": monitors.iter().map(|m|json!({"name":m.name,"width":m.width,"height":m.height,"scale":m.scale})).collect::<Vec<_>>(),
        "latestGameLogSession": latest.map(|s|s.to_string_lossy()),"files":files,
        "trackerRuntime": maps.as_ref().map_or("unobserved", |m| {let m=m.to_lowercase();if m.contains("libmono-2.0") {"Wine Mono loaded"}else if m.contains("/clr.dll"){"Microsoft CLR loaded"}else{"unresolved"}}),
        "trackerRendering":{"requested":render_settings(tracker.map(|c|c.pid))?,"builtinD3d9Mapped":maps.as_ref().map(|m|m.contains("x86_64-windows/d3d9.dll"))},
        "gameRenderingRequests":render_settings(windows.iter().find(|c|c.title=="Hearthstone").map(|c|c.pid))?,
        "controlInput":control,"savedMatchEntries":matches.as_ref().map(Vec::len),"savedMatchSummary":matches.unwrap_or_default(),
        "hdtLog":hdt.map(|h| json!({"selectedCorrectLogDirectory":h.contains("Using Hearthstone log directory 'C:\\Program Files (x86)\\Hearthstone\\Logs'"),"dnsFailureObserved":h.contains("NameResolutionFailure"),"portableUpdaterWarningObserved":h.contains("Velopack.Exceptions.NotInstalledException"),"hearthMirrorMentioned":h.contains("HearthMirror")}))
    });
    println!("{}", serde_json::to_string_pretty(&result)?);
    Ok(())
}

pub fn surface_check(cfg: &Config, args: &[std::ffi::OsString]) -> Result<()> {
    use std::io::Write;
    use std::os::unix::fs::OpenOptionsExt;
    let state: Value = serde_json::from_slice(&fs::read(
        cfg.project_root.join("evidence/hover-state.json"),
    )?)?;
    let observed = state
        .get("observedUTC")
        .and_then(Value::as_str)
        .context("Missing hover observation time")?;
    let age = (Utc::now() - DateTime::parse_from_rfc3339(observed)?.with_timezone(&Utc))
        .num_milliseconds();
    if !(0..=2500).contains(&age) {
        bail!("Hover state is stale");
    }
    let explicit = !args.is_empty();
    let bounds: Vec<i32> = if explicit {
        if args.len() != 5 || args[0] != "--bounds" {
            bail!("Usage: surface-check [--bounds x y width height]");
        }
        args[1..]
            .iter()
            .map(|x| {
                x.to_str()
                    .context("Bounds must be UTF-8")?
                    .parse::<i32>()
                    .context("Bounds must be signed integers")
            })
            .collect::<Result<_>>()?
    } else {
        state
            .get("lastPreviewBounds")
            .and_then(Value::as_array)
            .context("Hover an overlay card once before sampling")?
            .iter()
            .map(|x| {
                i32::try_from(x.as_i64().context("Bounds must be signed integers")?)
                    .context("Bounds outside signed int range")
            })
            .collect::<Result<_>>()?
    };
    if bounds.len() != 4 {
        bail!("Hover an overlay card once before sampling");
    }
    let clients = clients()?;
    let overlays: Vec<_> = clients
        .iter()
        .filter(|c| c.title == "HearthstoneOverlay" && c.class == cfg.window_class)
        .collect();
    let mains: Vec<_> = clients
        .iter()
        .filter(|c| c.title == "Hearthstone Deck Tracker" && c.class == cfg.window_class)
        .collect();
    if overlays.len() != 1 || mains.len() != 1 || overlays[0].pid != mains[0].pid {
        bail!("Ambiguous HDT identity");
    }
    let tree = command_output(&["xwininfo", "-root", "-tree"])?;
    let ids: Vec<_> = tree
        .lines()
        .filter_map(|line| {
            let (id, rest) = line.trim_start().split_once(' ')?;
            if id.starts_with("0x") && rest.starts_with("\"HearthstoneOverlay\":") {
                Some(id)
            } else {
                None
            }
        })
        .collect();
    if ids.len() != 1 {
        bail!("Ambiguous X11 overlay identity");
    }
    let output = Command::new(std::env::current_exe()?)
        .arg("surface-probe")
        .arg(ids[0])
        .arg(overlays[0].pid.to_string())
        .args(bounds.iter().map(ToString::to_string))
        .output()?;
    if !output.status.success() {
        bail!(
            "Native surface probe failed: {}",
            String::from_utf8_lossy(&output.stderr)
        );
    }
    let surface: Value = serde_json::from_slice(&output.stdout)?;
    let result = json!({"observedUTC":Utc::now().to_rfc3339_opts(chrono::SecondsFormat::Millis,true),"bounds":bounds,"regionSource":if explicit {"explicit"} else {"lastPreview"},"state":state,"surface":surface});
    crate::setup::private_dir(&cfg.project_root.join("evidence"))?;
    let text = serde_json::to_string_pretty(&result)?;
    let mut file = fs::OpenOptions::new()
        .create(true)
        .truncate(true)
        .write(true)
        .mode(0o600)
        .open(cfg.project_root.join("evidence/surface-check.json"))?;
    file.write_all(text.as_bytes())?;
    let mut samples = fs::OpenOptions::new()
        .create(true)
        .append(true)
        .mode(0o600)
        .open(cfg.project_root.join("evidence/surface-samples.jsonl"))?;
    writeln!(samples, "{}", serde_json::to_string(&result)?)?;
    println!("{text}");
    Ok(())
}
