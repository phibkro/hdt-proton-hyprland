//! Real sibling UI failure-domain acceptance. Only run in the owned private namespace.
use serde_json::{Value, json};
use std::{
    fs,
    io::{BufRead, BufReader, Write},
    os::unix::fs::PermissionsExt,
    process::{Child, Command, Stdio},
    sync::mpsc,
    time::{Duration, Instant},
};
use x11rb::{
    connection::Connection,
    protocol::{
        shape::{ConnectionExt as _, SK},
        xproto::{
            AtomEnum, ConnectionExt as _, CreateWindowAux, ImageFormat, MapState, PropMode,
            WindowClass,
        },
    },
    rust_connection::RustConnection,
    wrapper::ConnectionExt as _,
};
struct Owned(Child);
impl Drop for Owned {
    fn drop(&mut self) {
        let _ = self.0.kill();
        let _ = self.0.wait();
    }
}
struct Session {
    owner: Owned,
    input: std::process::ChildStdin,
    replies: mpsc::Receiver<Value>,
    log: fs::File,
}
impl Session {
    fn start(display: &str) -> Self {
        let mut owner = Owned(
            Command::new("/capture/hdt-host")
                .args(["native-session", "run", "/capture/session-config.json"])
                .env("DISPLAY", display)
                .stdin(Stdio::piped())
                .stdout(Stdio::piped())
                .stderr(fs::File::create("session-owner.stderr").unwrap())
                .spawn()
                .unwrap(),
        );
        let input = owner.0.stdin.take().unwrap();
        let output = owner.0.stdout.take().unwrap();
        let (tx, replies) = mpsc::sync_channel(32);
        std::thread::spawn(move || {
            let mut input = BufReader::new(output);
            loop {
                let mut bytes = Vec::new();
                let n = std::io::Read::take(&mut input, 8193)
                    .read_until(b'\n', &mut bytes)
                    .unwrap();
                if n == 0 {
                    break;
                }
                assert!(n <= 8192 && bytes.last() == Some(&b'\n'));
                if tx.send(serde_json::from_slice(&bytes).unwrap()).is_err() {
                    break;
                }
            }
        });
        Self {
            owner,
            input,
            replies,
            log: fs::OpenOptions::new()
                .create(true)
                .append(true)
                .open("session-actions.jsonl")
                .unwrap(),
        }
    }
    fn command(&mut self, value: Value) {
        writeln!(self.log, "{}", json!({"command":value})).unwrap();
        serde_json::to_writer(&mut self.input, &value).unwrap();
        self.input.write_all(b"\n").unwrap();
        self.input.flush().unwrap();
    }
    fn status(&mut self) -> Value {
        self.command(json!({"schemaVersion":1,"command":"status"}));
        loop {
            let reply = self
                .replies
                .recv_timeout(Duration::from_secs(4))
                .expect("external owner status deadline");
            writeln!(self.log, "{}", reply).unwrap();
            if reply["kind"] == "status" {
                return reply;
            }
        }
    }
    fn until(&mut self, why: &str, mut accept: impl FnMut(&Value) -> bool) -> Value {
        let end = Instant::now() + Duration::from_secs(9);
        loop {
            let state = self.status();
            if accept(&state) {
                return state;
            }
            assert!(Instant::now() < end, "{why}: {state}");
            std::thread::sleep(Duration::from_millis(100));
        }
    }
    fn restart(&mut self, role: &str) {
        self.command(json!({"schemaVersion":1,"command":"restart","role":role}));
    }
}
fn role<'a>(value: &'a Value, name: &str) -> &'a Value {
    value["roles"]
        .as_array()
        .unwrap()
        .iter()
        .find(|r| r["role"] == name)
        .unwrap()
}
fn pid(value: &Value, name: &str) -> i32 {
    role(value, name)["pid"].as_i64().unwrap() as i32
}
fn window(value: &Value, name: &str) -> u32 {
    role(value, name)["last"]["window"].as_u64().unwrap() as u32
}
fn signal(pid: i32, signal: nix::sys::signal::Signal) {
    nix::sys::signal::kill(nix::unistd::Pid::from_raw(pid), signal).unwrap();
}
fn publish(source: &Value, sequence: i32) {
    let mut source = source.clone();
    let now = chrono::Utc::now().to_rfc3339();
    source["sequence"] = json!(sequence);
    source["observedAt"] = json!(now);
    source["arena"]["sequence"] = json!(sequence);
    source["arena"]["observedAt"] = json!(now);
    fs::write("session/next.json", serde_json::to_vec(&source).unwrap()).unwrap();
    fs::rename("session/next.json", "session/observation.json").unwrap();
}
fn reference() -> Vec<u8> {
    let output = Command::new(std::env::var_os("HDT_TEST_UI_DOTNET").unwrap())
        .args([
            "/capture/checks/Hdt.Checks.dll",
            "--overlay-reference",
            "/capture/session/observation.json",
            "/capture/session/preferences.json",
            "/capture/reference.bgra",
        ])
        .env("HDT_HOST_BINARY", "/capture/hdt-host")
        .env("DOTNET_PROCESSOR_COUNT", "2")
        .output()
        .unwrap();
    assert!(
        output.status.success(),
        "reference renderer: {}",
        String::from_utf8_lossy(&output.stderr)
    );
    fs::read("reference.bgra").unwrap()
}
fn mapped(conn: &RustConnection, window: u32) -> bool {
    conn.get_window_attributes(window)
        .unwrap()
        .reply()
        .is_ok_and(|r| r.map_state == MapState::VIEWABLE)
}
fn pixels(conn: &RustConnection, window: u32, width: u16, height: u16) -> Vec<u8> {
    conn.get_image(ImageFormat::Z_PIXMAP, window, 0, 0, width, height, u32::MAX)
        .unwrap()
        .reply()
        .unwrap()
        .data
}
fn same_pixels(actual: &[u8], expected: &[u8]) -> bool {
    actual.len() == expected.len()
        && actual
            .as_chunks::<4>()
            .0
            .iter()
            .zip(expected.as_chunks::<4>().0.iter())
            .all(|(a, b)| a[..3] == b[..3])
}
fn expect_pixels(conn: &RustConnection, window: u32, expected: &[u8], why: &str) {
    let end = Instant::now() + Duration::from_secs(4);
    loop {
        if mapped(conn, window) && same_pixels(&pixels(conn, window, 320, 640), expected) {
            return;
        }
        assert!(
            Instant::now() < end,
            "{why}: current pixels never matched independently rendered reference"
        );
        std::thread::sleep(Duration::from_millis(50));
    }
}
fn screenshot(conn: &RustConnection, window: u32, name: &str) {
    let g = conn.get_geometry(window).unwrap().reply().unwrap();
    let bytes = pixels(conn, window, g.width, g.height);
    let mut ppm = format!("P6\n{} {}\n255\n", g.width, g.height).into_bytes();
    for pixel in bytes.as_chunks::<4>().0 {
        ppm.extend([pixel[2], pixel[1], pixel[0]]);
    }
    fs::write(format!("{name}.ppm"), ppm).unwrap();
}
fn assert_input_empty(conn: &RustConnection, window: u32) {
    assert!(
        conn.shape_get_rectangles(window, SK::INPUT)
            .unwrap()
            .reply()
            .unwrap()
            .rectangles
            .is_empty()
    );
}
fn place_main(conn: &RustConnection, window: u32) {
    // Keep both real windows visible in this 1280px fixture, at Main's supported minimum.
    conn.configure_window(
        window,
        &x11rb::protocol::xproto::ConfigureWindowAux::new()
            .x(0)
            .y(0)
            .width(800)
            .height(760),
    )
    .unwrap()
    .check()
    .unwrap();
    conn.flush().unwrap();
    std::thread::sleep(Duration::from_millis(200));
    let bounds = conn.get_geometry(window).unwrap().reply().unwrap();
    assert_eq!(bounds.width, 800);
}
fn click(conn: &RustConnection, display: &str, root: u32, main: u32, x: i16, y: i16) {
    let origin = conn
        .translate_coordinates(main, root, 0, 0)
        .unwrap()
        .reply()
        .unwrap();
    let x = origin.dst_x + x;
    let y = origin.dst_y + y;
    assert!(
        (0..600).contains(&x) && (0..900).contains(&y),
        "only owned UI pointer area"
    );
    assert!(
        Command::new(std::env::var_os("HDT_TEST_UI_XDOTOOL").unwrap())
            .args([
                "mousemove",
                "--sync",
                &x.to_string(),
                &y.to_string(),
                "click",
                "1"
            ])
            .env("DISPLAY", display)
            .status()
            .unwrap()
            .success()
    );
}
#[test]
#[ignore = "requires check-native-isolation.sh private PID/mount/network/display sandbox"]
fn independent_ui_processes_and_real_pixels() {
    for key in [
        "DISPLAY",
        "WAYLAND_DISPLAY",
        "XAUTHORITY",
        "DBUS_SESSION_BUS_ADDRESS",
    ] {
        assert!(std::env::var_os(key).is_none());
    }
    assert_eq!(std::env::var("HDT_TEST_X11_ISOLATED").unwrap(), "1");
    fs::create_dir_all("/tmp/.X11-unix").unwrap();
    assert!(fs::read_dir("/tmp/.X11-unix").unwrap().next().is_none());
    let mut server = Owned(
        Command::new(std::env::var_os("HDT_TEST_XVFB").unwrap())
            .args([
                "-displayfd",
                "1",
                "-screen",
                "0",
                "1280x900x24",
                "-nolisten",
                "tcp",
                "-ac",
            ])
            .stdout(Stdio::piped())
            .stderr(Stdio::inherit())
            .spawn()
            .unwrap(),
    );
    let (tx, rx) = mpsc::channel();
    let output = server.0.stdout.take().unwrap();
    std::thread::spawn(move || {
        let mut line = String::new();
        BufReader::new(output).read_line(&mut line).unwrap();
        tx.send(line).unwrap();
    });
    let number = rx
        .recv_timeout(Duration::from_secs(5))
        .unwrap()
        .trim()
        .parse::<u16>()
        .unwrap();
    let display = format!(":{number}");
    let (conn, screen) = x11rb::connect(Some(&display)).unwrap();
    let screen = &conn.setup().roots[screen];
    let root = screen.root;
    let game = conn.generate_id().unwrap();
    conn.create_window(
        screen.root_depth,
        game,
        root,
        600,
        100,
        640,
        680,
        0,
        WindowClass::INPUT_OUTPUT,
        screen.root_visual,
        &CreateWindowAux::new().background_pixel(0x223344),
    )
    .unwrap()
    .check()
    .unwrap();
    let pid_atom = conn
        .intern_atom(false, b"_NET_WM_PID")
        .unwrap()
        .reply()
        .unwrap()
        .atom;
    let active = conn
        .intern_atom(false, b"_NET_ACTIVE_WINDOW")
        .unwrap()
        .reply()
        .unwrap()
        .atom;
    conn.change_property32(
        PropMode::REPLACE,
        game,
        pid_atom,
        AtomEnum::CARDINAL,
        &[std::process::id()],
    )
    .unwrap()
    .check()
    .unwrap();
    conn.change_property8(
        PropMode::REPLACE,
        game,
        AtomEnum::WM_CLASS,
        AtomEnum::STRING,
        b"fixture\0hdt-fixture\0",
    )
    .unwrap()
    .check()
    .unwrap();
    conn.change_property8(
        PropMode::REPLACE,
        game,
        AtomEnum::WM_NAME,
        AtomEnum::STRING,
        b"Hearthstone",
    )
    .unwrap()
    .check()
    .unwrap();
    conn.map_window(game).unwrap().check().unwrap();
    let activate = |window| {
        conn.change_property32(PropMode::REPLACE, root, active, AtomEnum::WINDOW, &[window])
            .unwrap()
            .check()
            .unwrap();
        conn.flush().unwrap();
    };
    activate(game);
    fs::create_dir("session").unwrap();
    fs::set_permissions("session", fs::Permissions::from_mode(0o700)).unwrap();
    let first: Value =
        serde_json::from_slice(&fs::read("native-overlay-observation.json").unwrap()).unwrap();
    let second: Value =
        serde_json::from_slice(&fs::read("native-overlay-alternate.json").unwrap()).unwrap();
    publish(&first, 100);
    fs::write("session-config.json",serde_json::to_vec(&json!({"schemaVersion":1,"sessionRoot":"/capture/session","dotnet":std::env::var("HDT_TEST_UI_DOTNET").unwrap(),"application":"/capture/app/Hdt.Avalonia.dll","observationFile":"/capture/session/observation.json","game":{"window":game,"pid":std::process::id(),"class":"hdt-fixture"}})).unwrap()).unwrap();
    nix::sys::prctl::set_child_subreaper(true).unwrap();
    let mut session = Session::start(&display);
    let state = session.until("both independent dispatchers ready", |s| {
        ["main", "overlay"]
            .iter()
            .all(|r| role(s, r)["health"] == "responsive")
    });
    let conflict = Command::new("/capture/hdt-host")
        .args(["native-session", "run", "/capture/session-config.json"])
        .stdin(Stdio::null())
        .output()
        .unwrap();
    assert!(
        !conflict.status.success(),
        "exclusive lease must reject a second writer before spawn"
    );
    fs::write("session-lease-conflict.stderr", &conflict.stderr).unwrap();
    let main = pid(&state, "main");
    let renderer = pid(&state, "overlay");
    let overlay = window(&state, "overlay");
    place_main(&conn, window(&state, "main"));
    assert_ne!(main, renderer);
    assert_input_empty(&conn, overlay);
    let heroes: Value =
        serde_json::from_slice(&fs::read("native-overlay-heroes.json").unwrap()).unwrap();
    let cards: Value =
        serde_json::from_slice(&fs::read("native-overlay-cards.json").unwrap()).unwrap();
    let menu: Value =
        serde_json::from_slice(&fs::read("native-overlay-menu.json").unwrap()).unwrap();
    click(&conn, &display, root, window(&state, "main"), 150, 87);
    session.until("actual pointer opens Arena", |s| {
        role(s, "main")["last"]["mainPage"] == 1
    });
    publish(&heroes, 101);
    expect_pixels(
        &conn,
        overlay,
        &reference(),
        "explicit hero phase reaches real independent overlay pixels",
    );
    screenshot(&conn, overlay, "hero-selection-overlay");
    std::thread::sleep(Duration::from_millis(1100));
    screenshot(&conn, window(&state, "main"), "hero-selection-main");
    publish(&cards, 102);
    expect_pixels(
        &conn,
        overlay,
        &reference(),
        "hero choices replaced by actual card phase",
    );
    screenshot(&conn, overlay, "card-draft-overlay");
    publish(&first, 103);
    expect_pixels(
        &conn,
        overlay,
        &reference(),
        "next card offer replaces previous pixels",
    );
    publish(&menu, 104);
    session.until("explicit menu clears overlay choices", |s| {
        role(s, "overlay")["last"]["projection"] == "inactive" && !mapped(&conn, overlay)
    });
    assert!(
        !mapped(&conn, overlay),
        "menu transition unmaps previous hero/card advice"
    );
    publish(&first, 105);
    expect_pixels(&conn, overlay, &reference(), "initial real source");
    screenshot(&conn, overlay, "initial-overlay");
    // STOP the real Main process, not a mocked actor or setter. Source remains directly consumed.
    signal(main, nix::sys::signal::Signal::SIGSTOP);
    session.until("Main frozen while renderer stays responsive", |s| {
        role(s, "main")["health"] == "unresponsive" && role(s, "overlay")["health"] == "responsive"
    });
    publish(&second, 106);
    expect_pixels(
        &conn,
        overlay,
        &reference(),
        "offers replace while Main stopped",
    );
    screenshot(&conn, overlay, "main-stopped-overlay");
    signal(main, nix::sys::signal::Signal::SIGKILL);
    session.until("Main dead, renderer alive", |s| {
        role(s, "main")["health"] == "exited" && pid(s, "overlay") == renderer
    });
    publish(&first, 107);
    expect_pixels(
        &conn,
        overlay,
        &reference(),
        "Main kill leaves overlay current",
    );
    session.restart("main");
    let state = session.until("explicit Main replacement", |s| {
        role(s, "main")["health"] == "responsive" && pid(s, "main") != main
    });
    let main = pid(&state, "main");
    let main_window = window(&state, "main");
    place_main(&conn, main_window);
    // Navigate actual native controls, with no test-only setter or event relay.
    click(&conn, &display, root, main_window, 275, 87);
    session.until("actual pointer opens Options", |s| {
        role(s, "main")["last"]["mainPage"] == 3
    });
    screenshot(&conn, main_window, "options-before");
    publish(&first, 108);
    click(&conn, &display, root, main_window, 231, 223);
    session.until("Main persisted score preference", |s| {
        role(s, "main")["last"]["scores"] == false && role(s, "overlay")["last"]["scores"] == false
    });
    expect_pixels(
        &conn,
        overlay,
        &reference(),
        "renderer independently reads Main preferences",
    );
    screenshot(&conn, overlay, "preferences-overlay");
    let saved: Value =
        serde_json::from_slice(&fs::read("session/preferences.json").unwrap()).unwrap();
    assert_eq!(saved["ShowArenasmithScore"], false);
    signal(renderer, nix::sys::signal::Signal::SIGSTOP);
    session.until("renderer frozen, Main still responsive", |s| {
        role(s, "overlay")["health"] == "unresponsive" && role(s, "main")["health"] == "responsive"
    });
    click(&conn, &display, root, main_window, 345, 87);
    session.until("Main navigation while renderer stopped", |s| {
        role(s, "main")["last"]["mainPage"] == 4
    });
    std::thread::sleep(Duration::from_millis(5100));
    assert!(
        !mapped(&conn, overlay),
        "Rust raster owner hides stale renderer pixels"
    );
    signal(renderer, nix::sys::signal::Signal::SIGKILL);
    session.until("renderer killed without killing Main", |s| {
        role(s, "overlay")["health"] == "exited" && pid(s, "main") == main
    });
    assert!(
        conn.get_window_attributes(overlay)
            .unwrap()
            .reply()
            .is_err(),
        "dead renderer's native child was reaped"
    );
    publish(&first, 109);
    session.restart("overlay");
    let state = session.until("explicit renderer recovery", |s| {
        role(s, "overlay")["health"] == "responsive" && pid(s, "overlay") != renderer
    });
    let overlay = window(&state, "overlay");
    expect_pixels(
        &conn,
        overlay,
        &reference(),
        "restarted renderer reads source/preferences",
    );
    assert_input_empty(&conn, overlay);
    // Stale and malformed observations are visibly distinct from fresh facts, without Main help.
    let last_facts = pixels(&conn, overlay, 320, 640);
    signal(main, nix::sys::signal::Signal::SIGSTOP);
    std::thread::sleep(Duration::from_millis(5200));
    session.until("independent stale-source notice", |s| {
        role(s, "overlay")["last"]["projection"] == "sourceUnavailable"
    });
    assert!(mapped(&conn, overlay));
    let notice = pixels(&conn, overlay, 320, 640);
    assert_eq!(notice.len(), 320 * 640 * 4);
    assert!(
        !same_pixels(&notice, &last_facts),
        "source failure cannot retain fresh advice pixels"
    );
    screenshot(&conn, overlay, "stale-notice");
    fs::write("session/observation.json", b"{broken").unwrap();
    std::thread::sleep(Duration::from_millis(400));
    expect_pixels(
        &conn,
        overlay,
        &notice,
        "malformed retains explicit unavailable visual",
    );
    publish(&first, 108);
    std::thread::sleep(Duration::from_millis(400));
    expect_pixels(
        &conn,
        overlay,
        &notice,
        "notice never resets fact high-water",
    );
    publish(&first, 110);
    expect_pixels(
        &conn,
        overlay,
        &reference(),
        "fresh valid recovery while Main frozen",
    );
    activate(root);
    std::thread::sleep(Duration::from_millis(250));
    assert!(!mapped(&conn, overlay), "game focus loss hides overlay");
    activate(game);
    publish(&first, 111);
    expect_pixels(&conn, overlay, &reference(), "focus recovery");
    conn.configure_window(
        game,
        &x11rb::protocol::xproto::ConfigureWindowAux::new()
            .x(570)
            .y(130)
            .width(670),
    )
    .unwrap()
    .check()
    .unwrap();
    std::thread::sleep(Duration::from_millis(250));
    let bounds = conn.get_geometry(overlay).unwrap().reply().unwrap();
    assert_eq!((bounds.x, bounds.y), (920, 130));
    assert_input_empty(&conn, overlay);
    screenshot(&conn, root, "isolated-desktop");
    session.command(json!({"schemaVersion":1,"command":"shutdown"}));
    let end = Instant::now() + Duration::from_secs(7);
    loop {
        if let Some(status) = session.owner.0.try_wait().unwrap() {
            assert!(status.success());
            break;
        }
        assert!(Instant::now() < end, "owner shutdown bounded");
        std::thread::sleep(Duration::from_millis(25));
    }
    for child in [main, pid(&state, "overlay")] {
        assert!(
            !std::path::Path::new(&format!("/proc/{child}")).exists(),
            "owned UI child survived shutdown"
        );
        assert!(
            nix::sys::signal::killpg(nix::unistd::Pid::from_raw(child), None).is_err(),
            "owned role group survived shutdown"
        );
    }
    assert!(
        conn.get_window_attributes(overlay)
            .unwrap()
            .reply()
            .is_err()
    );
    assert!(mapped(&conn, game));
    assert_eq!(
        conn.shape_get_rectangles(game, SK::INPUT)
            .unwrap()
            .reply()
            .unwrap()
            .rectangles
            .len(),
        1
    );
    // Owner SIGKILL must also terminate STOP'd UI roles and the STOP'd raster descendant.
    publish(&first, 200);
    let mut killed = Session::start(&display);
    let state = killed.until("owner-death scenario ready", |s| {
        ["main", "overlay"]
            .iter()
            .all(|r| role(s, r)["health"] == "responsive")
    });
    let main = pid(&state, "main");
    let renderer = pid(&state, "overlay");
    let overlay = window(&state, "overlay");
    let raster = conn
        .get_property(false, overlay, pid_atom, AtomEnum::CARDINAL, 0, 1)
        .unwrap()
        .reply()
        .unwrap()
        .value32()
        .unwrap()
        .next()
        .unwrap() as i32;
    for child in [main, renderer, raster] {
        signal(child, nix::sys::signal::Signal::SIGSTOP);
    }
    signal(
        killed.owner.0.id() as i32,
        nix::sys::signal::Signal::SIGKILL,
    );
    assert!(!killed.owner.0.wait().unwrap().success());
    let end = Instant::now() + Duration::from_secs(4);
    for group in [main, renderer] {
        loop {
            match nix::sys::wait::waitpid(
                nix::unistd::Pid::from_raw(-group),
                Some(nix::sys::wait::WaitPidFlag::WNOHANG),
            ) {
                Ok(nix::sys::wait::WaitStatus::StillAlive) => {
                    assert!(
                        Instant::now() < end,
                        "owner-death descendant remained alive"
                    );
                    std::thread::sleep(Duration::from_millis(20));
                }
                Ok(_) => continue,
                Err(nix::errno::Errno::ECHILD) => break,
                Err(error) => panic!("owner-death reap: {error}"),
            }
        }
        assert!(nix::sys::signal::killpg(nix::unistd::Pid::from_raw(group), None).is_err());
    }
    for child in [main, renderer, raster] {
        assert!(
            !std::path::Path::new(&format!("/proc/{child}")).exists(),
            "owner death left exact STOP'd child {child}"
        );
    }
    assert!(
        conn.get_window_attributes(overlay)
            .unwrap()
            .reply()
            .is_err()
    );
    writeln!(
        killed.log,
        "{}",
        json!({"ownerDeath":"SIGKILL","stoppedOwnedPids":[main,renderer,raster],"reaped":true})
    )
    .unwrap();
    let invalid_parent = Command::new("/capture/hdt-host")
        .args(["read-snapshot", "1024", "/capture/session/observation.json"])
        .env("HDT_OWNED_PARENT_PID", "2147483647")
        .output()
        .unwrap();
    assert!(
        !invalid_parent.status.success()
            && String::from_utf8_lossy(&invalid_parent.stderr)
                .contains("owned helper parent no longer exists")
    );
    fs::write("invalid-parent.stderr", invalid_parent.stderr).unwrap();
    publish(&first, 201);
    let mut invalid = Session::start(&display);
    let state = invalid.until("invalid-input cleanup scenario ready", |s| {
        ["main", "overlay"]
            .iter()
            .all(|r| role(s, r)["health"] == "responsive")
    });
    invalid.input.write_all(&vec![b'x'; 8193]).unwrap();
    invalid.input.flush().unwrap();
    let end = Instant::now() + Duration::from_secs(7);
    loop {
        if let Some(status) = invalid.owner.0.try_wait().unwrap() {
            assert_eq!(
                status.code(),
                Some(1),
                "oversized IPC must fail, not silently fall back"
            );
            break;
        }
        assert!(
            Instant::now() < end,
            "oversized owner input cleanup deadline"
        );
        std::thread::sleep(Duration::from_millis(20));
    }
    for name in ["main", "overlay"] {
        let child = pid(&state, name);
        assert!(!std::path::Path::new(&format!("/proc/{child}")).exists());
        assert!(nix::sys::signal::killpg(nix::unistd::Pid::from_raw(child), None).is_err());
    }
    assert!(
        fs::read_to_string("session-owner.stderr")
            .unwrap()
            .contains("oversized native session line")
    );
    writeln!(
        invalid.log,
        "{}",
        json!({"invalidInputBytes":8193,"exitCode":1,"reaped":true})
    )
    .unwrap();
    conn.destroy_window(game).unwrap().check().unwrap();
    eprintln!(
        "PASS independent real Main/renderer STOP, kill, explicit restart, actual pointer navigation/preferences, pixel replacement, stale/malformed/backward notices, focus/geometry/input-empty, exclusive lease, stopped-descendant owner-death and exact child/group reaping"
    );
}
