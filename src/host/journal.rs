//! Independent, bounded, byte-preserving evidence collection.
use anyhow::{Context, Result, bail, ensure};
use base64::{Engine, engine::general_purpose::STANDARD};
use serde::{Deserialize, Serialize};
use serde_json::{Value, json};
use std::{
    collections::HashSet,
    ffi::OsString,
    fs::{self, DirBuilder, File, OpenOptions},
    io::Write,
    os::{
        fd::AsRawFd,
        unix::fs::{DirBuilderExt, FileExt, MetadataExt, OpenOptionsExt},
    },
    path::{Component, Path, PathBuf},
    sync::{
        Arc,
        atomic::{AtomicBool, Ordering},
    },
    time::{Duration, Instant},
};

#[derive(Clone, Copy, Debug, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "lowercase")]
pub enum From {
    Start,
    End,
}
#[derive(Clone, Debug, Serialize)]
pub struct Source {
    pub name: String,
    pub path: PathBuf,
}
const SESSION_LIMIT: usize = 128;
const SCAN_LIMIT: usize = 4096;
/// Only the log streams explicitly configured by the reference log manager.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum GameLog {
    Power,
    LoadingScreen,
    Arena,
    Achievements,
}
impl Serialize for GameLog {
    fn serialize<S: serde::Serializer>(
        &self,
        serializer: S,
    ) -> std::result::Result<S::Ok, S::Error> {
        serializer.serialize_str(self.filename())
    }
}
impl GameLog {
    const ALL: [Self; 4] = [
        Self::Power,
        Self::LoadingScreen,
        Self::Arena,
        Self::Achievements,
    ];
    fn filename(self) -> &'static str {
        match self {
            Self::Power => "Power.log",
            Self::LoadingScreen => "LoadingScreen.log",
            Self::Arena => "Arena.log",
            Self::Achievements => "Achievements.log",
        }
    }
    fn key(self) -> &'static str {
        match self {
            Self::Power => "power",
            Self::LoadingScreen => "loading-screen",
            Self::Arena => "arena",
            Self::Achievements => "achievements",
        }
    }
    fn parse(value: &OsString) -> Result<Self> {
        Self::ALL
            .into_iter()
            .find(|log| value == log.filename())
            .with_context(|| {
                format!(
                    "Choose an allowlisted --game-log: {}.",
                    Self::ALL.map(Self::filename).join(", ")
                )
            })
    }
}
pub enum DiscoveryRoot {
    Recorder(PathBuf),
    GameLogs { root: PathBuf, logs: Vec<GameLog> },
}
struct Discovery {
    config: DiscoveryRoot,
    sessions: HashSet<String>,
    unavailable: bool,
}
impl Discovery {
    fn root(&self) -> &Path {
        match &self.config {
            DiscoveryRoot::Recorder(root) | DiscoveryRoot::GameLogs { root, .. } => root,
        }
    }
    fn kind(&self) -> &'static str {
        match self.config {
            DiscoveryRoot::Recorder(_) => "recorder",
            DiscoveryRoot::GameLogs { .. } => "game",
        }
    }
}
// Directory descriptors keep discovery and source opens below the exact owned
// traversal, even when another process renames a parent during a poll.
fn directory_handle(path: &Path) -> Result<Option<File>> {
    use nix::fcntl::{OFlag, open, openat};
    use nix::sys::stat::Mode;
    ensure!(path.is_absolute(), "Directory path must be absolute.");
    let flags = OFlag::O_RDONLY | OFlag::O_DIRECTORY | OFlag::O_NOFOLLOW | OFlag::O_CLOEXEC;
    let mut handle = File::from(open(Path::new("/"), flags, Mode::empty())?);
    for part in path.components() {
        let Component::Normal(name) = part else {
            ensure!(
                matches!(part, Component::RootDir),
                "Directory path must be absolute and normalized."
            );
            continue;
        };
        match openat(&handle, Path::new(name), flags, Mode::empty()) {
            Ok(next) => handle = File::from(next),
            Err(nix::errno::Errno::ENOENT) => return Ok(None),
            Err(error) => {
                return Err(error)
                    .context("Directory components must be readable directories, never symlinks.");
            }
        }
    }
    Ok(Some(handle))
}
fn descriptor_path(directory: &File) -> PathBuf {
    PathBuf::from(format!("/proc/self/fd/{}", directory.as_raw_fd()))
}
struct Cursor {
    source: Source,
    from: From,
    handle: Option<File>,
    identity: Option<String>,
    offset: u64,
    generation: u64,
    anchor: Option<Vec<u8>>,
    unavailable: Option<bool>,
}
impl Cursor {
    fn new(source: Source, from: From) -> Self {
        Self {
            source,
            from,
            handle: None,
            identity: None,
            offset: 0,
            generation: 0,
            anchor: None,
            unavailable: None,
        }
    }
}
fn absolute(path: &Path) -> Result<PathBuf> {
    let path = if path.is_absolute() {
        path.to_owned()
    } else {
        std::env::current_dir()?.join(path)
    };
    let mut result = PathBuf::new();
    for component in path.components() {
        match component {
            Component::ParentDir => {
                result.pop();
            }
            Component::CurDir => {}
            other => result.push(other.as_os_str()),
        }
    }
    Ok(result)
}
fn identity(meta: &fs::Metadata) -> String {
    format!("{}:{}", meta.dev(), meta.ino())
}
fn optional_metadata(path: &Path) -> Result<Option<fs::Metadata>> {
    match fs::symlink_metadata(path) {
        Ok(meta) => Ok(Some(meta)),
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => Ok(None),
        Err(e) => Err(e.into()),
    }
}

pub struct InputJournal {
    directory: PathBuf,
    cursors: Vec<Cursor>,
    from: From,
    max_bytes: u64,
    discovery: Vec<Discovery>,
    sink: Option<File>,
    bytes: u64,
    seq: u64,
    started: Instant,
    closed: bool,
    heartbeat: Option<Instant>,
}
impl InputJournal {
    pub fn new(
        directory: PathBuf,
        sources: Vec<Source>,
        from: From,
        max_bytes: u64,
        roots: Vec<DiscoveryRoot>,
    ) -> Result<Self> {
        ensure!(
            (65_536..=9_007_199_254_740_991).contains(&max_bytes),
            "Journal capacity must be at least 65536 bytes and a safe integer."
        );
        ensure!(
            !sources.is_empty() || !roots.is_empty(),
            "Supply unique source names and paths."
        );
        let directory = absolute(&directory)?;
        let mut discovery = Vec::new();
        for mut config in roots {
            match &mut config {
                DiscoveryRoot::Recorder(root) => *root = absolute(root)?,
                DiscoveryRoot::GameLogs { root, logs } => {
                    use std::os::unix::ffi::OsStrExt;
                    ensure!(
                        root.is_absolute()
                            && !root
                                .as_os_str()
                                .as_bytes()
                                .split(|b| *b == b'/')
                                .any(|part| part == b"." || part == b".."),
                        "--game-log-root must be absolute without . or .. components."
                    );
                    ensure!(
                        !logs.is_empty()
                            && logs.len() <= GameLog::ALL.len()
                            && logs
                                .iter()
                                .enumerate()
                                .all(|(i, log)| !logs[..i].contains(log)),
                        "Select unique --game-log names with --game-log-root."
                    );
                }
            }
            let next = Discovery {
                config,
                sessions: HashSet::new(),
                unavailable: false,
            };
            ensure!(
                !discovery
                    .iter()
                    .any(|old: &Discovery| old.kind() == next.kind() || old.root() == next.root()),
                "Discovery roots and kinds must be unique."
            );
            discovery.push(next);
        }
        let mut names = HashSet::new();
        let mut paths = HashSet::new();
        let mut cursors = Vec::new();
        for mut source in sources {
            let mut chars = source.name.bytes();
            ensure!(
                chars.next().is_some_and(|c| c.is_ascii_lowercase())
                    && chars.all(|c| c.is_ascii_lowercase() || c.is_ascii_digit() || c == b'-'),
                "Invalid source name."
            );
            source.path = absolute(&source.path)?;
            ensure!(
                names.insert(source.name.clone()) && paths.insert(source.path.clone()),
                "Supply unique source names and paths."
            );
            ensure!(
                !discovery
                    .iter()
                    .any(|root| source.name.starts_with(&format!("{}-", root.kind()))),
                "Source names beginning recorder- or game- are reserved with their discovery root."
            );
            ensure!(
                source.path != directory.join("journal.jsonl"),
                "A journal cannot collect its own output."
            );
            cursors.push(Cursor::new(source, from));
        }
        Ok(Self {
            directory,
            cursors,
            from,
            max_bytes,
            discovery,
            sink: None,
            bytes: 0,
            seq: 0,
            started: Instant::now(),
            closed: false,
            heartbeat: None,
        })
    }
    pub fn start(&mut self) -> Result<()> {
        DirBuilder::new().mode(0o700).create(&self.directory)?;
        self.sink = Some(
            OpenOptions::new()
                .write(true)
                .create_new(true)
                .mode(0o600)
                .open(self.directory.join("journal.jsonl"))?,
        );
        let mut data = json!({"schema":1,"from":self.from,"maxBytes":self.max_bytes,"sources":self.cursors.iter().map(|c| &c.source).collect::<Vec<_>>()});
        for discovery in &self.discovery {
            match &discovery.config {
                DiscoveryRoot::Recorder(root) => data["recorderRoot"] = json!(root),
                DiscoveryRoot::GameLogs { root, logs } => {
                    data["gameLogRoot"] = json!(root);
                    data["gameLogs"] = json!(logs);
                }
            }
        }
        self.record("journal.start", data, true)?;
        self.discover(true)
    }
    fn record(&mut self, kind: &str, data: Value, reserve: bool) -> Result<()> {
        let sink = self.sink.as_mut().context("Journal is not open.")?;
        self.seq += 1;
        let mut line = serde_json::to_vec(
            &json!({"seq":self.seq,"utc":chrono::Utc::now().to_rfc3339_opts(chrono::SecondsFormat::Millis,true),"elapsedMS":self.started.elapsed().as_secs_f64()*1000.0,"kind":kind,"data":data}),
        )?;
        line.push(b'\n');
        ensure!(
            self.bytes + line.len() as u64 <= self.max_bytes - if reserve { 2048 } else { 0 },
            "Journal capacity exhausted."
        );
        sink.write_all(&line)?;
        self.bytes += line.len() as u64;
        Ok(())
    }
    fn discover(&mut self, initial: bool) -> Result<()> {
        let mut roots = std::mem::take(&mut self.discovery);
        let result = roots
            .iter_mut()
            .try_for_each(|root| self.discover_root(root, initial));
        self.discovery = roots;
        result
    }
    fn discover_root(&mut self, discovery: &mut Discovery, initial: bool) -> Result<()> {
        let kind = discovery.kind();
        let root = discovery.root().to_owned();
        let Some(directory) = directory_handle(&root)? else {
            if !discovery.unavailable {
                self.record(&format!("{kind}.root.unavailable"), json!({}), true)?;
            }
            discovery.unavailable = true;
            return Ok(());
        };
        let mut entries = Vec::new();
        for entry in fs::read_dir(descriptor_path(&directory))? {
            ensure!(
                entries.len() < SCAN_LIMIT,
                "Discovery scan capacity exhausted ({SCAN_LIMIT} entries)."
            );
            entries.push(entry?);
        }
        if discovery.unavailable {
            self.record(&format!("{kind}.root.available"), json!({}), true)?;
        }
        discovery.unavailable = false;
        entries.sort_by_key(|entry| entry.file_name());
        for entry in entries {
            let Some(name) = entry.file_name().to_str().map(str::to_owned) else {
                continue;
            };
            let candidate = match &discovery.config {
                DiscoveryRoot::Recorder(_) => session_name(&name),
                DiscoveryRoot::GameLogs { .. } => game_session_name(&name),
            };
            if !candidate {
                continue;
            }
            let Some(meta) = optional_metadata(&entry.path())? else {
                continue;
            };
            ensure!(
                !meta.file_type().is_symlink(),
                "Discovery session must be a directory, not a symlink."
            );
            if matches!(discovery.config, DiscoveryRoot::GameLogs { .. }) && !meta.is_dir() {
                continue;
            }
            ensure!(
                meta.is_dir(),
                "Recorder session must be a directory, not a symlink."
            );
            if discovery.sessions.contains(&name) {
                continue;
            }
            ensure!(
                discovery.sessions.len() < SESSION_LIMIT,
                "{kind} session capacity exhausted ({SESSION_LIMIT} sessions). Start a new capture."
            );
            let files = match &discovery.config {
                DiscoveryRoot::Recorder(_) => {
                    vec![(name.replacen("flight-", "", 1), "events.jsonl")]
                }
                DiscoveryRoot::GameLogs { logs, .. } => {
                    let encoded: String = name.bytes().map(|b| format!("{b:02x}")).collect();
                    logs.iter()
                        .map(|log| (format!("game-{encoded}-{}", log.key()), log.filename()))
                        .collect()
                }
            };
            for (source_name, filename) in files {
                let source = Source {
                    name: source_name,
                    path: root.join(&name).join(filename),
                };
                ensure!(
                    !self
                        .cursors
                        .iter()
                        .any(|c| c.source.path == source.path || c.source.name == source.name),
                    "Discovery source duplicates an explicit or discovered source."
                );
                let from = if initial { self.from } else { From::Start };
                self.record(
                    &format!("{kind}.discovered"),
                    json!({"session":name,"name":source.name,"path":source.path,"from":from}),
                    true,
                )?;
                self.cursors.push(Cursor::new(source, from));
            }
            discovery.sessions.insert(name);
        }
        Ok(())
    }
    fn reset(&mut self, c: &mut Cursor, kind: &str) -> Result<()> {
        c.generation += 1;
        self.record(kind,json!({"source":c.source.name,"previousOffset":c.offset,"generation":c.generation,"continuity":"unknown"}),true)?;
        c.offset = 0;
        c.anchor = None;
        Ok(())
    }
    fn drain(&mut self, c: &mut Cursor) -> Result<()> {
        let size = c
            .handle
            .as_ref()
            .context("Source descriptor missing")?
            .metadata()?
            .len();
        if size < c.offset {
            self.reset(c, "source.truncated")?;
        } else if let Some(anchor) = &c.anchor {
            let mut check = vec![0; anchor.len()];
            let n = c
                .handle
                .as_ref()
                .context("Source descriptor missing")?
                .read_at(&mut check, c.offset - anchor.len() as u64)?;
            if n != check.len() || &check != anchor {
                self.reset(c, "source.rewritten")?;
            }
        }
        let mut bytes = vec![0; size.saturating_sub(c.offset).min(32768) as usize];
        if bytes.is_empty() {
            return Ok(());
        }
        let n = c
            .handle
            .as_ref()
            .context("Source descriptor missing")?
            .read_at(&mut bytes, c.offset)?;
        bytes.truncate(n);
        if n == 0 {
            return Ok(());
        }
        self.record("source.bytes",json!({"source":c.source.name,"generation":c.generation,"identity":c.identity,"offset":c.offset,"base64":STANDARD.encode(&bytes)}),true)?;
        c.offset += n as u64;
        c.anchor = Some(bytes[bytes.len().saturating_sub(64)..].to_vec());
        Ok(())
    }
    fn poll_source(&mut self, c: &mut Cursor) -> Result<()> {
        use nix::fcntl::{OFlag, openat};
        use nix::sys::stat::Mode;
        let parent = c.source.path.parent().context("Source parent missing")?;
        let filename = c
            .source
            .path
            .file_name()
            .context("Source filename missing")?
            .to_owned();
        let directory = directory_handle(parent)?;
        let path_meta = directory
            .as_ref()
            .map(|dir| optional_metadata(&descriptor_path(dir).join(&filename)))
            .transpose()?
            .flatten();
        self.poll_source_at(c, path_meta, || {
            let directory = directory.as_ref().ok_or(std::io::ErrorKind::NotFound)?;
            openat(
                directory,
                Path::new(&filename),
                OFlag::O_RDONLY | OFlag::O_NOFOLLOW | OFlag::O_NONBLOCK | OFlag::O_CLOEXEC,
                Mode::empty(),
            )
            .map(File::from)
            .map_err(std::io::Error::from)
        })
    }
    #[cfg(test)]
    fn poll_source_with(
        &mut self,
        c: &mut Cursor,
        open: impl FnOnce(&Path) -> std::io::Result<File>,
    ) -> Result<()> {
        let path_meta = optional_metadata(&c.source.path)?;
        let path = c.source.path.clone();
        self.poll_source_at(c, path_meta, || open(&path))
    }
    fn poll_source_at(
        &mut self,
        c: &mut Cursor,
        path_meta: Option<fs::Metadata>,
        open: impl FnOnce() -> std::io::Result<File>,
    ) -> Result<()> {
        if let Some(meta) = &path_meta {
            ensure!(
                meta.is_file(),
                "Source {} must be a regular file, not a symlink or device.",
                c.source.name
            );
        }
        let path_identity = path_meta.as_ref().map(identity);
        if c.handle.is_some() {
            self.drain(c)?;
            if path_identity != c.identity {
                if c.offset
                    < c.handle
                        .as_ref()
                        .context("Source descriptor missing")?
                        .metadata()?
                        .len()
                {
                    return Ok(());
                }
                c.handle = None;
                self.reset(c, "source.replaced")?;
            }
        }
        if c.handle.is_none() && path_meta.is_some() {
            let opened = open();
            let handle = match opened {
                Ok(handle) => handle,
                Err(e) if e.kind() == std::io::ErrorKind::NotFound => {
                    self.reset(c, "source.open-raced")?;
                    c.unavailable = Some(true);
                    return Ok(());
                }
                Err(e) => return Err(e.into()),
            };
            let actual = handle.metadata()?;
            ensure!(
                actual.is_file(),
                "Source {} must be a regular file.",
                c.source.name
            );
            if Some(identity(&actual)) != path_identity {
                self.reset(c, "source.open-raced")?;
            }
            c.identity = Some(identity(&actual));
            c.handle = Some(handle);
            if c.generation == 0 && c.unavailable.is_none() && c.from == From::End {
                c.offset = actual.len();
            }
            self.record("source.open",json!({"source":c.source.name,"identity":c.identity,"offset":c.offset,"generation":c.generation}),true)?;
            c.unavailable = Some(false);
            self.drain(c)?;
        } else if path_meta.is_none() && c.unavailable != Some(true) {
            self.record("source.unavailable", json!({"source":c.source.name}), true)?;
            c.unavailable = Some(true);
        }
        Ok(())
    }
    pub fn poll(&mut self) -> Result<()> {
        ensure!(!self.closed, "Journal is closed.");
        let before = self.seq;
        self.discover(false)?;
        // Move cursors out only during the sequential drain; always restore them on error.
        let mut cursors = std::mem::take(&mut self.cursors);
        let result = cursors.iter_mut().try_for_each(|c| self.poll_source(c));
        self.cursors = cursors;
        result?;
        if self
            .heartbeat
            .is_none_or(|last| last.elapsed() >= Duration::from_secs(1))
        {
            self.record("journal.heartbeat",json!({"offsets":self.cursors.iter().map(|c|json!({"source":c.source.name,"generation":c.generation,"offset":c.offset,"available":c.handle.is_some()})).collect::<Vec<_>>()}),true)?;
            self.heartbeat = Some(Instant::now());
        }
        if self.seq != before {
            self.sink
                .as_ref()
                .context("Journal is not open.")?
                .sync_all()?;
        }
        Ok(())
    }
    pub fn close(&mut self, reason: &str) -> Result<()> {
        if self.closed {
            return Ok(());
        }
        self.closed = true;
        let result = (|| {
            if self.sink.is_some() {
                self.record("journal.stop", json!({"reason":reason}), false)?;
                self.sink
                    .as_ref()
                    .context("Journal is not open.")?
                    .sync_all()?;
            }
            Ok(())
        })();
        for c in &mut self.cursors {
            c.handle = None;
        }
        self.sink = None;
        result
    }
}
fn session_name(name: &str) -> bool {
    let Some(rest) = name.strip_prefix("flight-recorder-") else {
        return false;
    };
    let Some((pid, nonce)) = rest.split_once('-') else {
        return false;
    };
    !pid.starts_with('0')
        && !pid.is_empty()
        && pid.bytes().all(|c| c.is_ascii_digit())
        && nonce.len() == 32
        && nonce
            .bytes()
            .all(|c| c.is_ascii_digit() || (b'a'..=b'f').contains(&c))
}
fn game_session_name(name: &str) -> bool {
    !name.is_empty()
        && name.len() <= 128
        && name
            .bytes()
            .all(|c| c.is_ascii_alphanumeric() || c == b'_' || c == b'-')
}
pub fn run(args: &[OsString]) -> Result<()> {
    ensure!(
        args.len().is_multiple_of(2),
        "Each option requires a value."
    );
    let mut directory = None;
    let mut from = None;
    let mut max_bytes = None;
    let mut recorder_root = None;
    let mut game_root = None;
    let mut game_logs = Vec::new();
    let mut sources = Vec::new();
    for pair in args.as_chunks::<2>().0 {
        let option = pair[0].to_str().context("Invalid option encoding")?;
        ensure!(!pair[1].is_empty(), "Each option requires a value.");
        match option {
            "--output" => {
                ensure!(directory.is_none(), "Duplicate --output.");
                directory = Some(PathBuf::from(&pair[1]));
            }
            "--recorder-root" => {
                ensure!(recorder_root.is_none(), "Duplicate --recorder-root.");
                recorder_root = Some(PathBuf::from(&pair[1]));
            }
            "--game-log-root" => {
                ensure!(game_root.is_none(), "Duplicate --game-log-root.");
                game_root = Some(PathBuf::from(&pair[1]));
            }
            "--game-log" => game_logs.push(GameLog::parse(&pair[1])?),
            "--from" => {
                ensure!(from.is_none(), "Choose --from start or end once.");
                from = Some(match pair[1].to_str() {
                    Some("start") => From::Start,
                    Some("end") => From::End,
                    _ => bail!("Choose --from start or end once."),
                });
            }
            "--max-bytes" => {
                let text = pair[1].to_str().context("Invalid --max-bytes.")?;
                ensure!(
                    max_bytes.is_none()
                        && !text.starts_with('0')
                        && !text.is_empty()
                        && text.bytes().all(|c| c.is_ascii_digit()),
                    "Invalid --max-bytes."
                );
                max_bytes = Some(text.parse::<u64>()?);
            }
            "--source" => {
                use std::os::unix::ffi::{OsStrExt, OsStringExt};
                let value = pair[1].as_bytes();
                let split = value
                    .iter()
                    .position(|c| *c == b'=')
                    .filter(|n| *n > 0)
                    .context("Use --source name=/absolute/file.")?;
                sources.push(Source {
                    name: std::str::from_utf8(&value[..split])?.to_owned(),
                    path: PathBuf::from(OsString::from_vec(value[split + 1..].to_vec())),
                });
            }
            _ => bail!("Unknown option {option}."),
        }
    }
    let mut roots = Vec::new();
    if let Some(root) = recorder_root {
        roots.push(DiscoveryRoot::Recorder(root));
    }
    match game_root {
        Some(root) => roots.push(DiscoveryRoot::GameLogs {
            root,
            logs: game_logs,
        }),
        None => ensure!(game_logs.is_empty(), "--game-log requires --game-log-root."),
    }
    let mut journal = InputJournal::new(
        directory.context("Missing --output")?,
        sources,
        from.context("Missing --from")?,
        max_bytes.context("Missing --max-bytes")?,
        roots,
    )?;
    let stopping = Arc::new(AtomicBool::new(false));
    let term = signal_hook::flag::register(signal_hook::consts::SIGTERM, Arc::clone(&stopping))?;
    let interrupt =
        signal_hook::flag::register(signal_hook::consts::SIGINT, Arc::clone(&stopping))?;
    let result = (|| {
        journal.start()?;
        while !stopping.load(Ordering::Relaxed) {
            journal.poll()?;
            std::thread::sleep(Duration::from_millis(250));
        }
        Ok(())
    })();
    signal_hook::low_level::unregister(term);
    signal_hook::low_level::unregister(interrupt);
    let reason = match &result {
        Ok(()) => "signal".to_owned(),
        Err(error) => format!("{error}"),
    };
    let close = journal.close(&reason);
    result.and(close)
}

#[cfg(test)]
mod tests {
    use super::*;
    use proptest::prelude::*;
    proptest! {
        #![proptest_config(ProptestConfig::with_cases(128))]
        #[test]
        fn game_session_names_never_introduce_a_path_component(name in ".{0,256}") {
            if game_session_name(&name) {
                let path = Path::new(&name);
                prop_assert_eq!(path.components().count(), 1);
                prop_assert!(matches!(path.components().next(), Some(Component::Normal(_))));
                prop_assert!(name.is_ascii() && name.len() <= 128);
            }
        }
        #[test]
        fn game_log_parser_cannot_select_arbitrary_files(name in ".{0,256}") {
            let parsed = GameLog::parse(&OsString::from(&name));
            prop_assert_eq!(parsed.is_ok(), ["Power.log", "LoadingScreen.log", "Arena.log", "Achievements.log"].contains(&name.as_str()));
            if let Ok(log) = parsed { prop_assert_eq!(serde_json::to_value(log).unwrap(), json!(name)); }
        }
    }
    #[test]
    fn nonblocking_open_rejects_file_replaced_with_fifo() {
        let temp = tempfile::tempdir().unwrap();
        let source = temp.path().join("source");
        fs::write(&source, "ordinary file").unwrap();
        let mut journal = InputJournal::new(
            temp.path().join("journal"),
            vec![Source {
                name: "hdt".into(),
                path: source.clone(),
            }],
            From::Start,
            65536,
            vec![],
        )
        .unwrap();
        journal.start().unwrap();
        let mut cursor = journal.cursors.remove(0);
        let error = journal
            .poll_source_with(&mut cursor, |path| {
                fs::remove_file(path)?;
                nix::unistd::mkfifo(
                    path,
                    nix::sys::stat::Mode::S_IRUSR | nix::sys::stat::Mode::S_IWUSR,
                )?;
                OpenOptions::new()
                    .read(true)
                    .custom_flags(nix::libc::O_NOFOLLOW | nix::libc::O_NONBLOCK)
                    .open(path)
            })
            .unwrap_err();
        journal.cursors.push(cursor);
        assert!(error.to_string().contains("regular file"));
        journal.close("rejected fifo").unwrap();
    }
    #[test]
    fn replacement_or_disappearance_between_stat_and_open_preserves_new_bytes() {
        for replacement in [false, true] {
            let temp = tempfile::tempdir().unwrap();
            let source = temp.path().join("state.json");
            fs::write(&source, "old").unwrap();
            let output = temp.path().join("journal");
            let mut journal = InputJournal::new(
                output.clone(),
                vec![Source {
                    name: "state".into(),
                    path: source.clone(),
                }],
                From::End,
                1_048_576,
                vec![],
            )
            .unwrap();
            journal.start().unwrap();
            let mut cursor = journal.cursors.remove(0);
            journal
                .poll_source_with(&mut cursor, |path| {
                    fs::rename(path, temp.path().join("old"))?;
                    if replacement {
                        fs::write(path, "new")?;
                    }
                    OpenOptions::new()
                        .read(true)
                        .custom_flags(nix::libc::O_NOFOLLOW | nix::libc::O_NONBLOCK)
                        .open(path)
                })
                .unwrap();
            journal.cursors.push(cursor);
            if !replacement {
                fs::write(&source, "new").unwrap();
            }
            journal.poll().unwrap();
            journal.close("test").unwrap();
            let rows: Vec<Value> = fs::read_to_string(output.join("journal.jsonl"))
                .unwrap()
                .lines()
                .map(|line| serde_json::from_str(line).unwrap())
                .collect();
            assert!(
                rows.iter().any(|row| row["kind"] == "source.open-raced"
                    && row["data"]["continuity"] == "unknown")
            );
            let bytes: Vec<u8> = rows
                .iter()
                .filter(|row| row["kind"] == "source.bytes" && row["data"]["generation"] == 1)
                .flat_map(|row| {
                    STANDARD
                        .decode(row["data"]["base64"].as_str().unwrap())
                        .unwrap()
                })
                .collect();
            assert_eq!(bytes, b"new");
        }
    }
}
