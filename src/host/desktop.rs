//! Experimental portable X11 owner. Not selected by the installed Hyprland launcher.
//! Protocol and ownership rules derive from this project's unpublished MIT Xlib prototype.
use std::{
    collections::BTreeSet,
    ffi::OsString,
    io::{Read, Write},
    os::{fd::AsFd, unix::fs::FileTypeExt},
    sync::{
        Arc,
        atomic::{AtomicBool, Ordering},
    },
    time::{Duration, Instant, SystemTime, UNIX_EPOCH},
};

use anyhow::{Context, Result, bail, ensure};
use nix::{
    fcntl::{FcntlArg, OFlag, fcntl},
    poll::{PollFd, PollFlags, poll},
};
use serde::{Deserialize, Serialize};
use serde_json::{Value, json};
use x11rb::{
    connection::Connection,
    protocol::{
        randr::ConnectionExt as _,
        shape::{ConnectionExt as _, SK, SO},
        xproto::{
            AtomEnum, ClientMessageEvent, ClipOrdering, ConfigureWindowAux, ConnectionExt as _,
            EventMask, InputFocus, MapState, PropMode, Rectangle, StackMode, Window,
        },
    },
    rust_connection::RustConnection,
    wrapper::ConnectionExt as _,
};

use crate::x11::{atom, identity, vanished};

const MAX_RECTANGLES: usize = 2048;

/// Validate the production wire format without opening a display or changing state.
/// Shared by offline fuzzing and benchmarks, so they exercise the real parser.
pub fn validate_input_frame(bytes: &[u8]) -> Result<()> {
    Frame::parse(std::str::from_utf8(bytes)?).map(|_| ())
}

#[derive(Debug)]
struct Frame {
    width: u16,
    height: u16,
    rectangles: Vec<Rectangle>,
}
impl Frame {
    fn parse(line: &str) -> Result<Self> {
        ensure!(line.len() < 65_536, "input frame exceeds bound");
        let mut tokens = line.split_ascii_whitespace();
        ensure!(tokens.next() == Some("v1"), "unknown input protocol");
        let mut number = || -> Result<u16> {
            let token = tokens.next().context("incomplete input frame")?;
            ensure!(
                !token.is_empty() && token.bytes().all(|x| x.is_ascii_digit()),
                "invalid input number"
            );
            token.parse().context("invalid input number")
        };
        let width = number()?;
        let height = number()?;
        let count = number()?;
        ensure!(
            width > 0 && height > 0 && usize::from(count) <= MAX_RECTANGLES,
            "invalid frame dimensions/count"
        );
        let mut raw = Vec::with_capacity(usize::from(count));
        for _ in 0..count {
            raw.push([number()?, number()?, number()?, number()?]);
        }
        ensure!(tokens.next().is_none(), "extra input fields");
        Self::new(width, height, &raw)
    }
    fn new(width: u16, height: u16, raw: &[[u16; 4]]) -> Result<Self> {
        ensure!(
            width > 0 && height > 0 && raw.len() <= MAX_RECTANGLES,
            "invalid frame dimensions/count"
        );
        let mut rectangles = Vec::with_capacity(raw.len());
        for &[x, y, w, h] in raw {
            ensure!(
                w > 0
                    && h > 0
                    && u32::from(x) + u32::from(w) <= u32::from(width)
                    && u32::from(y) + u32::from(h) <= u32::from(height),
                "input rectangle outside overlay"
            );
            rectangles.push(Rectangle {
                x: x.try_into().context("input x exceeds X11 range")?,
                y: y.try_into().context("input y exceeds X11 range")?,
                width: w,
                height: h,
            });
        }
        Ok(Self {
            width,
            height,
            rectangles,
        })
    }
}

#[derive(Serialize)]
struct Client {
    address: String,
    pid: u32,
    title: String,
    class: String,
    at: [i16; 2],
    size: [u16; 2],
    xwayland: Option<bool>,
    fullscreen: Option<bool>,
}

struct Display {
    conn: RustConnection,
    root: Window,
    pid_atom: u32,
}
impl Display {
    fn open() -> Result<Self> {
        let (conn, screen) = x11rb::connect(None).context("cannot open X display")?;
        let root = conn.setup().roots[screen].root;
        let pid_atom = atom(&conn, b"_NET_WM_PID")?;
        Ok(Self {
            conn,
            root,
            pid_atom,
        })
    }
    fn window_property(&self, window: Window, name: &[u8]) -> Result<Vec<u32>> {
        let value = self
            .conn
            .get_property(
                false,
                window,
                atom(&self.conn, name)?,
                AtomEnum::WINDOW,
                0,
                65_536,
            )?
            .reply()?;
        ensure!(
            value.bytes_after == 0,
            "window property exceeds inventory bound"
        );
        Ok(value.value32().map_or_else(Vec::new, Iterator::collect))
    }
    fn clients(&self) -> Result<BTreeSet<Window>> {
        let mut windows: BTreeSet<_> = self
            .window_property(self.root, b"_NET_CLIENT_LIST")?
            .into_iter()
            .collect();
        windows.extend(self.conn.query_tree(self.root)?.reply()?.children);
        Ok(windows)
    }
    // X selections are scoped to the server, and released when this connection
    // closes. A short server grab makes check-and-claim atomic across owners.
    fn claim(&self, overlay: Window) -> Result<()> {
        let selection = atom(
            &self.conn,
            format!("_HDT_PROTON_INPUT_OWNER_{overlay}").as_bytes(),
        )?;
        let lock = self.conn.generate_id()?;
        self.conn
            .create_window(
                0,
                lock,
                self.root,
                0,
                0,
                1,
                1,
                0,
                x11rb::protocol::xproto::WindowClass::INPUT_ONLY,
                0,
                &x11rb::protocol::xproto::CreateWindowAux::new(),
            )?
            .check()?;
        self.conn.grab_server()?.check()?;
        let result = (|| {
            ensure!(
                self.conn.get_selection_owner(selection)?.reply()?.owner == 0,
                "another portable input owner already controls this overlay"
            );
            self.conn
                .set_selection_owner(lock, selection, x11rb::CURRENT_TIME)?
                .check()?;
            ensure!(
                self.conn.get_selection_owner(selection)?.reply()?.owner == lock,
                "portable input ownership claim failed"
            );
            Ok(())
        })();
        self.conn.ungrab_server()?.check()?;
        result
    }
    fn matches(&self, window: Window, pid: u32, title: &str, class: &str) -> Result<bool> {
        match identity(&self.conn, window, self.pid_atom, pid, title, class) {
            Err(e) if vanished(&e) => Ok(false),
            result => result,
        }
    }
    fn find(&self, pid: u32, title: &str, class: &str) -> Result<Window> {
        let mut found = None;
        for window in self.clients()? {
            if self.matches(window, pid, title, class)? {
                ensure!(found.is_none(), "ambiguous window identity");
                found = Some(window);
            }
        }
        found.context("expected window not found")
    }
    fn active(&self) -> Result<Window> {
        Ok(self
            .window_property(self.root, b"_NET_ACTIVE_WINDOW")?
            .first()
            .copied()
            .unwrap_or(0))
    }
    fn focused_within(&self, window: Window) -> Result<bool> {
        let mut focus = self.conn.get_input_focus()?.reply()?.focus;
        for _ in 0..16 {
            if focus <= 1 {
                return Ok(false);
            }
            if focus == window {
                return Ok(true);
            }
            focus = self.conn.query_tree(focus)?.reply()?.parent;
        }
        Ok(false)
    }
    fn request(&self, window: Window, name: &[u8], data: [u32; 5]) -> Result<()> {
        let event = ClientMessageEvent::new(32, window, atom(&self.conn, name)?, data);
        self.conn
            .send_event(
                false,
                self.root,
                EventMask::SUBSTRUCTURE_NOTIFY | EventMask::SUBSTRUCTURE_REDIRECT,
                event,
            )?
            .check()?;
        self.conn.flush()?;
        Ok(())
    }
    fn above(&self, window: Window) -> Result<bool> {
        let value = self
            .conn
            .get_property(
                false,
                window,
                atom(&self.conn, b"_NET_WM_STATE")?,
                AtomEnum::ATOM,
                0,
                1024,
            )?
            .reply()?;
        ensure!(value.bytes_after == 0, "window state exceeds bound");
        let above = atom(&self.conn, b"_NET_WM_STATE_ABOVE")?;
        Ok(value
            .value32()
            .is_some_and(|mut values| values.any(|x| x == above)))
    }
    fn set_above(&self, window: Window, above: bool) -> Result<()> {
        self.request(
            window,
            b"_NET_WM_STATE",
            [
                u32::from(above),
                atom(&self.conn, b"_NET_WM_STATE_ABOVE")?,
                0,
                2,
                0,
            ],
        )
    }
    fn shape(&self, window: Window, rectangles: &[Rectangle]) -> Result<()> {
        let previous = self.conn.shape_get_rectangles(window, SK::INPUT)?.reply()?;
        if !previous
            .rectangles
            .iter()
            .map(|r| (r.x, r.y, r.width, r.height))
            .eq(rectangles.iter().map(|r| (r.x, r.y, r.width, r.height)))
        {
            self.conn
                .shape_rectangles(
                    SO::SET,
                    SK::INPUT,
                    ClipOrdering::UNSORTED,
                    window,
                    0,
                    0,
                    rectangles,
                )?
                .check()?;
        }
        Ok(())
    }
    fn windows(&self, class: &str) -> Result<Vec<Client>> {
        let mut result = Vec::new();
        for window in self.clients()? {
            let pid = self
                .conn
                .get_property(false, window, self.pid_atom, AtomEnum::CARDINAL, 0, 1)?
                .reply();
            let pid = match pid {
                Ok(value) => value
                    .value32()
                    .and_then(|mut values| values.next())
                    .unwrap_or(0),
                Err(e) if matches!(&e, x11rb::errors::ReplyError::X11Error(error) if error.error_kind == x11rb::protocol::ErrorKind::Window) =>
                {
                    continue;
                }
                Err(e) => return Err(e.into()),
            };
            if pid == 0 {
                continue;
            }
            for title in [
                "Hearthstone",
                "HearthstoneOverlay",
                "Hearthstone Deck Tracker",
                "Battle.net",
            ] {
                if !self.matches(window, pid, title, class)? {
                    continue;
                }
                let attr = self.conn.get_window_attributes(window)?.reply()?;
                if attr.map_state != MapState::VIEWABLE {
                    continue;
                }
                let geometry = self.conn.get_geometry(window)?.reply()?;
                let position = self
                    .conn
                    .translate_coordinates(window, self.root, 0, 0)?
                    .reply()?;
                result.push(Client {
                    address: format!("0x{window:x}"),
                    pid,
                    title: title.to_owned(),
                    class: class.to_owned(),
                    at: [position.dst_x, position.dst_y],
                    size: [geometry.width, geometry.height],
                    xwayland: None,
                    fullscreen: None,
                });
            }
        }
        Ok(result)
    }
}

struct Bridge {
    display: Display,
    overlay: Window,
    game: Window,
    main: Window,
    pid: u32,
    game_pid: u32,
    class: String,
    original_parent: Window,
    original_above: bool,
    last_above: Option<bool>,
    focus_owned: bool,
}
impl Bridge {
    fn new(display: Display, pid: u32, game_pid: u32, class: String) -> Result<Self> {
        let version = display.conn.shape_query_version()?.reply()?;
        ensure!(
            (version.major_version, version.minor_version) >= (1, 1),
            "X Shape 1.1 input regions unavailable"
        );
        let overlay = display.find(pid, "HearthstoneOverlay", &class)?;
        let game = display.find(game_pid, "Hearthstone", &class)?;
        let main = display.find(pid, "Hearthstone Deck Tracker", &class)?;
        display.claim(overlay)?;
        let original_parent = display
            .window_property(overlay, b"WM_TRANSIENT_FOR")?
            .first()
            .copied()
            .unwrap_or(0);
        let original_above = display.above(overlay)?;
        let owner = Self {
            display,
            overlay,
            game,
            main,
            pid,
            game_pid,
            class,
            original_parent,
            original_above,
            last_above: None,
            focus_owned: false,
        };
        owner.display.shape(overlay, &[])?;
        owner
            .display
            .conn
            .change_property32(
                PropMode::REPLACE,
                overlay,
                AtomEnum::WM_TRANSIENT_FOR,
                AtomEnum::WINDOW,
                &[game],
            )?
            .check()?;
        ensure!(
            owner
                .display
                .window_property(overlay, b"WM_TRANSIENT_FOR")?
                == [game],
            "overlay parent readback failed"
        );
        Ok(owner)
    }
    fn validate(&self) -> Result<()> {
        ensure!(
            self.display
                .matches(self.overlay, self.pid, "HearthstoneOverlay", &self.class)?
                && self
                    .display
                    .matches(self.game, self.game_pid, "Hearthstone", &self.class)?
                && self.display.matches(
                    self.main,
                    self.pid,
                    "Hearthstone Deck Tracker",
                    &self.class
                )?,
            "window identity changed"
        );
        Ok(())
    }
    fn active(&self) -> Result<bool> {
        let active = self.display.active()?;
        Ok(active == self.game
            || active == self.overlay
            || (active == 0
                && (self.display.focused_within(self.game)?
                    || self.display.focused_within(self.overlay)?))
            || (active == self.main && self.display.focused_within(self.overlay)?))
    }
    fn focus(&mut self, active: bool, hit: bool) -> Result<()> {
        if active && hit {
            if !self.focus_owned && !self.display.focused_within(self.overlay)? {
                self.display
                    .conn
                    .set_input_focus(InputFocus::PARENT, self.overlay, x11rb::CURRENT_TIME)?
                    .check()?;
                self.focus_owned = true;
            }
        } else {
            if active && self.focus_owned && self.display.focused_within(self.overlay)? {
                self.display
                    .conn
                    .set_input_focus(InputFocus::PARENT, self.game, x11rb::CURRENT_TIME)?
                    .check()?;
            }
            self.focus_owned = false;
        }
        Ok(())
    }
    fn tick(
        &mut self,
        frame: Option<&(Frame, Instant)>,
        previous: &mut Option<(bool, bool)>,
        win32_input: Option<Win32Input>,
    ) -> Result<Value> {
        self.validate()?;
        let attr = self
            .display
            .conn
            .get_window_attributes(self.overlay)?
            .reply()?;
        let geometry = self.display.conn.get_geometry(self.overlay)?.reply()?;
        let active = self.active()?;
        let fresh = frame.is_some_and(|(f, t)| {
            t.elapsed() < Duration::from_secs(1)
                && attr.map_state == MapState::VIEWABLE
                && f.width == geometry.width
                && f.height == geometry.height
        });
        let rectangles = frame
            .filter(|_| fresh && active)
            .map_or(&[][..], |(f, _)| f.rectangles.as_slice());
        let pointer = self.display.conn.query_pointer(self.overlay)?.reply()?;
        let hit = pointer.same_screen
            && rectangles.iter().any(|r| {
                i32::from(pointer.win_x) >= i32::from(r.x)
                    && i32::from(pointer.win_y) >= i32::from(r.y)
                    && i32::from(pointer.win_x) < i32::from(r.x) + i32::from(r.width)
                    && i32::from(pointer.win_y) < i32::from(r.y) + i32::from(r.height)
            });
        self.focus(active, hit)?;
        // Prepare Wine before admitting controls, then retain their shape
        // across asynchronous focus notifications. Focus is not input readiness.
        let admitted = match win32_input {
            None => rectangles,
            Some(Win32Input::Prepared) => rectangles,
            _ => &[],
        };
        self.display.shape(self.overlay, admitted)?;
        if *previous != Some((active, attr.override_redirect)) {
            if attr.override_redirect {
                self.display
                    .conn
                    .configure_window(
                        self.overlay,
                        &ConfigureWindowAux::new().stack_mode(if active {
                            StackMode::ABOVE
                        } else {
                            StackMode::BELOW
                        }),
                    )?
                    .check()?;
            } else {
                self.display.set_above(self.overlay, active)?;
                self.last_above = Some(active);
            }
            *previous = Some((active, attr.override_redirect));
        }
        self.display.conn.flush()?;
        Ok(
            json!({"kind":"state","observedMS":SystemTime::now().duration_since(UNIX_EPOCH)?.as_millis(),
            "fresh":fresh,"active":active,"focused":self.display.focused_within(self.overlay)?,
            "pointerOverControl":hit,"controls":rectangles.len(),"inputControls":admitted.len()}),
        )
    }
    fn cleanup(&mut self) -> Result<()> {
        if !self
            .display
            .matches(self.overlay, self.pid, "HearthstoneOverlay", &self.class)?
        {
            return Ok(());
        }
        self.display.shape(self.overlay, &[])?;
        if self
            .display
            .matches(self.game, self.game_pid, "Hearthstone", &self.class)?
        {
            self.focus(self.active()?, false)?;
        }
        if let Some(last) = self.last_above
            && self.display.above(self.overlay)? == last
        {
            self.display.set_above(self.overlay, self.original_above)?;
        }
        if self
            .display
            .window_property(self.overlay, b"WM_TRANSIENT_FOR")?
            == [self.game]
        {
            if self.original_parent == 0 {
                self.display
                    .conn
                    .delete_property(self.overlay, AtomEnum::WM_TRANSIENT_FOR.into())?
                    .check()?;
            } else {
                self.display
                    .conn
                    .change_property32(
                        PropMode::REPLACE,
                        self.overlay,
                        AtomEnum::WM_TRANSIENT_FOR,
                        AtomEnum::WINDOW,
                        &[self.original_parent],
                    )?
                    .check()?;
            }
        }
        self.display.conn.flush()?;
        Ok(())
    }
}
impl Drop for Bridge {
    fn drop(&mut self) {
        if let Err(e) = self.cleanup() {
            eprintln!("HDT portable X11 cleanup: {e:#}");
        }
    }
}

fn emit(output: &mut impl Write, value: &Value) -> Result<()> {
    let mut line = serde_json::to_vec(value)?;
    line.push(b'\n');
    ensure!(
        line.len() <= 4096,
        "native response exceeds atomic pipe bound"
    );
    output.write_all(&line)?;
    output.flush()?;
    Ok(())
}
fn bridge(mut owner: Bridge) -> Result<()> {
    let stop = Arc::new(AtomicBool::new(false));
    let signals = [signal_hook::consts::SIGTERM, signal_hook::consts::SIGINT]
        .map(|signal| signal_hook::flag::register(signal, Arc::clone(&stop)))
        .into_iter()
        .collect::<std::io::Result<Vec<_>>>()?;
    let result = (|| {
        ensure!(
            std::fs::metadata("/proc/self/fd/1")?.file_type().is_fifo(),
            "portable bridge requires piped output"
        );
        let stdout = std::io::stdout();
        let flags = OFlag::from_bits_truncate(fcntl(stdout.as_fd(), FcntlArg::F_GETFL)?);
        fcntl(stdout.as_fd(), FcntlArg::F_SETFL(flags | OFlag::O_NONBLOCK))?;
        let mut output = stdout.lock();
        emit(
            &mut output,
            &json!({"kind":"ready","overlay":owner.overlay,"game":owner.game,"original":owner.original_parent}),
        )?;
        let mut input = std::fs::File::open("/proc/self/fd/0")?;
        let mut pending = Vec::new();
        let mut frame = None;
        let mut previous = None;
        while !stop.load(Ordering::Relaxed) {
            let mut fds = [PollFd::new(input.as_fd(), PollFlags::POLLIN)];
            match poll(&mut fds, 100u16) {
                Err(nix::errno::Errno::EINTR) => continue,
                result => {
                    result?;
                }
            }
            if fds[0]
                .revents()
                .is_some_and(|x| x.intersects(PollFlags::POLLIN | PollFlags::POLLHUP))
            {
                let mut chunk = [0u8; 4096];
                let count = input.read(&mut chunk)?;
                if count == 0 {
                    break;
                }
                pending.extend_from_slice(&chunk[..count]);
                ensure!(pending.len() < 65_536, "pending native input exceeds bound");
                while let Some(end) = pending.iter().position(|x| *x == b'\n') {
                    frame = Some((
                        Frame::parse(std::str::from_utf8(&pending[..end])?)?,
                        Instant::now(),
                    ));
                    pending.drain(..=end);
                }
            }
            emit(
                &mut output,
                &owner.tick(frame.as_ref(), &mut previous, None)?,
            )?;
        }
        Ok(())
    })();
    for signal in signals {
        signal_hook::low_level::unregister(signal);
    }
    result
}

#[derive(Clone, Copy, Deserialize)]
enum Win32Input {
    Unprepared,
    Prepared,
}

#[derive(Deserialize)]
struct Producer {
    session: String,
    #[serde(rename = "observedMS")]
    observed_ms: i64,
    #[serde(rename = "overlayHwnd")]
    hwnd: u32,
    #[serde(rename = "win32Input")]
    win32_input: Win32Input,
    width: u16,
    height: u16,
    rectangles: Vec<[u16; 4]>,
}
impl Producer {
    fn current(&self, session: &str, width: u16, height: u16, now: i64) -> bool {
        self.session == session
            && self.hwnd != 0
            && self.width == width
            && self.height == height
            && now.abs_diff(self.observed_ms) < 1500
    }
}

fn write_ack(path: &std::path::Path, value: &Value) -> Result<()> {
    use std::os::unix::fs::OpenOptionsExt as _;
    let temp = path.with_file_name(format!("control-{}.next", uuid::Uuid::new_v4()));
    let result = (|| {
        let mut file = std::fs::OpenOptions::new()
            .write(true)
            .create_new(true)
            .mode(0o600)
            .open(&temp)?;
        serde_json::to_writer(&mut file, value)?;
        file.flush()?;
        std::fs::rename(&temp, path)?;
        Ok(())
    })();
    if result.is_err() {
        let _ = std::fs::remove_file(&temp);
    }
    result
}

/// Explicit opt-in controller for experiments; the regular launcher never calls it.
pub fn control(cfg: &crate::config::Config, args: &[OsString]) -> Result<()> {
    let [session] = args else {
        bail!("Usage: hdt-host x11-control SESSION");
    };
    let session = session
        .to_str()
        .filter(|s| !s.is_empty() && s.len() <= 128)
        .context("invalid control session")?;
    use std::os::unix::fs::OpenOptionsExt as _;
    let directory = cfg.project_root.join("evidence");
    std::fs::create_dir_all(&directory)?;
    let controller_lock = std::fs::OpenOptions::new()
        .read(true)
        .write(true)
        .create(true)
        .truncate(false)
        .mode(0o600)
        .custom_flags(OFlag::O_NOFOLLOW.bits())
        .open(directory.join("portable-input.lock"))?;
    controller_lock
        .try_lock()
        .context("another portable controller owns this evidence directory")?;
    let ack = directory.join("control-input.json");
    let stop = Arc::new(AtomicBool::new(false));
    let signals = [signal_hook::consts::SIGTERM, signal_hook::consts::SIGINT]
        .map(|s| signal_hook::flag::register(s, Arc::clone(&stop)))
        .into_iter()
        .collect::<std::io::Result<Vec<_>>>()?;
    let mut owner: Option<Bridge> = None;
    let mut previous = None;
    let mut last_failure = String::new();
    let result = (|| {
        while !stop.load(Ordering::Relaxed) {
            let tick = (|| -> Result<Value> {
                if owner.is_none() {
                    let display = Display::open()?;
                    let clients = display.windows(&cfg.window_class)?;
                    let games: Vec<_> = clients
                        .iter()
                        .filter(|x| x.title == "Hearthstone")
                        .collect();
                    let overlays: Vec<_> = clients
                        .iter()
                        .filter(|x| x.title == "HearthstoneOverlay")
                        .collect();
                    let mains: Vec<_> = clients
                        .iter()
                        .filter(|x| x.title == "Hearthstone Deck Tracker")
                        .collect();
                    let ([game], [overlay], [main]) =
                        (games.as_slice(), overlays.as_slice(), mains.as_slice())
                    else {
                        bail!("expected one game and HDT pair");
                    };
                    ensure!(overlay.pid == main.pid, "overlay/main identity mismatch");
                    owner = Some(Bridge::new(
                        display,
                        overlay.pid,
                        game.pid,
                        cfg.window_class.clone(),
                    )?);
                    previous = None;
                }
                let bridge = owner.as_mut().context("missing native owner")?;
                let geometry = bridge.display.conn.get_geometry(bridge.overlay)?.reply()?;
                let mut bytes = Vec::new();
                let producer = match std::fs::OpenOptions::new()
                    .read(true)
                    .custom_flags((OFlag::O_NONBLOCK | OFlag::O_NOFOLLOW).bits())
                    .open(directory.join("input-region.json"))
                {
                    Ok(file) => {
                        ensure!(
                            file.metadata()?.is_file(),
                            "region snapshot is not a regular file"
                        );
                        file.take(1_048_577).read_to_end(&mut bytes)?;
                        ensure!(bytes.len() <= 1_048_576, "region snapshot exceeds bound");
                        Some(serde_json::from_slice::<Producer>(&bytes)?)
                    }
                    Err(e) if e.kind() == std::io::ErrorKind::NotFound => None,
                    Err(e) => return Err(e.into()),
                };
                let now: i64 = SystemTime::now()
                    .duration_since(UNIX_EPOCH)?
                    .as_millis()
                    .try_into()?;
                let fresh = producer
                    .as_ref()
                    .is_some_and(|p| p.current(session, geometry.width, geometry.height, now));
                let frame = Frame::new(
                    geometry.width,
                    geometry.height,
                    producer
                        .as_ref()
                        .filter(|p| fresh && !matches!(p.win32_input, Win32Input::Unprepared))
                        .map_or(&[][..], |p| p.rectangles.as_slice()),
                )?;
                let mut state = bridge.tick(
                    Some(&(frame, Instant::now())),
                    &mut previous,
                    Some(
                        producer
                            .as_ref()
                            .map_or(Win32Input::Unprepared, |p| p.win32_input),
                    ),
                )?;
                state["fresh"] = json!(fresh && state["fresh"] == true);
                state["session"] = json!(session);
                state["backend"] = json!("x11");
                state["nativeReady"] = json!(true);
                state["overlayHwnd"] = json!(producer.map_or(0, |p| p.hwnd));
                Ok(state)
            })();
            match tick {
                Ok(state) => {
                    write_ack(&ack, &state)?;
                    last_failure.clear();
                }
                Err(error) => {
                    owner = None;
                    write_ack(
                        &ack,
                        &json!({"session":session,"backend":"x11","nativeReady":false}),
                    )?;
                    let message = error.to_string();
                    if message != last_failure {
                        eprintln!("Portable X11 input pending: {message}");
                        last_failure = message;
                    }
                }
            }
            std::thread::sleep(Duration::from_millis(100));
        }
        Ok(())
    })();
    drop(owner);
    let cleanup = write_ack(
        &ack,
        &json!({"session":session,"backend":"x11","nativeReady":false}),
    );
    for signal in signals {
        signal_hook::low_level::unregister(signal);
    }
    result.and(cleanup)
}

pub fn run(args: &[OsString]) -> Result<()> {
    let args: Vec<_> = args
        .iter()
        .map(|x| x.to_str().context("X11 arguments must be UTF-8"))
        .collect::<Result<_>>()?;
    let pid = |x: &str| -> Result<u32> {
        let p = x.parse()?;
        ensure!(p > 0, "invalid PID");
        Ok(p)
    };
    match args.as_slice() {
        ["windows", class] => println!(
            "{}",
            serde_json::to_string(&Display::open()?.windows(class)?)?
        ),
        ["monitors"] => {
            let display = Display::open()?;
            let monitors = display
                .conn
                .randr_get_monitors(display.root, true)?
                .reply()?
                .monitors;
            ensure!(
                !monitors.is_empty(),
                "RandR monitor enumeration unavailable"
            );
            let values: Vec<_> = monitors.into_iter().map(|m| -> Result<_> {Ok(json!({"name":String::from_utf8(display.conn.get_atom_name(m.name)?.reply()?.name)?,
                "width":m.width,"height":m.height,"x":m.x,"y":m.y,"scale":null}))}).collect::<Result<_>>()?;
            println!("{}", serde_json::to_string(&values)?);
        }
        ["focus", class, process, xid] => {
            let process = pid(process)?;
            let window = if let Some(hex) = xid.strip_prefix("0x") {
                u32::from_str_radix(hex, 16)?
            } else {
                xid.parse()?
            };
            let display = Display::open()?;
            ensure!(
                display.matches(window, process, "Hearthstone Deck Tracker", class)?,
                "main-window identity changed"
            );
            display.request(
                window,
                b"_NET_ACTIVE_WINDOW",
                [2, x11rb::CURRENT_TIME, display.active()?, 0, 0],
            )?;
        }
        ["bridge", process, game_process, class] => {
            let process = pid(process)?;
            let game_process = pid(game_process)?;
            bridge(Bridge::new(
                Display::open()?,
                process,
                game_process,
                (*class).to_owned(),
            )?)?;
        }
        _ => bail!(
            "Usage: hdt-host x11-desktop windows CLASS|monitors|focus CLASS PID XID|bridge PID GAME_PID CLASS"
        ),
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use proptest::prelude::*;

    proptest! {
        #![proptest_config(ProptestConfig::with_cases(256))]
        #[test]
        fn arbitrary_wire_data_never_panics(bytes in proptest::collection::vec(any::<u8>(), 0..70_000)) {
            let _ = validate_input_frame(&bytes);
        }

        #[test]
        fn arbitrary_ascii_tokens_never_panic(text in "[ -~]{0,4096}") {
            let _ = validate_input_frame(text.as_bytes());
        }

        #[test]
        fn accepted_rectangles_are_inside_the_actual_window(
            width in 1u16..=u16::MAX, height in 1u16..=u16::MAX,
            x in any::<u16>(), y in any::<u16>(), w in any::<u16>(), h in any::<u16>()
        ) {
            let expected = x <= i16::MAX as u16 && y <= i16::MAX as u16 && w > 0 && h > 0 &&
                u32::from(x) + u32::from(w) <= u32::from(width) &&
                u32::from(y) + u32::from(h) <= u32::from(height);
            let wire = format!("v1 {width} {height} 1 {x} {y} {w} {h}");
            prop_assert_eq!(validate_input_frame(wire.as_bytes()).is_ok(), expected);
        }

        #[test]
        fn producer_sequences_follow_current_time_dimensions_and_session(
            operations in proptest::collection::vec((0u8..6, 0i64..4000), 1..100)
        ) {
            // Reference model tracks the producer and native geometry separately;
            // acceptance depends on the currently matching context, not a latched invalidation history.
            let mut now = 10_000i64;
            let mut width = 100u16;
            let mut generation = 0u32;
            let mut published_generation = 0u32;
            let mut published_width = 100u16;
            let mut published_time = now;
            let mut has_handle = true;
            let mut producer = Producer {session:"0".into(), observed_ms:now, win32_input:Win32Input::Unprepared,
                hwnd:1,width,height:80,rectangles:vec![]};
            for (operation, delta) in operations {
                match operation {
                    0 => { now += delta; },
                    1 => { width = if width == 100 { 101 } else { 100 }; },
                    2 => { generation += 1; },
                    3 => {
                        published_generation = generation; published_width = width;
                        published_time = now; has_handle = true;
                        producer = Producer {session:generation.to_string(),observed_ms:now,win32_input:Win32Input::Unprepared,
                            hwnd:1,width,height:80,rectangles:vec![]};
                    },
                    4 => { has_handle = false; producer.hwnd = 0; },
                    _ => { now -= delta; },
                }
                let expected = has_handle && generation == published_generation && width == published_width &&
                    now >= published_time - 1499 && now <= published_time + 1499;
                prop_assert_eq!(producer.current(&generation.to_string(), width, 80, now), expected);
            }
        }
    }
    #[test]
    fn producer_requires_matching_session_dimensions_and_recent_time() {
        let mut producer = Producer {
            session: "owned".into(),
            observed_ms: 10000,
            hwnd: 1,
            win32_input: Win32Input::Unprepared,
            width: 100,
            height: 80,
            rectangles: vec![],
        };
        assert!(producer.current("owned", 100, 80, 10000));
        assert!(!producer.current("other", 100, 80, 10000));
        assert!(!producer.current("owned", 101, 80, 10000));
        assert!(!producer.current("owned", 100, 80, 11500));
        assert!(!producer.current("owned", 100, 80, 8500));
        producer.hwnd = 0;
        assert!(!producer.current("owned", 100, 80, 10000));
    }
    #[test]
    fn frames_cannot_extend_input_outside_the_overlay() {
        assert!(Frame::parse("v1 500 400 1 100 100 80 40").is_ok());
        assert!(Frame::parse("v1 500 400 0").is_ok());
        for invalid in [
            "v2 500 400 0",
            "v1 0 400 0",
            "v1 500 400 2049",
            "v1 500 400 1 490 0 20 20",
            "v1 500 400 1 -1 0 1 1",
            "v1 500 400 1 0 0 0 1",
            "v1 500 400 0 extra",
            "v1 65535 400 1 32768 0 1 1",
        ] {
            assert!(Frame::parse(invalid).is_err(), "accepted {invalid}");
        }
    }
}
