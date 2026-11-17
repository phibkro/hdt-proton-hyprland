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
    os::unix::fs::{DirBuilderExt, FileExt, MetadataExt, OpenOptionsExt},
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
    recorder_root: Option<PathBuf>,
    sink: Option<File>,
    bytes: u64,
    seq: u64,
    started: Instant,
    closed: bool,
    heartbeat: Option<Instant>,
    sessions: HashSet<String>,
    root_unavailable: bool,
}
impl InputJournal {
    pub fn new(
        directory: PathBuf,
        sources: Vec<Source>,
        from: From,
        max_bytes: u64,
        recorder_root: Option<PathBuf>,
    ) -> Result<Self> {
        ensure!(
            (65_536..=9_007_199_254_740_991).contains(&max_bytes),
            "Journal capacity must be at least 65536 bytes and a safe integer."
        );
        ensure!(
            !sources.is_empty() || recorder_root.is_some(),
            "Supply unique source names and paths."
        );
        let directory = absolute(&directory)?;
        let recorder_root = recorder_root.map(|p| absolute(&p)).transpose()?;
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
                recorder_root.is_none() || !source.name.starts_with("recorder-"),
                "Source names beginning recorder- are reserved with --recorder-root."
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
            recorder_root,
            sink: None,
            bytes: 0,
            seq: 0,
            started: Instant::now(),
            closed: false,
            heartbeat: None,
            sessions: HashSet::new(),
            root_unavailable: false,
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
        if let Some(root) = &self.recorder_root {
            data["recorderRoot"] = json!(root);
        }
        self.record("journal.start", data, true)?;
        self.discover(true)
    }
    fn record(&mut self, kind: &str, data: Value, reserve: bool) -> Result<()> {
        let sink = self.sink.as_mut().context("Journal is not open.")?;
        self.seq += 1;
        let mut line = serde_json::to_vec(
            &json!({"seq":self.seq,"utc":crate::timestamp::Timestamp::now().utc_millis(),"elapsedMS":self.started.elapsed().as_secs_f64()*1000.0,"kind":kind,"data":data}),
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
        let Some(root) = self.recorder_root.clone() else {
            return Ok(());
        };
        let Some(meta) = optional_metadata(&root)? else {
            if !self.root_unavailable {
                self.record("recorder.root.unavailable", json!({}), true)?;
            }
            self.root_unavailable = true;
            return Ok(());
        };
        ensure!(
            meta.is_dir(),
            "Recorder root must be a directory, not a symlink."
        );
        let mut entries = match fs::read_dir(&root)
            .and_then(|entries| entries.collect::<std::io::Result<Vec<_>>>())
        {
            Ok(entries) => entries,
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => {
                if !self.root_unavailable {
                    self.record("recorder.root.unavailable", json!({}), true)?;
                }
                self.root_unavailable = true;
                return Ok(());
            }
            Err(error) => return Err(error.into()),
        };
        if self.root_unavailable {
            self.record("recorder.root.available", json!({}), true)?;
        }
        self.root_unavailable = false;
        entries.sort_by_key(|entry| entry.file_name());
        for entry in entries {
            let Some(name) = entry.file_name().to_str().map(str::to_owned) else {
                continue;
            };
            if !session_name(&name) || self.sessions.contains(&name) {
                continue;
            }
            let Some(meta) = optional_metadata(&entry.path())? else {
                continue;
            };
            ensure!(
                meta.is_dir(),
                "Recorder session must be a directory, not a symlink."
            );
            ensure!(
                self.sessions.len() < 128,
                "Recorder session capacity exhausted (128 sessions). Start a new capture."
            );
            let source = Source {
                name: name.replacen("flight-", "", 1),
                path: entry.path().join("events.jsonl"),
            };
            ensure!(
                !self.cursors.iter().any(|c| c.source.path == source.path),
                "Recorder session duplicates an explicit source path."
            );
            let from = if initial { self.from } else { From::Start };
            self.record(
                "recorder.discovered",
                json!({"name":source.name,"path":source.path,"from":from}),
                true,
            )?;
            self.cursors.push(Cursor::new(source, from));
            self.sessions.insert(name);
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
        self.poll_source_with(c, |path| {
            OpenOptions::new()
                .read(true)
                .custom_flags(nix::libc::O_NOFOLLOW | nix::libc::O_NONBLOCK)
                .open(path)
        })
    }
    fn poll_source_with(
        &mut self,
        c: &mut Cursor,
        open: impl FnOnce(&Path) -> std::io::Result<File>,
    ) -> Result<()> {
        let path_meta = optional_metadata(&c.source.path)?;
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
            let opened = open(&c.source.path);
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
pub fn run(args: &[OsString]) -> Result<()> {
    ensure!(
        args.len().is_multiple_of(2),
        "Each option requires a value."
    );
    let mut directory = None;
    let mut from = None;
    let mut max_bytes = None;
    let mut recorder_root = None;
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
    let mut journal = InputJournal::new(
        directory.context("Missing --output")?,
        sources,
        from.context("Missing --from")?,
        max_bytes.context("Missing --max-bytes")?,
        recorder_root,
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
            None,
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
                None,
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
