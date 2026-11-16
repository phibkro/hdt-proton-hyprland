//! Real protocol acceptance. Run only inside the project's private PID/mount/network sandbox.
use std::{
    io::{BufRead, BufReader},
    process::{Child, Command, Stdio},
    sync::mpsc,
    time::Duration,
};
use x11rb::{
    connection::Connection,
    protocol::{
        composite::{ConnectionExt as _, Redirect},
        render::{ConnectionExt as _, PictType},
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
    conn.flush().unwrap();
}
