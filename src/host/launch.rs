//! Launch HDT inside the exact running Proton session without altering its prefix.
use crate::config::Config;
use anyhow::{Context, Result, bail, ensure};
use std::{
    collections::BTreeMap,
    ffi::OsString,
    fs,
    os::unix::{
        fs::{DirBuilderExt, MetadataExt},
        process::ExitStatusExt,
    },
    path::{Path, PathBuf},
    process::{Child, Command, ExitStatus},
};

type Environment = BTreeMap<OsString, OsString>;
#[derive(Debug)]
pub(crate) struct Session {
    target_pid: String,
    start_time: u64,
    prefix: PathBuf,
    environment: Environment,
    proton: PathBuf,
}
impl Session {
    fn namespace_command(&self, executable: &Path) -> Command {
        let mut command = Command::new("nsenter");
        command
            .args([
                "--target",
                &self.target_pid,
                "--user",
                "--mount",
                "--preserve-credentials",
                "--root",
                "--",
            ])
            .arg(executable);
        command
    }

    fn display(&self) -> Result<&std::ffi::OsStr> {
        self.environment
            .get(std::ffi::OsStr::new("DISPLAY"))
            .filter(|value| !value.is_empty())
            .map(OsString::as_os_str)
            .context("Selected Steam wineserver has no DISPLAY for the X11 link helper")
    }
    pub(crate) fn link_command(
        &self,
        executable: &Path,
        arguments: &[OsString],
    ) -> Result<Command> {
        self.link_command_at(Path::new("/proc"), executable, arguments)
    }
    fn link_command_at(
        &self,
        proc_root: &Path,
        executable: &Path,
        arguments: &[OsString],
    ) -> Result<Command> {
        let current = session(proc_root, &self.target_pid, &self.prefix)
            .context("Read selected Steam wineserver for X11 link helper")?;
        ensure!(
            current.start_time == self.start_time,
            "Selected Steam wineserver identity changed"
        );
        let mut command = current.namespace_command(executable);
        command.args(arguments).env("DISPLAY", current.display()?);
        match current.environment.get(std::ffi::OsStr::new("XAUTHORITY")) {
            Some(authority) => {
                command.env("XAUTHORITY", authority);
            }
            None => {
                command.env_remove("XAUTHORITY");
            }
        }
        Ok(command)
    }
    fn controller_arguments(&self, nonce: &str) -> [OsString; 3] {
        [
            nonce.into(),
            self.target_pid.clone().into(),
            self.start_time.to_string().into(),
        ]
    }
}

pub fn tracker_environment(mut inherited: Environment) -> Environment {
    if let Some(value) = inherited.get(std::ffi::OsStr::new("LD_PRELOAD")) {
        let libraries: Vec<_> = value
            .to_string_lossy()
            .split(|c: char| c == ':' || c.is_whitespace())
            .filter(|p| {
                !p.is_empty()
                    && Path::new(p)
                        .file_name()
                        .is_none_or(|n| n != "gameoverlayrenderer.so")
            })
            .map(str::to_owned)
            .collect();
        if libraries.is_empty() {
            inherited.remove(std::ffi::OsStr::new("LD_PRELOAD"));
        } else {
            inherited.insert("LD_PRELOAD".into(), libraries.join(":").into());
        }
    }
    inherited
}
/// Validate the explicitly selected Linux x86-64 shared library header.
/// The loader still performs full dynamic-link validation when HDT starts.
pub fn libxi_library(path: &Path) -> Result<PathBuf> {
    use std::{io::Read, os::unix::fs::OpenOptionsExt};
    ensure!(path.is_absolute(), "HDT_LIBXI_LIBRARY must be absolute");
    let path = fs::canonicalize(path).context("Resolve HDT_LIBXI_LIBRARY")?;
    let text = path.to_str().context("HDT_LIBXI_LIBRARY must be UTF-8")?;
    ensure!(
        !text.chars().any(|c| c == ':' || c.is_whitespace()),
        "HDT_LIBXI_LIBRARY cannot contain loader-list separators"
    );
    ensure!(
        path.file_name().is_some_and(|name| name == "libXi.so.6"),
        "HDT_LIBXI_LIBRARY must select the runtime bundle's libXi.so.6"
    );
    let mut file = fs::OpenOptions::new()
        .read(true)
        .custom_flags(nix::libc::O_NOFOLLOW | nix::libc::O_NONBLOCK)
        .open(&path)?;
    let metadata = file.metadata()?;
    ensure!(
        metadata.is_file() && (64..=16 * 1024 * 1024).contains(&metadata.len()),
        "HDT_LIBXI_LIBRARY must be a bounded regular shared-library file"
    );
    let mut header = [0u8; 64];
    file.read_exact(&mut header)?;
    ensure!(
        &header[..4] == b"\x7fELF" && header[4..7] == [2, 1, 1] && header[16..20] == [3, 0, 62, 0],
        "HDT_LIBXI_LIBRARY requires a Linux x86-64 ELF shared library"
    );
    Ok(path)
}

/// Affect only the environment of the selected HDT launch and its descendants.
pub fn libxi_environment(environment: &mut Environment, library: &Path) -> Result<PathBuf> {
    let selected = libxi_library(library)?;
    let inherited = environment
        .get(std::ffi::OsStr::new("LD_PRELOAD"))
        .map(|value| value.to_str().context("Inherited LD_PRELOAD must be UTF-8"))
        .transpose()?
        .unwrap_or("");
    let mut libraries = vec![selected.to_string_lossy().into_owned()];
    for item in inherited
        .split(|c: char| c == ':' || c.is_whitespace())
        .filter(|s| !s.is_empty())
    {
        if Path::new(item)
            .file_name()
            .is_some_and(|name| name.as_encoded_bytes().starts_with(b"libXi.so"))
        {
            ensure!(
                Path::new(item) == selected,
                "Conflicting libXi in inherited LD_PRELOAD"
            );
        } else {
            libraries.push(item.to_owned());
        }
    }
    environment.insert("LD_PRELOAD".into(), libraries.join(":").into());
    Ok(selected)
}

fn start_time(proc_root: &Path, pid: &str) -> Result<u64> {
    let stat = fs::read_to_string(proc_root.join(pid).join("stat"))?;
    stat.rsplit_once(") ")
        .context("Invalid process stat")?
        .1
        .split_whitespace()
        .nth(19)
        .context("Missing process start time")?
        .parse()
        .context("Invalid process start time")
}
pub(crate) fn controller_session(prefix: &Path, args: &[OsString]) -> Result<Session> {
    controller_session_at(Path::new("/proc"), prefix, args)
}
fn controller_session_at(proc_root: &Path, prefix: &Path, args: &[OsString]) -> Result<Session> {
    ensure!(
        args.len() == 3,
        "Expected controller nonce, Steam wineserver PID and start time"
    );
    let pid = args[1].to_str().context("Invalid Steam wineserver PID")?;
    ensure!(
        !pid.is_empty() && pid.bytes().all(|b| b.is_ascii_digit()),
        "Invalid Steam wineserver PID"
    );
    let expected: u64 = args[2]
        .to_str()
        .context("Invalid Steam wineserver start time")?
        .parse()?;
    let selected = session(proc_root, pid, &fs::canonicalize(prefix)?)?;
    ensure!(
        selected.start_time == expected,
        "Selected Steam wineserver identity changed"
    );
    selected.display()?;
    Ok(selected)
}
fn session(proc_root: &Path, pid: &str, prefix: &Path) -> Result<Session> {
    use std::os::unix::ffi::OsStringExt;
    let before = start_time(proc_root, pid)?;
    let directory = proc_root.join(pid);
    let environment: Environment = fs::read(directory.join("environ"))?
        .split(|b| *b == 0)
        .filter_map(|entry| {
            entry.iter().position(|b| *b == b'=').map(|index| {
                (
                    OsString::from_vec(entry[..index].to_vec()),
                    OsString::from_vec(entry[index + 1..].to_vec()),
                )
            })
        })
        .collect();
    let candidate = environment
        .get(std::ffi::OsStr::new("WINEPREFIX"))
        .context("Wrong game prefix")?;
    ensure!(fs::canonicalize(candidate)? == prefix, "Wrong game prefix");
    let server = fs::read_link(directory.join("exe"))?;
    ensure!(
        server.ends_with("files/bin/wineserver"),
        "Expected live Proton wineserver"
    );
    for entry in fs::read_dir(directory.join("fd"))? {
        let path = match fs::read_link(entry?.path()) {
            Ok(p) => p,
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => continue,
            Err(e) => return Err(e.into()),
        };
        let name = path.to_string_lossy();
        ensure!(
            !(name.contains("wine-") && name.ends_with("-fsync (deleted)")),
            "Wine fsync file is deleted; fully restart the Steam game session before launching HDT."
        );
    }
    let proton = server
        .parent()
        .and_then(Path::parent)
        .and_then(Path::parent)
        .context("Invalid Proton server path")?
        .join("proton");
    ensure!(
        before == start_time(proc_root, pid)?,
        "Selected Steam wineserver identity changed"
    );
    Ok(Session {
        target_pid: pid.to_owned(),
        start_time: before,
        prefix: prefix.to_owned(),
        environment,
        proton,
    })
}
fn code(status: ExitStatus) -> i32 {
    status
        .code()
        .unwrap_or_else(|| 128 + status.signal().unwrap_or(1))
}
fn win(path: &Path) -> String {
    format!("Z:{}", path.to_string_lossy().replace('/', "\\"))
}
fn enabled(name: &str) -> bool {
    std::env::var_os(name).is_some_and(|v| v == "1")
}
fn variable(name: &str, fallback: &str) -> String {
    std::env::var(name).unwrap_or_else(|_| fallback.into())
}
fn wait_owned(command: &mut Command) -> Result<i32> {
    use std::sync::{
        Arc,
        atomic::{AtomicUsize, Ordering},
    };
    let signal = Arc::new(AtomicUsize::new(0));
    struct Registrations(Vec<signal_hook::SigId>);
    impl Drop for Registrations {
        fn drop(&mut self) {
            for id in self.0.drain(..) {
                signal_hook::low_level::unregister(id);
            }
        }
    }
    let mut registrations = Registrations(Vec::new());
    for value in [signal_hook::consts::SIGTERM, signal_hook::consts::SIGINT] {
        registrations.0.push(signal_hook::flag::register_usize(
            value,
            Arc::clone(&signal),
            value as usize,
        )?);
    }
    let mut child = command.spawn()?;
    loop {
        if let Some(status) = child.try_wait()? {
            return Ok(code(status));
        }
        let value = signal.swap(0, Ordering::Relaxed);
        if value != 0 {
            let value = nix::sys::signal::Signal::try_from(value as i32)?;
            nix::sys::signal::kill(nix::unistd::Pid::from_raw(child.id() as i32), value)?;
        }
        std::thread::sleep(std::time::Duration::from_millis(20));
    }
}
fn execute(
    program: impl AsRef<std::ffi::OsStr>,
    args: &[OsString],
    cwd: &Path,
    environment: &Environment,
) -> Result<i32> {
    wait_owned(
        Command::new(program)
            .args(args)
            .current_dir(cwd)
            .env_clear()
            .envs(environment),
    )
}
/// Validate and apply HDT-only rendering overrides, retaining unrelated module rules.
pub fn renderer_environment(
    environment: &mut Environment,
    renderer: &str,
    evidence: &Path,
) -> Result<()> {
    match renderer {
        "inherit" => (),
        "dxvk-software" => {
            let old = environment
                .get(std::ffi::OsStr::new("DXVK_CONFIG"))
                .map(|v| v.to_string_lossy().into_owned())
                .unwrap_or_default();
            environment.insert(
                "DXVK_CONFIG".into(),
                [old.as_str(), "d3d9.shaderModel = 1"]
                    .into_iter()
                    .filter(|s| !s.is_empty())
                    .collect::<Vec<_>>()
                    .join("; ")
                    .into(),
            );
            environment.insert("DXVK_LOG_PATH".into(), evidence.as_os_str().to_owned());
        }
        "wine-gdi" => {
            let old = environment
                .get(std::ffi::OsStr::new("WINEDLLOVERRIDES"))
                .map(|v| v.to_string_lossy().into_owned())
                .unwrap_or_default();
            let mut rules = Vec::new();
            for rule in old.split(';').filter(|s| !s.is_empty()) {
                let (modules, value) = rule
                    .split_once('=')
                    .context("Malformed inherited DLL override")?;
                let names: Vec<_> = modules
                    .split(',')
                    .filter(|m| !m.trim().eq_ignore_ascii_case("d3d9"))
                    .collect();
                if !names.is_empty() {
                    rules.push(format!("{}={value}", names.join(",")));
                }
            }
            rules.push("d3d9=b".into());
            environment.insert("WINEDLLOVERRIDES".into(), rules.join(";").into());
            let old = environment
                .get(std::ffi::OsStr::new("WINE_D3D_CONFIG"))
                .map(|v| v.to_string_lossy().into_owned())
                .unwrap_or_default();
            let mut values: Vec<_> = old
                .split([';', ','])
                .filter(|s| !s.is_empty() && !s.starts_with("renderer="))
                .map(str::to_owned)
                .collect();
            values.push("renderer=gdi".into());
            environment.insert("WINE_D3D_CONFIG".into(), values.join(";").into());
            environment.insert("WINEDEBUG".into(), "-all,+winediag".into());
        }
        _ => bail!("HDT_RENDERER must be inherit, dxvk-software, or wine-gdi"),
    }
    Ok(())
}
struct Helper(Option<Child>);
impl Drop for Helper {
    fn drop(&mut self) {
        if let Some(child) = self.0.as_mut() {
            let _ = nix::sys::signal::kill(
                nix::unistd::Pid::from_raw(child.id() as i32),
                nix::sys::signal::Signal::SIGTERM,
            );
            if let Ok(status) = child.wait()
                && !status.success()
            {
                eprintln!("HDT input-region helper exited ({})", code(status));
            }
        }
    }
}
pub fn run(cfg: &Config, args: &[OsString]) -> Result<i32> {
    let canonical_prefix = fs::canonicalize(&cfg.prefix)?;
    let exe = fs::canonicalize(cfg.app_directory.join("HearthstoneDeckTracker.exe"))?;
    fs::DirBuilder::new()
        .recursive(true)
        .mode(0o700)
        .create(cfg.project_root.join("evidence"))?;
    let inside = args.first().is_some_and(|s| s == "--inside");
    let probe = args.iter().any(|s| s == "--window-probe");
    let plugin = args.iter().any(|s| s == "--build-plugin");
    if inside {
        let pid = args
            .get(1)
            .context("Missing wineserver PID")?
            .to_str()
            .context("Invalid wineserver PID")?;
        ensure!(
            !pid.is_empty() && pid.bytes().all(|b| b.is_ascii_digit()),
            "Invalid wineserver PID"
        );
        let session = session(Path::new("/proc"), pid, &canonical_prefix)?;
        let mut environment = tracker_environment(session.environment);
        let outer = fs::metadata(format!("/proc/{pid}/root/tmp"))?;
        let current = fs::metadata("/tmp")?;
        ensure!(
            outer.dev() == current.dev() && outer.ino() == current.ino(),
            "Steam temporary-files namespace was not joined"
        );
        let wine = session
            .proton
            .parent()
            .context("Invalid Proton directory")?
            .join("files/bin/wine");
        let compiler =
            OsString::from("C:\\windows\\Microsoft.NET\\Framework64\\v4.0.30319\\csc.exe");
        if plugin {
            let mut arguments = vec![
                compiler,
                "/nologo".into(),
                "/target:library".into(),
                "/platform:x64".into(),
                format!(
                    "/out:{}",
                    win(&cfg.project_root.join("evidence/ProtonCompatibility.dll"))
                )
                .into(),
                format!("/r:{}", win(&exe)).into(),
                "/r:System.Drawing.dll".into(),
                "/r:System.Xaml.dll".into(),
                format!("/r:{}", win(&cfg.app_directory.join("HearthMirror.dll"))).into(),
                "/r:System.Web.Extensions.dll".into(),
                format!("/r:{}", win(&cfg.app_directory.join("MahApps.Metro.dll"))).into(),
            ];
            arguments.extend(
                [
                    "PresentationCore.dll",
                    "PresentationFramework.dll",
                    "WindowsBase.dll",
                ]
                .map(|name| {
                    format!("/r:C:\\windows\\Microsoft.NET\\Framework64\\v4.0.30319\\WPF\\{name}")
                        .into()
                }),
            );
            let mut sources = fs::read_dir(cfg.source_root.join("src/plugin"))?
                .map(|e| e.map(|e| e.path()))
                .collect::<std::io::Result<Vec<_>>>()?;
            sources.retain(|p| p.extension().is_some_and(|e| e == "cs"));
            sources.sort();
            arguments.extend(sources.iter().map(|p| win(p).into()));
            return execute(wine, &arguments, &cfg.project_root, &environment);
        }
        if probe {
            let output = cfg.project_root.join("evidence/window-probe.exe");
            let status = execute(
                &wine,
                &[
                    compiler,
                    "/nologo".into(),
                    "/target:winexe".into(),
                    "/platform:x64".into(),
                    format!("/out:{}", win(&output)).into(),
                    win(&cfg.source_root.join("src/diagnostics/window-probe.cs")).into(),
                ],
                &cfg.project_root,
                &environment,
            )?;
            ensure!(status == 0, "Window probe compilation failed");
            return execute(
                wine,
                &[
                    output.into_os_string(),
                    win(&cfg.project_root.join("evidence/window-probe.json")).into(),
                ],
                &cfg.project_root,
                &environment,
            );
        }
        renderer_environment(
            &mut environment,
            &variable(
                "HDT_RENDERER",
                if enabled("HDT_SOFTWARE_RENDER") {
                    "dxvk-software"
                } else {
                    "inherit"
                },
            ),
            &cfg.project_root.join("evidence"),
        )?;
        if enabled("HDT_DEBUG") {
            environment.insert("WINEDEBUG".into(), "+mscoree,+seh".into());
        }
        let recorder = variable("HDT_FLIGHT_RECORDER", "1");
        ensure!(
            recorder == "0" || recorder == "1",
            "HDT_FLIGHT_RECORDER must be 0 or 1"
        );
        environment.insert("HDT_FLIGHT_RECORDER".into(), recorder.into());
        if enabled("HDT_HOVER_DIAGNOSTICS") {
            environment.insert(
                "HDT_HOVER_DIAGNOSTICS_FILE".into(),
                cfg.project_root
                    .join("evidence/hover-state.json")
                    .into_os_string(),
            );
        }
        if enabled("HDT_ALPHA_FLOOR") {
            environment.insert("HDT_ALPHA_FLOOR".into(), "1".into());
        }
        if enabled("HDT_CONTROL_INPUT") {
            let nonce = std::env::var_os("HDT_INPUT_SESSION")
                .filter(|v| !v.is_empty())
                .context("Input-region session is missing")?;
            environment.insert("HDT_INPUT_SESSION".into(), nonce);
            environment.insert(
                "HDT_INPUT_REGION_FILE".into(),
                cfg.project_root
                    .join("evidence/input-region.json")
                    .into_os_string(),
            );
        }
        if let Some(library) = std::env::var_os("HDT_LIBXI_LIBRARY") {
            let selected = libxi_environment(&mut environment, Path::new(&library))?;
            println!("HDT-only libXi selected: {}", selected.display());
        }
        let result = if enabled("HDT_DIRECT") {
            execute(
                wine,
                &[exe.clone().into_os_string()],
                exe.parent().context("Missing app directory")?,
                &environment,
            )?
        } else {
            execute(
                "/usr/bin/python3",
                &[
                    session.proton.into_os_string(),
                    "run".into(),
                    exe.clone().into_os_string(),
                ],
                exe.parent().context("Missing app directory")?,
                &environment,
            )?
        };
        println!("HDT launch process exited with status {result}");
        return Ok(result);
    }
    let mut candidates = Vec::new();
    for entry in fs::read_dir("/proc")? {
        let entry = entry?;
        let pid = entry.file_name();
        let Some(pid) = pid.to_str() else { continue };
        if !pid.bytes().all(|b| b.is_ascii_digit()) {
            continue;
        }
        let candidate = (|| -> Result<()> {
            if fs::read_to_string(entry.path().join("comm"))?.trim() != "wineserver" {
                return Ok(());
            }
            session(Path::new("/proc"), pid, &canonical_prefix)?;
            candidates.push(pid.to_owned());
            Ok(())
        })();
        if let Err(error) = candidate {
            if error.to_string() == "Wrong game prefix"
                || error.downcast_ref::<std::io::Error>().is_some_and(|e| {
                    matches!(
                        e.kind(),
                        std::io::ErrorKind::NotFound | std::io::ErrorKind::PermissionDenied
                    )
                })
            {
                continue;
            }
            return Err(error);
        }
    }
    ensure!(
        candidates.len() == 1,
        "Expected one running game wineserver; found {}. Launch Battle.net through Steam first.",
        candidates.len()
    );
    fs::File::open(&exe)?;
    ensure!(
        !enabled("HDT_ELEMENT_INPUT"),
        "X11 input-shape experiment retired; use HDT_CONTROL_INPUT=1 for compositor control gating."
    );
    let executable = std::env::current_exe()?;
    let selected = session(Path::new("/proc"), &candidates[0], &canonical_prefix)?;
    let nonce = uuid::Uuid::new_v4().to_string();
    let _bridge = if enabled("HDT_CONTROL_INPUT") && !probe && !plugin {
        selected.display()?;
        Helper(Some(
            Command::new(&executable)
                .arg("control-input")
                .args(selected.controller_arguments(&nonce))
                .current_dir(&cfg.project_root)
                .env("HDT_INPUT_SESSION", &nonce)
                .spawn()?,
        ))
    } else {
        Helper(None)
    };
    let mut command = selected.namespace_command(&executable);
    command
        .args([
            OsString::from("raw-launch"),
            OsString::from("--inside"),
            OsString::from(&candidates[0]),
        ])
        .env("HDT_INPUT_SESSION", nonce)
        .current_dir(&cfg.project_root);
    if probe {
        command.arg("--window-probe");
    }
    if plugin {
        command.arg("--build-plugin");
    }
    println!("Launching HDT inside the existing Steam Proton session.");
    wait_owned(&mut command).context("Enter the project devenv shell first (nsenter required).")
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::os::unix::fs::symlink;
    fn write_stat(process: &Path, start: u64) -> Result<()> {
        let mut fields = vec!["0".to_owned(); 20];
        fields[0] = "S".into();
        fields[19] = start.to_string();
        fs::write(
            process.join("stat"),
            format!("123 (wine server) {}", fields.join(" ")),
        )?;
        Ok(())
    }
    #[test]
    fn link_helpers_use_validated_namespace_display_and_optional_auth() -> Result<()> {
        let temporary = tempfile::tempdir()?;
        let prefix = temporary.path().join("prefix");
        fs::create_dir(&prefix)?;
        let process = temporary.path().join("123");
        fs::create_dir_all(process.join("fd"))?;
        write_stat(&process, 42)?;
        symlink("/Steam/Proton/files/bin/wineserver", process.join("exe"))?;
        for authority in [Some("/run/pressure-vessel/Xauthority"), None] {
            let mut environment = format!("WINEPREFIX={}\0DISPLAY=:7\0", prefix.display());
            if let Some(path) = authority {
                environment.push_str(&format!("XAUTHORITY={path}\0"));
            }
            fs::write(process.join("environ"), environment)?;
            let selected = session(temporary.path(), "123", &prefix)?;
            let executable = Path::new("/owned/hdt-host");
            let arguments = ["overlay-link".into(), "apply".into()];
            let command = selected.link_command_at(temporary.path(), executable, &arguments)?;
            assert_eq!(
                selected.controller_arguments("nonce"),
                [OsString::from("nonce"), "123".into(), "42".into()]
            );
            // Inspect explicit child overrides/removal. The separate environment
            // projection below does not test this Command's inheritance policy.
            let overrides: BTreeMap<_, _> = command.get_envs().collect();
            assert_eq!(
                overrides.get(std::ffi::OsStr::new("DISPLAY")),
                Some(&Some(std::ffi::OsStr::new(":7")))
            );
            assert_eq!(
                overrides.get(std::ffi::OsStr::new("XAUTHORITY")),
                Some(&authority.map(std::ffi::OsStr::new))
            );
            assert!(!overrides.contains_key(std::ffi::OsStr::new("HDT_INPUT_SESSION")));
            assert!(!overrides.contains_key(std::ffi::OsStr::new("HYPRLAND_INSTANCE_SIGNATURE")));
            assert!(!overrides.contains_key(std::ffi::OsStr::new("HDT_LIBXI_LIBRARY")));
            assert!(!overrides.contains_key(std::ffi::OsStr::new("XDG_RUNTIME_DIR")));
            let mut expected: Vec<OsString> = selected
                .namespace_command(executable)
                .get_args()
                .map(OsString::from)
                .collect();
            assert_eq!(
                expected,
                [
                    "--target",
                    "123",
                    "--user",
                    "--mount",
                    "--preserve-credentials",
                    "--root",
                    "--",
                    "/owned/hdt-host"
                ]
                .map(OsString::from)
            );
            expected.extend(arguments);
            assert_eq!(command.get_program(), "nsenter");
            assert_eq!(
                command.get_args().map(OsString::from).collect::<Vec<_>>(),
                expected
            );

            // Execute only the environment projection, not nsenter or X11.
            let mut projection = Command::new("/bin/sh");
            projection
                .env("HDT_INPUT_SESSION", "nonce")
                .env("DISPLAY", ":stale-host")
                .env("XAUTHORITY", "/stale/host-auth")
                .env("HYPRLAND_INSTANCE_SIGNATURE", "owned-synthetic")
                .env("HDT_LIBXI_LIBRARY", "/synthetic/libXi.so.6")
                .env("XDG_RUNTIME_DIR", "/synthetic/runtime");
            for (key, value) in command.get_envs() {
                match value {
                    Some(value) => {
                        projection.env(key, value);
                    }
                    None => {
                        projection.env_remove(key);
                    }
                }
            }
            projection.args(["-c", "test \"$DISPLAY\" = :7 && test \"${XAUTHORITY-unset}\" = \"$1\" && test \"$HDT_INPUT_SESSION\" = nonce && test \"$HYPRLAND_INSTANCE_SIGNATURE\" = owned-synthetic && test \"$HDT_LIBXI_LIBRARY\" = /synthetic/libXi.so.6 && test \"$XDG_RUNTIME_DIR\" = /synthetic/runtime", "fixture", authority.unwrap_or("unset")]);
            assert!(projection.status()?.success());
            assert!(
                controller_session_at(
                    temporary.path(),
                    &prefix,
                    &selected.controller_arguments("nonce")
                )
                .is_ok()
            );
            write_stat(&process, 43)?;
            assert!(
                selected
                    .link_command_at(
                        temporary.path(),
                        executable,
                        &["overlay-link".into(), "restore".into()]
                    )
                    .unwrap_err()
                    .to_string()
                    .contains("identity changed")
            );
            assert!(
                controller_session_at(
                    temporary.path(),
                    &prefix,
                    &selected.controller_arguments("nonce")
                )
                .is_err()
            );
            write_stat(&process, 42)?;
        }
        for display in ["", "DISPLAY=\0"] {
            fs::write(
                process.join("environ"),
                format!("WINEPREFIX={}\0{display}", prefix.display()),
            )?;
            assert!(
                session(temporary.path(), "123", &prefix)?
                    .link_command_at(
                        temporary.path(),
                        Path::new("/owned/hdt-host"),
                        &["overlay-link".into()]
                    )
                    .unwrap_err()
                    .to_string()
                    .contains("no DISPLAY")
            );
        }
        let other = temporary.path().join("other");
        fs::create_dir(&other)?;
        fs::write(
            process.join("environ"),
            format!("WINEPREFIX={}\0DISPLAY=:7\0", other.display()),
        )?;
        assert_eq!(
            session(temporary.path(), "123", &prefix)
                .unwrap_err()
                .to_string(),
            "Wrong game prefix"
        );
        Ok(())
    }
    #[test]
    fn session_uses_actual_executable_and_refuses_deleted_fsync() -> Result<()> {
        let temporary = tempfile::tempdir()?;
        let prefix = temporary.path().join("prefix");
        fs::create_dir(&prefix)?;
        let process = temporary.path().join("123");
        fs::create_dir_all(process.join("fd"))?;
        write_stat(&process, 42)?;
        fs::write(
            process.join("environ"),
            format!("WINEPREFIX={}\0", prefix.display()),
        )?;
        symlink(
            "/Steam/Proton Experimental/files/bin/wineserver",
            process.join("exe"),
        )?;
        assert_eq!(
            session(temporary.path(), "123", &prefix)?.proton,
            Path::new("/Steam/Proton Experimental/proton")
        );
        symlink("/tmp/wine-user-fsync (deleted)", process.join("fd/9"))?;
        assert!(
            session(temporary.path(), "123", &prefix)
                .unwrap_err()
                .to_string()
                .contains("fully restart")
        );
        Ok(())
    }
    #[test]
    fn owned_helper_is_reaped_on_scope_exit() -> Result<()> {
        let child = Command::new("/bin/sh")
            .args(["-c", "exec sleep 30"])
            .spawn()?;
        let pid = nix::unistd::Pid::from_raw(child.id() as i32);
        let helper = Helper(Some(child));
        drop(helper);
        assert_eq!(
            nix::sys::signal::kill(pid, None),
            Err(nix::errno::Errno::ESRCH)
        );
        Ok(())
    }
    #[test]
    fn process_boundary_preserves_arguments_and_exit_code() -> Result<()> {
        let temporary = tempfile::tempdir()?;
        let output = temporary.path().join("arguments");
        let arguments = [
            OsString::from("-c"),
            OsString::from("printf '%s\\n' \"$1\" \"$2\" > \"$3\"; exit 37"),
            OsString::from("fixture"),
            OsString::from("with spaces"),
            OsString::from("literal;$()"),
            output.clone().into_os_string(),
        ];
        assert_eq!(
            execute("/bin/sh", &arguments, temporary.path(), &Environment::new())?,
            37
        );
        assert_eq!(fs::read_to_string(output)?, "with spaces\nliteral;$()\n");
        Ok(())
    }
}
