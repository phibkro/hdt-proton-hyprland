//! Real protocol acceptance. Run only inside the project's private PID/mount/network sandbox.
use std::{
    io::{BufRead, BufReader, Write},
    process::{Child, Command, Stdio},
    sync::mpsc,
    time::{Duration, Instant},
};
use x11rb::{
    connection::Connection,
    protocol::{
        composite::{ConnectionExt as _, Redirect},
        render::{ConnectionExt as _, PictType},
        shape::{ConnectionExt as _, SK},
        xproto::{
            AtomEnum, ColormapAlloc, ConnectionExt as _, CreateGCAux, CreateWindowAux, PropMode,
            Rectangle, WindowClass,
        },
    },
    rust_connection::RustConnection,
    wrapper::ConnectionExt as _,
};

struct Server(Child);
impl Drop for Server {
    fn drop(&mut self) {
        let _ = self.0.kill();
        let _ = self.0.wait();
    }
}

fn property(conn: &RustConnection, window: u32) -> Option<u32> {
    let reply = conn
        .get_property(
            false,
            window,
            AtomEnum::WM_TRANSIENT_FOR,
            AtomEnum::WINDOW,
            0,
            1,
        )
        .unwrap()
        .reply()
        .unwrap();
    reply.value32().and_then(|mut values| values.next())
}

fn identity(conn: &RustConnection, window: u32, pid_atom: u32, pid: u32, title: &[u8]) {
    conn.change_property8(
        PropMode::REPLACE,
        window,
        AtomEnum::WM_NAME,
        AtomEnum::STRING,
        title,
    )
    .unwrap()
    .check()
    .unwrap();
    conn.change_property8(
        PropMode::REPLACE,
        window,
        AtomEnum::WM_CLASS,
        AtomEnum::STRING,
        b"fixture\0hdt-fixture\0",
    )
    .unwrap()
    .check()
    .unwrap();
    conn.change_property32(
        PropMode::REPLACE,
        window,
        pid_atom,
        AtomEnum::CARDINAL,
        &[pid],
    )
    .unwrap()
    .check()
    .unwrap();
}

fn run(display: &str, args: &[String], success: bool) -> String {
    let output = Command::new(
        std::env::var_os("HDT_TEST_HOST_BINARY")
            .unwrap_or_else(|| env!("CARGO_BIN_EXE_hdt-host").into()),
    )
    .args(args)
    .env("DISPLAY", display)
    .env_remove("XAUTHORITY")
    .output()
    .unwrap();
    let stderr = String::from_utf8(output.stderr).unwrap();
    assert_eq!(output.status.success(), success, "{args:?}: {stderr}");
    if success {
        String::from_utf8(output.stdout).unwrap()
    } else {
        stderr
    }
}

#[test]
#[ignore = "Requires Bubblewrap private /tmp, PID and network namespaces; HDT_TEST_XVFB executable"]
fn isolated_x11_relationship_and_argb_pixels() {
    for name in [
        "DISPLAY",
        "WAYLAND_DISPLAY",
        "XAUTHORITY",
        "DBUS_SESSION_BUS_ADDRESS",
    ] {
        assert!(
            std::env::var_os(name).is_none(),
            "inherited desktop environment: {name}"
        );
    }
    assert_eq!(
        std::env::var("HDT_TEST_X11_ISOLATED").as_deref(),
        Ok("1"),
        "parent must attest private namespaces and /tmp"
    );
    std::fs::create_dir_all("/tmp/.X11-unix").expect("Private X socket directory");
    assert!(
        std::fs::read_dir("/tmp/.X11-unix")
            .unwrap()
            .next()
            .is_none(),
        "Private socket directory must start empty"
    );
    let executable = std::env::var_os("HDT_TEST_XVFB").expect("HDT_TEST_XVFB required");
    let mut server = Server(
        Command::new(executable)
            .args([
                "-displayfd",
                "1",
                "-screen",
                "0",
                "128x128x24",
                "-nolisten",
                "tcp",
                "-ac",
            ])
            .stdin(Stdio::null())
            .stdout(Stdio::piped())
            .stderr(Stdio::inherit())
            .spawn()
            .unwrap(),
    );
    let stdout = server.0.stdout.take().unwrap();
    let (send, receive) = mpsc::channel();
    std::thread::spawn(move || {
        let mut line = String::new();
        let result = BufReader::new(stdout).read_line(&mut line).map(|_| line);
        let _ = send.send(result);
    });
    let number = receive
        .recv_timeout(Duration::from_secs(10))
        .expect("Xvfb startup deadline")
        .unwrap();
    let number: u16 = number
        .trim()
        .parse()
        .expect("Xvfb displayfd must contain a display number");
    let display = format!(":{number}");
    let (conn, screen) = x11rb::connect(Some(&display)).unwrap();
    let root = &conn.setup().roots[screen];
    let pid_atom = conn
        .intern_atom(false, b"_NET_WM_PID")
        .unwrap()
        .reply()
        .unwrap()
        .atom;
    let make_window = || {
        let window = conn.generate_id().unwrap();
        conn.create_window(
            root.root_depth,
            window,
            root.root,
            0,
            0,
            32,
            32,
            0,
            WindowClass::INPUT_OUTPUT,
            root.root_visual,
            &CreateWindowAux::new(),
        )
        .unwrap()
        .check()
        .unwrap();
        window
    };
    let game = make_window();
    let overlay = make_window();
    identity(&conn, game, pid_atom, 200, b"Hearthstone");
    identity(&conn, overlay, pid_atom, 100, b"HearthstoneOverlay");
    let apply = vec![
        "overlay-link".into(),
        "apply".into(),
        "100".into(),
        "200".into(),
        "hdt-fixture".into(),
    ];
    let applied: serde_json::Value = serde_json::from_str(&run(&display, &apply, true)).unwrap();
    assert_eq!(applied["overlay"], overlay);
    assert_eq!(applied["game"], game);
    assert_eq!(applied["original"], 0);
    assert_eq!(property(&conn, overlay), Some(game));
    let restore = vec![
        "overlay-link".into(),
        "restore".into(),
        "100".into(),
        "hdt-fixture".into(),
        overlay.to_string(),
        game.to_string(),
        "0".into(),
    ];
    run(&display, &restore, true);
    assert_eq!(property(&conn, overlay), None);
    conn.change_property32(
        PropMode::REPLACE,
        overlay,
        AtomEnum::WM_TRANSIENT_FOR,
        AtomEnum::WINDOW,
        &[root.root],
    )
    .unwrap()
    .check()
    .unwrap();
    let reapplied: serde_json::Value = serde_json::from_str(&run(&display, &apply, true)).unwrap();
    assert_eq!(reapplied["original"], root.root);
    let mut restore_original = restore.clone();
    restore_original[6] = root.root.to_string();
    run(&display, &restore_original, true);
    assert_eq!(property(&conn, overlay), Some(root.root));
    conn.delete_property(overlay, AtomEnum::WM_TRANSIENT_FOR.into())
        .unwrap()
        .check()
        .unwrap();
    run(&display, &apply, true);
    conn.change_property32(
        PropMode::REPLACE,
        overlay,
        AtomEnum::WM_TRANSIENT_FOR,
        AtomEnum::WINDOW,
        &[root.root],
    )
    .unwrap()
    .check()
    .unwrap();
    assert!(run(&display, &restore, false).contains("refusing to overwrite"));
    assert_eq!(property(&conn, overlay), Some(root.root));
    let duplicate = make_window();
    identity(&conn, duplicate, pid_atom, 100, b"HearthstoneOverlay");
    assert!(run(&display, &apply, false).contains("ambiguous window identity"));
    conn.destroy_window(duplicate).unwrap().check().unwrap();
    conn.change_property32(
        PropMode::REPLACE,
        overlay,
        pid_atom,
        AtomEnum::CARDINAL,
        &[100, 100],
    )
    .unwrap()
    .check()
    .unwrap();
    assert!(run(&display, &apply, false).contains("expected window not found"));
    conn.change_property32(
        PropMode::REPLACE,
        overlay,
        pid_atom,
        AtomEnum::WINDOW,
        &[100],
    )
    .unwrap()
    .check()
    .unwrap();
    assert!(run(&display, &apply, false).contains("expected window not found"));
    conn.change_property8(
        PropMode::REPLACE,
        overlay,
        AtomEnum::WM_NAME,
        AtomEnum::STRING,
        &vec![b'x'; 20_000],
    )
    .unwrap()
    .check()
    .unwrap();
    assert!(run(&display, &apply, false).contains("expected window not found"));
    assert_eq!(property(&conn, overlay), Some(root.root));
    identity(&conn, overlay, pid_atom, 101, b"HearthstoneOverlay");
    assert!(run(&display, &apply, false).contains("expected window not found"));
    // A wrong PID makes restore a no-op, preserving someone else's relationship.
    run(&display, &restore, true);
    assert_eq!(property(&conn, overlay), Some(root.root));
    conn.destroy_window(overlay).unwrap().check().unwrap();

    conn.composite_query_version(0, 2).unwrap().reply().unwrap();
    let formats = conn.render_query_pict_formats().unwrap().reply().unwrap();
    let picture = formats
        .formats
        .iter()
        .find(|format| {
            format.type_ == PictType::DIRECT
                && format.depth == 32
                && format.direct.alpha_shift == 24
                && format.direct.alpha_mask == 255
        })
        .expect("Xvfb ARGB32 direct format");
    let visual = formats.screens[screen]
        .depths
        .iter()
        .flat_map(|depth| &depth.visuals)
        .find(|visual| visual.format == picture.id)
        .expect("ARGB32 visual")
        .visual;
    let colormap = conn.generate_id().unwrap();
    conn.create_colormap(ColormapAlloc::NONE, colormap, root.root, visual)
        .unwrap()
        .check()
        .unwrap();
    let argb = conn.generate_id().unwrap();
    conn.create_window(
        32,
        argb,
        root.root,
        10,
        10,
        4,
        1,
        0,
        WindowClass::INPUT_OUTPUT,
        visual,
        &CreateWindowAux::new()
            .colormap(colormap)
            .border_pixel(0)
            .background_pixel(0),
    )
    .unwrap()
    .check()
    .unwrap();
    identity(&conn, argb, pid_atom, 300, b"HearthstoneOverlay");
    conn.composite_redirect_window(argb, Redirect::AUTOMATIC)
        .unwrap()
        .check()
        .unwrap();
    conn.map_window(argb).unwrap().check().unwrap();
    let gc = conn.generate_id().unwrap();
    conn.create_gc(gc, argb, &CreateGCAux::new())
        .unwrap()
        .check()
        .unwrap();
    let pixels = [0_u32, 0xffffffff, 0x01000000, 0x80010203];
    for (x, pixel) in pixels.into_iter().enumerate() {
        conn.change_gc(
            gc,
            &x11rb::protocol::xproto::ChangeGCAux::new().foreground(pixel),
        )
        .unwrap()
        .check()
        .unwrap();
        conn.poly_fill_rectangle(
            argb,
            gc,
            &[Rectangle {
                x: x as i16,
                y: 0,
                width: 1,
                height: 1,
            }],
        )
        .unwrap()
        .check()
        .unwrap();
    }
    let probe = vec![
        "surface-probe".into(),
        argb.to_string(),
        "300".into(),
        "10".into(),
        "10".into(),
        "4".into(),
        "1".into(),
    ];
    let result: serde_json::Value = serde_json::from_str(&run(&display, &probe, true)).unwrap();
    let hash = pixels
        .into_iter()
        .fold(14_695_981_039_346_656_037_u64, |hash, pixel| {
            (hash ^ u64::from(pixel)).wrapping_mul(1_099_511_628_211)
        });
    assert_eq!(
        result,
        serde_json::json!({"width":4,"height":1,"transparentPixels":1,
        "opaquePixels":1,"partialAlphaPixels":2,"oneAlphaPixels":1,"pixelHash":format!("{hash:016x}")})
    );
    let mut wrong_owner = probe.clone();
    wrong_owner[2] = "301".into();
    assert!(run(&display, &wrong_owner, false).contains("overlay owner mismatch"));
    if let Some(legacy) = std::env::var_os("HDT_TEST_C_SURFACE") {
        let output = Command::new(legacy)
            .args(&probe[1..])
            .env("DISPLAY", &display)
            .env_remove("XAUTHORITY")
            .output()
            .unwrap();
        assert!(
            output.status.success(),
            "{}",
            String::from_utf8_lossy(&output.stderr)
        );
        let legacy: serde_json::Value = serde_json::from_slice(&output.stdout).unwrap();
        assert_eq!(
            legacy, result,
            "Rust and legacy C backing-pixmap reports differ"
        );
    }
    conn.free_gc(gc).unwrap().check().unwrap();
    conn.destroy_window(argb).unwrap().check().unwrap();
    conn.free_colormap(colormap).unwrap().check().unwrap();
    conn.destroy_window(game).unwrap().check().unwrap();
    portable_bridge_acceptance(&conn, &display, root.root, pid_atom);
    conn.flush().unwrap();
}

fn portable_bridge_acceptance(conn: &RustConnection, display: &str, root: u32, pid_atom: u32) {
    let screen = &conn.setup().roots[0];
    let make = |pid, title: &[u8], x| {
        let window = conn.generate_id().unwrap();
        conn.create_window(
            screen.root_depth,
            window,
            root,
            x,
            x,
            32,
            32,
            0,
            WindowClass::INPUT_OUTPUT,
            screen.root_visual,
            &CreateWindowAux::new(),
        )
        .unwrap()
        .check()
        .unwrap();
        identity(conn, window, pid_atom, pid, title);
        conn.map_window(window).unwrap().check().unwrap();
        window
    };
    let game = make(400, b"Hearthstone", 0);
    let overlay = make(500, b"HearthstoneOverlay", 0);
    let main = make(500, b"Hearthstone Deck Tracker", 80);
    let wrapper = make(600, b"WM frame", 0);
    conn.reparent_window(game, wrapper, 0, 0)
        .unwrap()
        .check()
        .unwrap();
    conn.map_window(game).unwrap().check().unwrap();
    let clients = conn
        .intern_atom(false, b"_NET_CLIENT_LIST")
        .unwrap()
        .reply()
        .unwrap()
        .atom;
    conn.change_property32(
        PropMode::REPLACE,
        root,
        clients,
        AtomEnum::WINDOW,
        &[game, overlay, main],
    )
    .unwrap()
    .check()
    .unwrap();
    let inventory: serde_json::Value = serde_json::from_str(&run(
        display,
        &["x11-desktop".into(), "windows".into(), "hdt-fixture".into()],
        true,
    ))
    .unwrap();
    assert_eq!(
        inventory.as_array().unwrap().len(),
        3,
        "managed reparented game and root overlay are both discoverable without duplicates"
    );
    let active = conn
        .intern_atom(false, b"_NET_ACTIVE_WINDOW")
        .unwrap()
        .reply()
        .unwrap()
        .atom;
    conn.change_property32(PropMode::REPLACE, root, active, AtomEnum::WINDOW, &[game])
        .unwrap()
        .check()
        .unwrap();
    conn.set_input_focus(
        x11rb::protocol::xproto::InputFocus::PARENT,
        game,
        x11rb::CURRENT_TIME,
    )
    .unwrap()
    .check()
    .unwrap();
    conn.warp_pointer(x11rb::NONE, root, 0, 0, 0, 0, 20, 20)
        .unwrap()
        .check()
        .unwrap();
    let executable = std::env::var_os("HDT_TEST_HOST_BINARY").unwrap();
    let mut owner = Server(
        Command::new(&executable)
            .args(["x11-desktop", "bridge", "500", "400", "hdt-fixture"])
            .env("DISPLAY", display)
            .stdin(Stdio::piped())
            .stdout(Stdio::piped())
            .stderr(Stdio::inherit())
            .spawn()
            .unwrap(),
    );
    let output = owner.0.stdout.take().unwrap();
    let (sender, receiver) = mpsc::channel();
    std::thread::spawn(move || {
        for line in BufReader::new(output).lines() {
            let value: serde_json::Value = serde_json::from_str(&line.unwrap()).unwrap();
            if sender.send(value).is_err() {
                break;
            }
        }
    });
    let ready = receiver.recv_timeout(Duration::from_secs(3)).unwrap();
    assert_eq!(ready["kind"], "ready");
    assert_eq!(ready["overlay"], overlay);
    assert_eq!(property(conn, overlay), Some(game));
    assert!(
        conn.shape_get_rectangles(overlay, SK::INPUT)
            .unwrap()
            .reply()
            .unwrap()
            .rectangles
            .is_empty()
    );
    let wait = |predicate: &dyn Fn(&serde_json::Value) -> bool| {
        let deadline = Instant::now() + Duration::from_secs(4);
        loop {
            let remaining = deadline
                .checked_duration_since(Instant::now())
                .expect("native observation deadline");
            let value = receiver.recv_timeout(remaining).unwrap();
            if predicate(&value) {
                break;
            }
        }
    };
    owner
        .0
        .stdin
        .as_mut()
        .unwrap()
        .write_all(b"v1 32 32 1 8 8 8 8\n")
        .unwrap();
    wait(&|v| v["controls"] == 1);
    let regions = conn
        .shape_get_rectangles(overlay, SK::INPUT)
        .unwrap()
        .reply()
        .unwrap()
        .rectangles;
    // A competing owner must fail before clearing the first owner's region.
    let rejected = run(
        display,
        &[
            "x11-desktop".into(),
            "bridge".into(),
            "500".into(),
            "400".into(),
            "hdt-fixture".into(),
        ],
        false,
    );
    assert!(rejected.contains("another portable input owner"));
    assert_eq!(
        conn.shape_get_rectangles(overlay, SK::INPUT)
            .unwrap()
            .reply()
            .unwrap()
            .rectangles
            .len(),
        1
    );
    assert_eq!(regions.len(), 1);
    assert_eq!(
        (
            regions[0].x,
            regions[0].y,
            regions[0].width,
            regions[0].height
        ),
        (8, 8, 8, 8)
    );
    conn.warp_pointer(x11rb::NONE, root, 0, 0, 0, 0, 10, 10)
        .unwrap()
        .check()
        .unwrap();
    wait(&|v| v["focused"] == true && v["pointerOverControl"] == true);
    wait(&|v| v["fresh"] == false && v["controls"] == 0);
    assert!(
        conn.shape_get_rectangles(overlay, SK::INPUT)
            .unwrap()
            .reply()
            .unwrap()
            .rectangles
            .is_empty()
    );
    assert_eq!(
        conn.get_input_focus().unwrap().reply().unwrap().focus,
        game,
        "expired lease returns owned focus to the game stand-in"
    );
    drop(owner.0.stdin.take());
    assert!(owner.0.wait().unwrap().success());
    assert_eq!(property(conn, overlay), None);
    assert!(
        conn.shape_get_rectangles(overlay, SK::INPUT)
            .unwrap()
            .reply()
            .unwrap()
            .rectangles
            .is_empty()
    );
    run(
        display,
        &[
            "x11-desktop".into(),
            "bridge".into(),
            "500".into(),
            "401".into(),
            "hdt-fixture".into(),
        ],
        false,
    );
    assert_eq!(
        property(conn, overlay),
        None,
        "rejected game identity cannot alter the overlay"
    );
    portable_controller_faults(conn, display, &[overlay, game, main]);
    for window in [overlay, game, main, wrapper] {
        conn.destroy_window(window).unwrap().check().unwrap();
    }
}

fn portable_controller_faults(conn: &RustConnection, display: &str, windows: &[u32; 3]) {
    use serde_json::json;
    let [overlay, game, main] = *windows;
    for window in [overlay, game, main] {
        conn.change_property8(
            PropMode::REPLACE,
            window,
            AtomEnum::WM_CLASS,
            AtomEnum::STRING,
            b"fixture\0steam_app_123\0",
        )
        .unwrap()
        .check()
        .unwrap();
    }
    let temp = tempfile::tempdir().unwrap();
    let evidence = temp.path().join("evidence");
    std::fs::create_dir(&evidence).unwrap();
    let region = evidence.join("input-region.json");
    let ack = evidence.join("control-input.json");
    let publish = |session: &str, age: i64| {
        let now = std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .unwrap()
            .as_millis() as i64;
        std::fs::write(
            &region,
            serde_json::to_vec(&json!({"session":session,"observedMS":now-age,
            "overlayHwnd":123,"width":32,"height":32,"rectangles":[[8,8,8,8]]}))
            .unwrap(),
        )
        .unwrap();
    };
    let active = conn
        .intern_atom(false, b"_NET_ACTIVE_WINDOW")
        .unwrap()
        .reply()
        .unwrap()
        .atom;
    conn.change_property32(
        PropMode::REPLACE,
        conn.setup().roots[0].root,
        active,
        AtomEnum::WINDOW,
        &[game],
    )
    .unwrap()
    .check()
    .unwrap();
    let executable = std::env::var_os("HDT_TEST_HOST_BINARY").unwrap();
    let command = || {
        let mut command = Command::new(&executable);
        command
            .args(["x11-control", "owned"])
            .env("DISPLAY", display)
            .env("HS_APP_ID", "123")
            .env("HDT_PROJECT_DIR", temp.path())
            .env("HOME", temp.path());
        command
    };
    let mut owner = Server(
        command()
            .stdout(Stdio::null())
            .stderr(Stdio::inherit())
            .spawn()
            .unwrap(),
    );
    let wait_ack = |check: &dyn Fn(&serde_json::Value) -> bool| {
        let deadline = Instant::now() + Duration::from_secs(4);
        loop {
            if let Ok(bytes) = std::fs::read(&ack)
                && let Ok(value) = serde_json::from_slice::<serde_json::Value>(&bytes)
                && check(&value)
            {
                return;
            }
            assert!(
                Instant::now() < deadline,
                "controller acknowledgement timeout"
            );
            std::thread::sleep(Duration::from_millis(10));
        }
    };
    publish("owned", 0);
    wait_ack(&|v| v["fresh"] == true && v["controls"] == 1);
    let second = command().output().unwrap();
    assert!(!second.status.success());
    assert!(String::from_utf8_lossy(&second.stderr).contains("another portable controller"));
    assert_eq!(
        conn.shape_get_rectangles(overlay, SK::INPUT)
            .unwrap()
            .reply()
            .unwrap()
            .rectangles
            .len(),
        1
    );
    // Corrupt producer, wrong generation, old/future timestamp, then recovery.
    std::fs::write(&region, b"{").unwrap();
    wait_ack(&|v| v["nativeReady"] == false);
    assert!(
        conn.shape_get_rectangles(overlay, SK::INPUT)
            .unwrap()
            .reply()
            .unwrap()
            .rectangles
            .is_empty()
    );
    for (session, age) in [("other", 0), ("owned", 5000), ("owned", -5000)] {
        publish("owned", 0);
        wait_ack(&|v| v["fresh"] == true && v["controls"] == 1);
        assert_eq!(
            conn.shape_get_rectangles(overlay, SK::INPUT)
                .unwrap()
                .reply()
                .unwrap()
                .rectangles
                .len(),
            1
        );
        publish(session, age);
        wait_ack(&|v| v["nativeReady"] == true && v["fresh"] == false && v["controls"] == 0);
        assert!(
            conn.shape_get_rectangles(overlay, SK::INPUT)
                .unwrap()
                .reply()
                .unwrap()
                .rectangles
                .is_empty()
        );
    }
    publish("owned", 0);
    wait_ack(&|v| v["fresh"] == true && v["controls"] == 1);
    assert_eq!(
        conn.shape_get_rectangles(overlay, SK::INPUT)
            .unwrap()
            .reply()
            .unwrap()
            .rectangles
            .len(),
        1
    );
    std::fs::remove_file(&region).unwrap();
    wait_ack(&|v| v["fresh"] == false && v["controls"] == 0);
    assert!(
        conn.shape_get_rectangles(overlay, SK::INPUT)
            .unwrap()
            .reply()
            .unwrap()
            .rectangles
            .is_empty()
    );
    nix::sys::signal::kill(
        nix::unistd::Pid::from_raw(owner.0.id() as i32),
        nix::sys::signal::Signal::SIGTERM,
    )
    .unwrap();
    assert!(owner.0.wait().unwrap().success());
    wait_ack(&|v| v["nativeReady"] == false);
    assert_eq!(property(conn, overlay), None);
}
