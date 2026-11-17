//! Explicit private native session: sibling UI lifetimes, no game facts relayed.
use crate::snapshot;
use anyhow::{Context, Result, bail, ensure};
use nix::{
    fcntl::{Flock, FlockArg},
    sys::{
        signal::{Signal, killpg},
        wait::{WaitPidFlag, WaitStatus, waitpid},
    },
    unistd::Pid,
};
use serde::{Deserialize, Serialize};
use std::{
    ffi::OsString,
    fs::{self, OpenOptions},
    io::{BufRead, BufReader, Read, Write},
    os::unix::{
        fs::{MetadataExt, OpenOptionsExt},
        process::{CommandExt, ExitStatusExt},
    },
    path::{Path, PathBuf},
    process::{Child, Command, Stdio},
    sync::{
        Arc,
        atomic::{AtomicBool, Ordering},
        mpsc::{self, SyncSender},
    },
    time::{Duration, Instant},
};
const LINE: usize = 8192;
/// Opt-in for managed-owned Rust helpers. A dead/reparented caller cannot be
/// mistaken for the intended owner, including the spawn-to-exec race.
pub fn bind_owned_parent() -> Result<()> {
    let Some(expected) = std::env::var_os("HDT_OWNED_PARENT_PID") else {
        return Ok(());
    };
    let expected: i32 = expected
        .to_str()
        .context("invalid owned parent PID")?
        .parse()?;
    ensure!(
        expected > 1 && nix::unistd::getppid().as_raw() == expected,
        "owned helper parent no longer exists"
    );
    nix::sys::prctl::set_pdeathsig(Some(Signal::SIGKILL))?;
    ensure!(
        nix::unistd::getppid().as_raw() == expected,
        "owned helper parent exited during preparation"
    );
    Ok(())
}
#[derive(Clone, Copy, Debug, Deserialize, Serialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
enum Role {
    Main,
    Overlay,
}
impl Role {
    fn name(self) -> &'static str {
        match self {
            Self::Main => "main",
            Self::Overlay => "overlay",
        }
    }
}
#[derive(Clone, Deserialize, Serialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct Game {
    window: u32,
    pid: u32,
    class: String,
}
#[derive(Clone, Deserialize, Serialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct Config {
    schema_version: u8,
    session_root: PathBuf,
    dotnet: PathBuf,
    application: PathBuf,
    observation_file: PathBuf,
    game: Game,
}
impl Config {
    fn read(path: &Path) -> Result<Self> {
        let value: Self = serde_json::from_slice(
            &snapshot::read(path, 16384)?.context("native session configuration missing")?,
        )?;
        ensure!(
            value.schema_version == 1
                && value.game.window > 0
                && value.game.pid > 0
                && !value.game.class.is_empty()
                && value.game.class.len() <= 128,
            "invalid explicit native session configuration"
        );
        for path in [
            &value.session_root,
            &value.dotnet,
            &value.application,
            &value.observation_file,
        ] {
            ensure!(
                path.is_absolute() && path.as_os_str().len() <= 4096,
                "session paths must be explicit bounded absolute paths"
            );
        }
        let root = fs::symlink_metadata(&value.session_root)?;
        ensure!(
            root.is_dir()
                && root.uid() == nix::unistd::getuid().as_raw()
                && root.mode() & 0o077 == 0,
            "session root must be an existing owned private directory"
        );
        ensure!(
            value.dotnet.is_file() && value.application.is_file(),
            "configured native runtime/application unavailable"
        );
        Ok(value)
    }
}
#[derive(Deserialize)]
#[serde(
    tag = "command",
    rename_all = "camelCase",
    rename_all_fields = "camelCase",
    deny_unknown_fields
)]
enum Action {
    Status { schema_version: u8 },
    Restart { schema_version: u8, role: Role },
    Shutdown { schema_version: u8 },
}
#[derive(Deserialize, Clone, Serialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
enum ReportKind {
    Ready,
    Heartbeat,
}
#[derive(Deserialize, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
enum ProjectionKind {
    Starting,
    Main,
    Facts,
    Inactive,
    SourceUnavailable,
}
#[derive(Deserialize, Clone, Serialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct Report {
    schema_version: u8,
    kind: ReportKind,
    role: Role,
    instance: String,
    pid: u32,
    ui_sequence: u64,
    ui_responsive: bool,
    window: u32,
    projection: ProjectionKind,
    main_page: Option<u8>,
    source_sequence: Option<i32>,
    scores: Option<bool>,
    synergies: Option<bool>,
}
enum Event {
    Input(Result<Option<Action>, String>),
    Child(Role, String, Result<Report, String>),
}
#[derive(Clone, Copy, Serialize)]
#[serde(rename_all = "camelCase")]
enum StopReason {
    ProcessExit,
    ProtocolFailure,
    ReadinessTimeout,
    ExplicitRestart,
    SessionShutdown,
}
struct Slot {
    role: Role,
    child: Option<Child>,
    instance: String,
    started: Instant,
    last: Option<(Report, Instant)>,
    ready: bool,
    exit: Option<i32>,
    stop_reason: Option<StopReason>,
    fault: Arc<AtomicBool>,
    workers: Vec<std::thread::JoinHandle<()>>,
}
impl Slot {
    fn new(role: Role) -> Self {
        Self {
            role,
            child: None,
            instance: String::new(),
            started: Instant::now(),
            last: None,
            ready: false,
            exit: None,
            stop_reason: None,
            fault: Arc::new(AtomicBool::new(false)),
            workers: Vec::new(),
        }
    }
    fn start(&mut self, config: &Config, launch: &Path, events: SyncSender<Event>) -> Result<()> {
        ensure!(
            self.child.is_none() && self.workers.is_empty(),
            "old native role must be reaped before restart"
        );
        self.instance = uuid::Uuid::new_v4().to_string();
        self.ready = false;
        self.last = None;
        self.exit = None;
        self.stop_reason = None;
        self.started = Instant::now();
        self.fault = Arc::new(AtomicBool::new(false));
        let mut command = Command::new(std::env::current_exe()?);
        command.args([
            OsString::from("native-session"),
            OsString::from("child"),
            OsString::from(self.role.name()),
            OsString::from(&self.instance),
            OsString::from(std::process::id().to_string()),
            launch.as_os_str().to_owned(),
        ]);
        command
            .env_clear()
            .env("HOME", &config.session_root)
            .env("DOTNET_PROCESSOR_COUNT", "2")
            .env("DOTNET_SYSTEM_GLOBALIZATION_INVARIANT", "1")
            .env("DOTNET_CLI_TELEMETRY_OPTOUT", "1")
            .env("HDT_HOST_BINARY", std::env::current_exe()?);
        for name in [
            "DISPLAY",
            "XAUTHORITY",
            "LD_LIBRARY_PATH",
            "FONTCONFIG_FILE",
            "DOTNET_ROOT",
        ] {
            if let Some(value) = std::env::var_os(name) {
                command.env(name, value);
            }
        }
        command
            .stdin(Stdio::null())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped());
        let mut child = command.spawn()?;
        let stdout = child.stdout.take().context("missing role stdout")?;
        let stderr = child.stderr.take().context("missing role stderr")?;
        let role = self.role;
        let instance = self.instance.clone();
        let fault = Arc::clone(&self.fault);
        self.workers.push(std::thread::spawn(move || {
            let mut reader = BufReader::new(stdout);
            loop {
                let result = read_line(&mut reader).and_then(|line| {
                    line.map(|bytes| {
                        serde_json::from_slice::<Report>(&bytes).map_err(anyhow::Error::from)
                    })
                    .transpose()
                });
                match result {
                    Ok(None) => {
                        fault.store(true, Ordering::Relaxed);
                        break;
                    }
                    result => {
                        let failed = result.is_err();
                        if events
                            .try_send(Event::Child(
                                role,
                                instance.clone(),
                                result
                                    .and_then(|value| value.context("closed native role stream"))
                                    .map_err(|error| error.to_string()),
                            ))
                            .is_err()
                        {
                            fault.store(true, Ordering::Relaxed);
                            break;
                        }
                        if failed {
                            break;
                        }
                    }
                }
            }
        }));
        let fault = Arc::clone(&self.fault);
        let diagnostics =
            config
                .session_root
                .join(format!("{}-{}.stderr", self.role.name(), self.instance));
        self.workers.push(std::thread::spawn(move || {
            let mut bytes = Vec::new();
            let result = stderr.take(65537).read_to_end(&mut bytes);
            if result.is_err() || bytes.len() > 65536 {
                fault.store(true, Ordering::Relaxed);
            }
            bytes.truncate(65536);
            let _ = fs::write(diagnostics, bytes);
        }));
        self.child = Some(child);
        Ok(())
    }
    fn stop(&mut self, reason: StopReason) -> Result<()> {
        let Some(mut child) = self.child.take() else {
            return Ok(());
        };
        self.stop_reason = Some(reason);
        let group = Pid::from_raw(child.id() as i32);
        let _ = killpg(group, Signal::SIGCONT);
        let _ = nix::sys::signal::kill(group, Signal::SIGCONT);
        let _ = killpg(group, Signal::SIGTERM);
        let _ = nix::sys::signal::kill(group, Signal::SIGTERM);
        let deadline = Instant::now() + Duration::from_secs(2);
        loop {
            if let Some(status) = child.try_wait()? {
                self.exit = Some(
                    status
                        .code()
                        .unwrap_or_else(|| 128 + status.signal().unwrap_or(0)),
                );
                break;
            }
            if Instant::now() >= deadline {
                let _ = killpg(group, Signal::SIGKILL);
                let _ = child.kill();
                let status = child.wait()?;
                self.exit = Some(
                    status
                        .code()
                        .unwrap_or_else(|| 128 + status.signal().unwrap_or(0)),
                );
                break;
            }
            std::thread::sleep(Duration::from_millis(10));
        }
        // The session is a subreaper: collect adopted descendants in this exact group.
        let _ = killpg(group, Signal::SIGKILL);
        let deadline = Instant::now() + Duration::from_secs(2);
        loop {
            match waitpid(Pid::from_raw(-group.as_raw()), Some(WaitPidFlag::WNOHANG)) {
                Ok(WaitStatus::StillAlive) => {
                    ensure!(
                        Instant::now() < deadline,
                        "owned role descendants did not exit"
                    );
                    std::thread::sleep(Duration::from_millis(10));
                }
                Ok(_) => continue,
                Err(nix::errno::Errno::ECHILD) => break,
                Err(error) => return Err(error.into()),
            }
        }
        self.ready = false;
        let deadline = Instant::now() + Duration::from_secs(1);
        while self.workers.iter().any(|worker| !worker.is_finished()) {
            ensure!(
                Instant::now() < deadline,
                "owned role pipe workers did not exit; replacement forbidden"
            );
            std::thread::sleep(Duration::from_millis(5));
        }
        for worker in self.workers.drain(..) {
            worker
                .join()
                .map_err(|_| anyhow::anyhow!("owned role pipe worker failed"))?;
        }
        Ok(())
    }
    fn status(&self) -> serde_json::Value {
        let health = if self.child.is_none() {
            "exited"
        } else if !self.ready {
            "starting"
        } else if self.last.as_ref().is_some_and(|(report, time)| {
            report.ui_responsive && time.elapsed() < Duration::from_secs(3)
        }) {
            "responsive"
        } else {
            "unresponsive"
        };
        serde_json::json!({"role":self.role,"pid":self.child.as_ref().map(Child::id),"instance":self.instance,"health":health,"exitCode":self.exit,"stopReason":self.stop_reason,"last":self.last.as_ref().map(|(report,_)|report)})
    }
}
impl Drop for Slot {
    fn drop(&mut self) {
        let _ = self.stop(StopReason::SessionShutdown);
    }
}
fn read_line(reader: &mut impl BufRead) -> Result<Option<Vec<u8>>> {
    let mut bytes = Vec::new();
    let count = reader
        .take((LINE + 1) as u64)
        .read_until(b'\n', &mut bytes)?;
    if count == 0 {
        return Ok(None);
    }
    ensure!(
        count <= LINE && bytes.last() == Some(&b'\n'),
        "incomplete or oversized native session line"
    );
    Ok(Some(bytes))
}
fn child(role: &str, instance: &str, owner: &str, path: &Path) -> Result<i32> {
    let role = match role {
        "main" => Role::Main,
        "overlay" => Role::Overlay,
        _ => bail!("unknown native child role"),
    };
    uuid::Uuid::parse_str(instance)?;
    let parent: i32 = owner.parse().context("invalid native owner PID")?;
    ensure!(
        parent > 1 && nix::unistd::getppid().as_raw() == parent,
        "native session owner exited before child preparation"
    );
    nix::sys::prctl::set_pdeathsig(Some(Signal::SIGKILL))?;
    ensure!(
        nix::unistd::getppid().as_raw() == parent,
        "native session owner exited during child preparation"
    );
    nix::unistd::setsid()?;
    let config = Config::read(path)?;
    let mut command = Command::new(&config.dotnet);
    command
        .arg(&config.application)
        .args([
            "--session-role",
            role.name(),
            "--session-instance",
            instance,
            "--settings",
        ])
        .arg(config.session_root.join("preferences.json"))
        .arg("--client-data")
        .arg(config.session_root.join("library.json"))
        .arg("--native-state")
        .arg(&config.observation_file);
    if role == Role::Overlay {
        command.args([
            "--game-window",
            &config.game.window.to_string(),
            "--game-pid",
            &config.game.pid.to_string(),
            "--game-class",
            &config.game.class,
        ]);
    }
    Err(command.exec()).context("native child exec failed")
}
fn owner(path: &Path) -> Result<i32> {
    let config = Config::read(path)?;
    let file = OpenOptions::new()
        .read(true)
        .write(true)
        .create(true)
        .truncate(false)
        .mode(0o600)
        .custom_flags(nix::libc::O_NOFOLLOW | nix::libc::O_NONBLOCK)
        .open(config.session_root.join("owner.lock"))?;
    ensure!(
        file.metadata()?.is_file(),
        "native session lease must be regular"
    );
    let _lease = Flock::lock(file, FlockArg::LockExclusiveNonblock).map_err(|(_, error)| error)?;
    let launch = config.session_root.join("launch.json");
    let temporary = config
        .session_root
        .join(format!("launch-{}.tmp", uuid::Uuid::new_v4()));
    let mut file = OpenOptions::new()
        .write(true)
        .create_new(true)
        .mode(0o600)
        .open(&temporary)?;
    file.write_all(&serde_json::to_vec(&config)?)?;
    file.sync_all()?;
    fs::rename(temporary, &launch)?;
    nix::sys::prctl::set_child_subreaper(true)?;
    let stopped = Arc::new(AtomicBool::new(false));
    let signals = [signal_hook::consts::SIGTERM, signal_hook::consts::SIGINT]
        .map(|signal| signal_hook::flag::register(signal, Arc::clone(&stopped)))
        .into_iter()
        .collect::<std::io::Result<Vec<_>>>()?;
    let (output_tx, output_rx) = mpsc::sync_channel::<serde_json::Value>(8);
    let output_stopped = Arc::clone(&stopped);
    let output_done = Arc::new(AtomicBool::new(false));
    let finished_output = Arc::clone(&output_done);
    let output_thread = std::thread::spawn(move || {
        let stdout = std::io::stdout();
        let mut output = stdout.lock();
        while let Ok(value) = output_rx.recv() {
            let result = (|| -> Result<()> {
                let bytes = serde_json::to_vec(&value)?;
                ensure!(bytes.len() <= LINE, "session output exceeds bound");
                output.write_all(&bytes)?;
                output.write_all(b"\n")?;
                output.flush()?;
                Ok(())
            })();
            if result.is_err() {
                output_stopped.store(true, Ordering::Relaxed);
                break;
            }
        }
        finished_output.store(true, Ordering::Relaxed);
    });
    let emit = |value| {
        output_tx
            .try_send(value)
            .map_err(|_| anyhow::anyhow!("native session control consumer stalled"))
    };
    let (tx, rx) = mpsc::sync_channel(32);
    let input_tx = tx.clone();
    let input_stopped = Arc::clone(&stopped);
    std::thread::spawn(move || {
        let stdin = std::io::stdin();
        let mut input = stdin.lock();
        loop {
            let result = read_line(&mut input).and_then(|line| {
                line.map(|bytes| {
                    serde_json::from_slice::<Action>(&bytes).map_err(anyhow::Error::from)
                })
                .transpose()
            });
            let finished = !matches!(result, Ok(Some(_)));
            if input_tx
                .try_send(Event::Input(result.map_err(|error| error.to_string())))
                .is_err()
            {
                input_stopped.store(true, Ordering::Relaxed);
                break;
            }
            if finished {
                break;
            }
        }
    });
    let mut slots = [Slot::new(Role::Main), Slot::new(Role::Overlay)];
    for slot in &mut slots {
        slot.start(&config, &launch, tx.clone())?;
    }
    emit(
        serde_json::json!({"schemaVersion":1,"kind":"started","ownerPid":std::process::id(),"roles":slots.iter().map(Slot::status).collect::<Vec<_>>()}),
    )?;
    let result = (|| -> Result<()> {
        while !stopped.load(Ordering::Relaxed) {
            while let Ok(event) = rx.try_recv() {
                match event {
                    Event::Input(Ok(None)) => stopped.store(true, Ordering::Relaxed),
                    Event::Input(Err(error)) => bail!("invalid native session command: {error}"),
                    Event::Input(Ok(Some(action))) => match action {
                        Action::Status { schema_version } => {
                            ensure!(schema_version == 1, "unknown session command version");
                            emit(
                                serde_json::json!({"schemaVersion":1,"kind":"status","ownerPid":std::process::id(),"roles":slots.iter().map(Slot::status).collect::<Vec<_>>()}),
                            )?;
                        }
                        Action::Shutdown { schema_version } => {
                            ensure!(schema_version == 1, "unknown session command version");
                            stopped.store(true, Ordering::Relaxed);
                        }
                        Action::Restart {
                            schema_version,
                            role,
                        } => {
                            ensure!(schema_version == 1, "unknown session command version");
                            let slot = slots
                                .iter_mut()
                                .find(|slot| slot.role == role)
                                .context("unknown native role")?;
                            slot.stop(StopReason::ExplicitRestart)?;
                            slot.start(&config, &launch, tx.clone())?;
                            emit(
                                serde_json::json!({"schemaVersion":1,"kind":"restarting","role":role,"pid":slot.child.as_ref().map(Child::id)}),
                            )?;
                        }
                    },
                    Event::Child(role, instance, message) => {
                        let slot = slots
                            .iter_mut()
                            .find(|slot| slot.role == role)
                            .context("unknown child role")?;
                        if slot.instance != instance {
                            continue;
                        }
                        match message {
                            Err(_) => slot.fault.store(true, Ordering::Relaxed),
                            Ok(report) => {
                                let valid = report.schema_version == 1
                                    && report.role == role
                                    && report.instance == instance
                                    && slot
                                        .child
                                        .as_ref()
                                        .is_some_and(|child| child.id() == report.pid)
                                    && report.window > 0
                                    && (!report.ui_responsive || report.ui_sequence > 0)
                                    && (report.kind != ReportKind::Ready || report.ui_responsive)
                                    && report.main_page.is_none_or(|page| page <= 5)
                                    && slot.last.as_ref().is_none_or(|(last, _)| {
                                        report.ui_sequence >= last.ui_sequence
                                    });
                                if !valid {
                                    slot.fault.store(true, Ordering::Relaxed);
                                } else {
                                    if report.kind == ReportKind::Ready {
                                        slot.ready = report.ui_responsive;
                                    }
                                    slot.last = Some((report, Instant::now()));
                                }
                            }
                        }
                    }
                }
            }
            for slot in &mut slots {
                if slot.child.is_some() {
                    let exited = slot
                        .child
                        .as_mut()
                        .context("missing role")?
                        .try_wait()?
                        .is_some();
                    if exited
                        || slot.fault.load(Ordering::Relaxed)
                        || !slot.ready && slot.started.elapsed() > Duration::from_secs(8)
                    {
                        let reason = if exited {
                            StopReason::ProcessExit
                        } else if slot.fault.load(Ordering::Relaxed) {
                            StopReason::ProtocolFailure
                        } else {
                            StopReason::ReadinessTimeout
                        };
                        slot.stop(reason)?;
                        emit(
                            serde_json::json!({"schemaVersion":1,"kind":"childExited","role":slot.role,"exitCode":slot.exit,"stopReason":slot.stop_reason}),
                        )?;
                    }
                }
            }
            std::thread::sleep(Duration::from_millis(10));
        }
        Ok(())
    })();
    for slot in &mut slots {
        slot.stop(StopReason::SessionShutdown)?;
    }
    for signal in signals {
        signal_hook::low_level::unregister(signal);
    }
    emit(serde_json::json!({"schemaVersion":1,"kind":"stopped","reaped":true}))?;
    drop(output_tx);
    let deadline = Instant::now() + Duration::from_secs(1);
    while !output_done.load(Ordering::Relaxed) && Instant::now() < deadline {
        std::thread::sleep(Duration::from_millis(5));
    }
    if output_done.load(Ordering::Relaxed) {
        let _ = output_thread.join();
    }
    result?;
    Ok(0)
}
pub fn run(args: &[OsString]) -> Result<i32> {
    match args {
        [action, path] if action == "run" => owner(Path::new(path)),
        [action, role, instance, owner, path] if action == "child" => child(
            role.to_str().context("invalid role")?,
            instance.to_str().context("invalid instance")?,
            owner.to_str().context("invalid owner PID")?,
            Path::new(path),
        ),
        _ => bail!("Usage: hdt-host native-session run CONFIG"),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn session_launch_requires_private_explicit_regular_configuration() {
        use std::os::unix::fs::PermissionsExt;
        let root = tempfile::tempdir().unwrap();
        fs::set_permissions(root.path(), fs::Permissions::from_mode(0o700)).unwrap();
        let config = root.path().join("config.json");
        let runtime = root.path().join("runtime");
        fs::write(&runtime, b"not executed by this boundary test").unwrap();
        let valid = serde_json::json!({"schemaVersion":1,"sessionRoot":root.path(),"dotnet":runtime,"application":runtime,"observationFile":root.path().join("absent.json"),"game":{"window":1,"pid":1,"class":"owned-fixture"}});
        fs::write(&config, serde_json::to_vec(&valid).unwrap()).unwrap();
        assert!(Config::read(&config).is_ok());
        for (key, value) in [
            ("schemaVersion", serde_json::json!(2)),
            ("sessionRoot", serde_json::json!("relative")),
            ("extra", serde_json::json!(true)),
        ] {
            let mut invalid = valid.clone();
            invalid[key] = value;
            fs::write(&config, serde_json::to_vec(&invalid).unwrap()).unwrap();
            assert!(Config::read(&config).is_err(), "accepted {key}");
        }
        fs::write(&config, vec![b'x'; 16385]).unwrap();
        assert!(Config::read(&config).is_err());
        assert!(Config::read(root.path()).is_err());
        fs::write(&config, serde_json::to_vec(&valid).unwrap()).unwrap();
        fs::set_permissions(root.path(), fs::Permissions::from_mode(0o755)).unwrap();
        assert!(Config::read(&config).is_err());
    }
    #[test]
    fn lifecycle_command_boundary_is_closed_versioned_and_bounded() {
        for bytes in [
            br#"{"command":"restart","schemaVersion":1,"role":"game"}"#.as_slice(),
            br#"{"command":"status","schemaVersion":1,"secret":true}"#.as_slice(),
            br#"{"command":"restart","schemaVersion":1}"#.as_slice(),
        ] {
            assert!(serde_json::from_slice::<Action>(bytes).is_err());
        }
        assert!(
            serde_json::from_slice::<Action>(
                br#"{"command":"restart","schemaVersion":1,"role":"overlay"}"#
            )
            .is_ok()
        );
        assert!(read_line(&mut std::io::Cursor::new(vec![b'x'; LINE + 1])).is_err());
        assert!(read_line(&mut std::io::Cursor::new(b"{}".as_slice())).is_err());
        assert!(
            read_line(&mut std::io::Cursor::new(b"{}\n".as_slice()))
                .unwrap()
                .is_some()
        );
    }
}
