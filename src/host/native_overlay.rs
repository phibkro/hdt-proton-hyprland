//! Read-only game references and a Rust-owned, permanently input-empty raster window.
//! The caller supplies pixels; this process never acquires cards or sends game input.
use crate::x11::{atom, identity};
use anyhow::{Context, Result, bail, ensure};
use base64::Engine;
use serde::Deserialize;
use std::{
    ffi::OsString,
    fs,
    io::{BufRead, Write},
    os::unix::fs::MetadataExt,
    sync::{Arc, Mutex},
    time::{Duration, Instant},
};
use x11rb::{
    connection::Connection,
    protocol::{
        shape::{ConnectionExt as _, SK, SO},
        xproto::{
            AtomEnum, ClipOrdering, ConfigureWindowAux, ConnectionExt as _, CreateGCAux,
            CreateWindowAux, EventMask, ImageFormat, ImageOrder, MapState, PropMode, StackMode,
            WindowClass,
        },
    },
    rust_connection::RustConnection,
    wrapper::ConnectionExt as _,
};
const WIDTH: u16 = 320;
const HEIGHT: u16 = 640;
const PIXELS: usize = WIDTH as usize * HEIGHT as usize * 4;
const LINE_BYTES: usize = 2 * 1024 * 1024;
#[derive(Deserialize, serde::Serialize)]
enum RasterFormat {
    #[serde(rename = "bgra8888Opaque")]
    Bgra8888Opaque,
}
#[derive(Clone, serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub struct GameWindow {
    window: u32,
    pid: u32,
    class: String,
    x: i16,
    y: i16,
    width: u16,
    height: u16,
}
fn owned_process(pid: u32) -> bool {
    fs::metadata(format!("/proc/{pid}"))
        .is_ok_and(|metadata| metadata.uid() == nix::unistd::getuid().as_raw())
}
fn candidates(conn: &RustConnection, root: u32) -> Result<Vec<GameWindow>> {
    let list = conn
        .get_property(
            false,
            root,
            atom(conn, b"_NET_CLIENT_LIST")?,
            AtomEnum::WINDOW,
            0,
            2048,
        )?
        .reply()?;
    ensure!(list.bytes_after == 0, "X11 client list exceeds bound");
    let windows = if list.format == 32 {
        list.value32().context("invalid X11 client list")?.collect()
    } else {
        conn.query_tree(root)?.reply()?.children
    };
    ensure!(windows.len() <= 2048, "X11 client list exceeds bound");
    let pid_atom = atom(conn, b"_NET_WM_PID")?;
    let mut result = Vec::new();
    for window in windows {
        let candidate = (|| -> Result<Option<GameWindow>> {
            let name = conn
                .get_property(false, window, AtomEnum::WM_NAME, AtomEnum::STRING, 0, 64)?
                .reply()?;
            if name.bytes_after != 0 || name.value != b"Hearthstone" {
                return Ok(None);
            }
            let pid = conn
                .get_property(false, window, pid_atom, AtomEnum::CARDINAL, 0, 1)?
                .reply()?;
            let Some(pid) = pid.value32().and_then(|mut values| values.next()) else {
                return Ok(None);
            };
            if !owned_process(pid) {
                return Ok(None);
            }
            let class = conn
                .get_property(false, window, AtomEnum::WM_CLASS, AtomEnum::STRING, 0, 64)?
                .reply()?;
            if class.bytes_after != 0 || class.format != 8 {
                return Ok(None);
            }
            let Some(class) = class.value.split(|byte| *byte == 0).nth(1) else {
                return Ok(None);
            };
            let class = std::str::from_utf8(class)?.to_owned();
            if class.is_empty()
                || class.len() > 128
                || !identity(conn, window, pid_atom, pid, "Hearthstone", &class)?
            {
                return Ok(None);
            }
            let geometry = conn.get_geometry(window)?.reply()?;
            let origin = conn.translate_coordinates(window, root, 0, 0)?.reply()?;
            Ok(Some(GameWindow {
                window,
                pid,
                class,
                x: origin.dst_x,
                y: origin.dst_y,
                width: geometry.width,
                height: geometry.height,
            }))
        })();
        if let Ok(Some(candidate)) = candidate {
            result.push(candidate);
        }
    }
    Ok(result)
}
#[derive(Deserialize)]
#[serde(
    tag = "kind",
    rename_all = "camelCase",
    rename_all_fields = "camelCase",
    deny_unknown_fields
)]
enum Packet {
    Hide,
    Notice {
        schema_version: u8,
        renderer_session: String,
        sequence: u32,
        rendered_ms: i64,
        width: u16,
        height: u16,
        pixel_format: RasterFormat,
        pixels: String,
    },
    Frame {
        schema_version: u8,
        source_session: String,
        sequence: u32,
        observed_ms: i64,
        width: u16,
        height: u16,
        pixel_format: RasterFormat,
        pixels: String,
    },
}
struct Frame {
    session: String,
    sequence: u32,
    observed_ms: i64,
    pixels: Vec<u8>,
}
enum Directive {
    Hide,
    Frame(Frame),
    Notice(Frame),
}
fn decode(bytes: &[u8]) -> Result<Directive> {
    let packet: Packet = serde_json::from_slice(bytes).context("invalid raster protocol")?;
    match packet {
        Packet::Hide => Ok(Directive::Hide),
        Packet::Notice {
            schema_version,
            renderer_session,
            sequence,
            rendered_ms,
            width,
            height,
            pixel_format,
            pixels,
        } => {
            let frame = serde_json::json!({"kind":"frame","schemaVersion":schema_version,"sourceSession":renderer_session,"sequence":sequence,"observedMs":rendered_ms,"width":width,"height":height,"pixelFormat":pixel_format,"pixels":pixels});
            match decode(&serde_json::to_vec(&frame)?)? {
                Directive::Frame(value) => Ok(Directive::Notice(value)),
                _ => bail!("invalid notice raster"),
            }
        }
        Packet::Frame {
            schema_version,
            source_session,
            sequence,
            observed_ms,
            width,
            height,
            pixel_format,
            pixels,
        } => {
            let RasterFormat::Bgra8888Opaque = pixel_format;
            ensure!(
                schema_version == 1 && sequence > 0 && width == WIDTH && height == HEIGHT,
                "unsupported raster identity, dimensions or format"
            );
            uuid::Uuid::parse_str(&source_session).context("invalid raster session")?;
            ensure!(
                pixels.len() <= PIXELS.div_ceil(3) * 4,
                "raster base64 exceeds bound"
            );
            let pixels = base64::engine::general_purpose::STANDARD
                .decode(pixels)
                .context("invalid raster encoding")?;
            ensure!(
                pixels.len() == PIXELS
                    && pixels
                        .as_chunks::<4>()
                        .0
                        .iter()
                        .all(|pixel| pixel[3] == 255),
                "raster must be complete opaque BGRA pixels"
            );
            Ok(Directive::Frame(Frame {
                session: source_session,
                sequence,
                observed_ms,
                pixels,
            }))
        }
    }
}
fn read_packet(reader: &mut impl BufRead) -> Result<Option<Directive>> {
    let mut line = Vec::new();
    let count =
        std::io::Read::take(reader, (LINE_BYTES + 1) as u64).read_until(b'\n', &mut line)?;
    if count == 0 {
        return Ok(None);
    }
    ensure!(
        count <= LINE_BYTES && line.last() == Some(&b'\n'),
        "raster line is incomplete or oversized"
    );
    decode(&line).map(Some)
}
#[derive(Default)]
struct Input {
    pending: Option<Directive>,
    closed: bool,
    failed: bool,
}
struct Surface<'a> {
    conn: &'a RustConnection,
    window: u32,
    pixmap: u32,
    gc: u32,
}
impl<'a> Surface<'a> {
    fn new(
        conn: &'a RustConnection,
        root: &x11rb::protocol::xproto::Screen,
        game: u32,
    ) -> Result<Self> {
        let visual = root
            .allowed_depths
            .iter()
            .flat_map(|depth| &depth.visuals)
            .find(|visual| visual.visual_id == root.root_visual)
            .context("missing root visual")?;
        ensure!(
            root.root_depth == 24
                && visual.red_mask == 0x00ff0000
                && visual.green_mask == 0x0000ff00
                && visual.blue_mask == 0x000000ff
                && conn.setup().image_byte_order == ImageOrder::LSB_FIRST
                && conn
                    .setup()
                    .pixmap_formats
                    .iter()
                    .any(|format| format.depth == 24
                        && format.bits_per_pixel == 32
                        && format.scanline_pad == 32),
            "native raster preview requires little-endian 24-bit RGB/32-bit pixels"
        );
        let window = conn.generate_id()?;
        conn.create_window(
            root.root_depth,
            window,
            root.root,
            0,
            0,
            WIDTH,
            HEIGHT,
            0,
            WindowClass::INPUT_OUTPUT,
            root.root_visual,
            &CreateWindowAux::new()
                .override_redirect(1)
                .event_mask(EventMask::EXPOSURE | EventMask::STRUCTURE_NOTIFY),
        )?
        .check()?;
        // No mapping API is called before input shape has been emptied and read back.
        conn.shape_rectangles(
            SO::SET,
            SK::INPUT,
            ClipOrdering::UNSORTED,
            window,
            0,
            0,
            &[],
        )?
        .check()?;
        ensure!(
            conn.shape_get_rectangles(window, SK::INPUT)?
                .reply()?
                .rectangles
                .is_empty(),
            "input-empty preparation readback failed"
        );
        conn.change_property8(
            PropMode::REPLACE,
            window,
            AtomEnum::WM_NAME,
            AtomEnum::STRING,
            b"HDT native tracking overlay",
        )?
        .check()?;
        conn.change_property8(
            PropMode::REPLACE,
            window,
            AtomEnum::WM_CLASS,
            AtomEnum::STRING,
            b"hdt-native-overlay\0hdt-native-overlay\0",
        )?
        .check()?;
        conn.change_property32(
            PropMode::REPLACE,
            window,
            atom(conn, b"_NET_WM_PID")?,
            AtomEnum::CARDINAL,
            &[std::process::id()],
        )?
        .check()?;
        conn.change_property32(
            PropMode::REPLACE,
            window,
            AtomEnum::WM_TRANSIENT_FOR,
            AtomEnum::WINDOW,
            &[game],
        )?
        .check()?;
        conn.change_property32(
            PropMode::REPLACE,
            window,
            AtomEnum::WM_HINTS,
            AtomEnum::WM_HINTS,
            &[1, 0, 0, 0, 0, 0, 0, 0, 0],
        )?
        .check()?;
        let pixmap = conn.generate_id()?;
        conn.create_pixmap(root.root_depth, pixmap, root.root, WIDTH, HEIGHT)?
            .check()?;
        let gc = conn.generate_id()?;
        conn.create_gc(gc, pixmap, &CreateGCAux::new().graphics_exposures(0))?
            .check()?;
        Ok(Self {
            conn,
            window,
            pixmap,
            gc,
        })
    }
    fn upload(&self, pixels: &[u8]) -> Result<()> {
        let stride = usize::from(WIDTH) * 4;
        let rows = (usize::from(self.conn.setup().maximum_request_length) * 4).saturating_sub(128)
            / stride;
        ensure!(rows > 0, "X11 image request budget is too small");
        for (index, chunk) in pixels.chunks(rows * stride).enumerate() {
            self.conn
                .put_image(
                    ImageFormat::Z_PIXMAP,
                    self.pixmap,
                    self.gc,
                    WIDTH,
                    (chunk.len() / stride).try_into()?,
                    0,
                    (index * rows).try_into()?,
                    0,
                    24,
                    chunk,
                )?
                .check()?;
        }
        self.paint()
    }
    fn paint(&self) -> Result<()> {
        self.conn
            .copy_area(self.pixmap, self.window, self.gc, 0, 0, 0, 0, WIDTH, HEIGHT)?
            .check()?;
        Ok(())
    }
}
fn announce(value: serde_json::Value) -> Result<()> {
    println!("{}", serde_json::to_string(&value)?);
    std::io::stdout().flush()?;
    Ok(())
}
fn run_window(game: u32, pid: u32, class: &str) -> Result<()> {
    ensure!(
        game > 0 && pid > 0 && !class.is_empty() && class.len() <= 128 && owned_process(pid),
        "explicit owned game window identity is required"
    );
    let parent = nix::unistd::getppid();
    nix::sys::prctl::set_pdeathsig(Some(nix::sys::signal::Signal::SIGKILL))?;
    ensure!(
        nix::unistd::getppid() == parent,
        "overlay parent exited during preparation"
    );
    let (conn, screen) = x11rb::connect(None).context("cannot open X11 display")?;
    let root = &conn.setup().roots[screen];
    let pid_atom = atom(&conn, b"_NET_WM_PID")?;
    ensure!(
        identity(&conn, game, pid_atom, pid, "Hearthstone", class)?,
        "selected game window identity changed"
    );
    let surface = Surface::new(&conn, root, game)?;
    announce(
        serde_json::json!({"kind":"ready","schemaVersion":1,"window":surface.window,"pid":std::process::id(),"width":WIDTH,"height":HEIGHT,"pixelFormat":RasterFormat::Bgra8888Opaque,"inputRectangles":0}),
    )?;
    let input = Arc::new(Mutex::new(Input::default()));
    let producer = input.clone();
    std::thread::spawn(move || {
        let mut reader = std::io::stdin().lock();
        loop {
            let next = read_packet(&mut reader);
            let Ok(mut input) = producer.lock() else {
                return;
            };
            match next {
                Ok(Some(value)) => input.pending = Some(value),
                Ok(None) => {
                    input.closed = true;
                    return;
                }
                Err(_) => {
                    input.failed = true;
                    return;
                }
            }
        }
    });
    let active_atom = atom(&conn, b"_NET_ACTIVE_WINDOW")?;
    let mut last: Option<(String, u32, i64, Instant)> = None;
    let mut high_water: Option<(String, u32, i64)> = None;
    let mut visible = false;
    let mut notice_high_water: Option<(String, u32, i64)> = None;
    let mut geometry = None;
    let mut next_status = Instant::now();
    loop {
        let directive = {
            let mut input = input
                .lock()
                .map_err(|_| anyhow::anyhow!("overlay input owner failed"))?;
            ensure!(!input.failed, "invalid bounded raster packet");
            if input.closed {
                break;
            }
            input.pending.take()
        };
        if let Some(directive) = directive {
            match directive {
                Directive::Hide => last = None,
                Directive::Notice(frame) => {
                    let backwards =
                        notice_high_water
                            .as_ref()
                            .is_some_and(|(session, sequence, time)| {
                                session == &frame.session
                                    && (frame.sequence < *sequence || frame.observed_ms < *time)
                            });
                    if backwards
                        || chrono::Utc::now()
                            .timestamp_millis()
                            .abs_diff(frame.observed_ms)
                            > 5000
                    {
                        last = None;
                    } else {
                        notice_high_water =
                            Some((frame.session.clone(), frame.sequence, frame.observed_ms));
                        surface.upload(&frame.pixels)?;
                        last = Some((
                            frame.session,
                            frame.sequence,
                            frame.observed_ms,
                            Instant::now(),
                        ));
                    }
                }
                Directive::Frame(frame) => {
                    let backwards = high_water
                        .as_ref()
                        .is_some_and(|(session, sequence, time)| {
                            session == &frame.session
                                && (frame.sequence < *sequence || frame.observed_ms < *time)
                        });
                    if backwards
                        || chrono::Utc::now()
                            .timestamp_millis()
                            .abs_diff(frame.observed_ms)
                            > 5000
                    {
                        last = None;
                    } else {
                        high_water =
                            Some((frame.session.clone(), frame.sequence, frame.observed_ms));
                        surface.upload(&frame.pixels)?;
                        last = Some((
                            frame.session,
                            frame.sequence,
                            frame.observed_ms,
                            Instant::now(),
                        ));
                    }
                }
            }
        }
        ensure!(
            identity(&conn, game, pid_atom, pid, "Hearthstone", class)?,
            "selected game window identity changed"
        );
        let bounds = conn.get_geometry(game)?.reply()?;
        let origin = conn.translate_coordinates(game, root.root, 0, 0)?.reply()?;
        let width = WIDTH.min(bounds.width);
        let height = HEIGHT.min(bounds.height);
        let position = (
            i16::try_from(i32::from(origin.dst_x) + i32::from(bounds.width - width))?,
            origin.dst_y,
            width,
            height,
        );
        if geometry != Some(position) {
            conn.configure_window(
                surface.window,
                &ConfigureWindowAux::new()
                    .x(i32::from(position.0))
                    .y(i32::from(position.1))
                    .width(u32::from(width))
                    .height(u32::from(height)),
            )?
            .check()?;
            ensure!(
                conn.shape_get_rectangles(surface.window, SK::INPUT)?
                    .reply()?
                    .rectangles
                    .is_empty(),
                "owned raster input shape changed"
            );
            geometry = Some(position);
            if last.is_some() {
                surface.paint()?;
            }
        }
        let active = conn
            .get_property(false, root.root, active_atom, AtomEnum::WINDOW, 0, 1)?
            .reply()?;
        let active = active.bytes_after == 0
            && active.value32().and_then(|mut values| values.next()) == Some(game);
        let fresh = last.as_ref().is_some_and(|(_, _, time, received)| {
            received.elapsed() <= Duration::from_secs(5)
                && chrono::Utc::now().timestamp_millis().abs_diff(*time) <= 5000
        });
        let mapped = conn.get_window_attributes(game)?.reply()?.map_state == MapState::VIEWABLE;
        let should_show =
            fresh && active && mapped && bounds.width >= WIDTH && bounds.height >= 200;
        if should_show != visible {
            if should_show {
                conn.map_window(surface.window)?.check()?;
                conn.configure_window(
                    surface.window,
                    &ConfigureWindowAux::new().stack_mode(StackMode::ABOVE),
                )?
                .check()?;
                surface.paint()?;
            } else {
                conn.unmap_window(surface.window)?.check()?;
            }
            visible = should_show;
        }
        for _ in 0..64 {
            let Some(event) = conn.poll_for_event()? else {
                break;
            };
            if matches!(event, x11rb::protocol::Event::Expose(_)) && last.is_some() {
                surface.paint()?;
            }
        }
        if Instant::now() >= next_status {
            let input_rectangles = conn
                .shape_get_rectangles(surface.window, SK::INPUT)?
                .reply()?
                .rectangles
                .len();
            ensure!(input_rectangles == 0, "owned raster input shape changed");
            announce(
                serde_json::json!({"kind":"status","schemaVersion":1,"window":surface.window,"visible":visible,"inputRectangles":input_rectangles}),
            )?;
            next_status = Instant::now() + Duration::from_secs(1);
        }
        conn.flush()?;
        std::thread::sleep(Duration::from_millis(50));
    }
    conn.destroy_window(surface.window)?.check()?;
    conn.flush()?;
    Ok(())
}
pub fn run(args: &[OsString]) -> Result<()> {
    let values: Vec<_> = args
        .iter()
        .map(|arg| {
            arg.to_str()
                .context("native overlay arguments must be UTF-8")
        })
        .collect::<Result<_>>()?;
    match values.as_slice() {
        ["info"] => announce(
            serde_json::json!({"schemaVersion":1,"width":WIDTH,"height":HEIGHT,"pixelFormat":RasterFormat::Bgra8888Opaque}),
        ),
        ["list"] => {
            let (conn, screen) = x11rb::connect(None)?;
            announce(serde_json::to_value(candidates(
                &conn,
                conn.setup().roots[screen].root,
            )?)?)
        }
        ["run", game, pid, class] => run_window(
            game.parse().context("invalid game XID")?,
            pid.parse().context("invalid game PID")?,
            class,
        ),
        _ => bail!("Usage: hdt-host native-overlay info|list|run GAME_XID GAME_PID CLASS"),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    fn packet() -> serde_json::Value {
        let mut pixels = vec![0_u8; PIXELS];
        for pixel in pixels.as_chunks_mut::<4>().0 {
            pixel[3] = 255;
        }
        serde_json::json!({"kind":"frame","schemaVersion":1,"sourceSession":"03ff2759-fbe7-4d19-9fe4-a1d156974a1f","sequence":1,"observedMs":0,
            "width":WIDTH,"height":HEIGHT,"pixelFormat":RasterFormat::Bgra8888Opaque,"pixels":base64::engine::general_purpose::STANDARD.encode(pixels)})
    }
    #[test]
    fn raster_boundary_requires_exact_opaque_bytes_and_explicit_supported_contract() {
        let valid = packet();
        assert!(matches!(
            decode(&serde_json::to_vec(&valid).unwrap()).unwrap(),
            Directive::Frame(_)
        ));
        for (field, invalid) in [
            ("schemaVersion", serde_json::json!(2)),
            ("width", serde_json::json!(0)),
            ("height", serde_json::json!(65535)),
            ("sequence", serde_json::json!(0)),
            ("sourceSession", serde_json::json!("not-a-source-id")),
            ("pixelFormat", serde_json::json!("rgba")),
            ("pixels", serde_json::json!("AA==")),
        ] {
            let mut value = valid.clone();
            value[field] = invalid;
            assert!(
                decode(&serde_json::to_vec(&value).unwrap()).is_err(),
                "accepted invalid field {field}"
            );
        }
        let mut transparent = valid.clone();
        transparent["pixels"] =
            serde_json::json!(base64::engine::general_purpose::STANDARD.encode(vec![0; PIXELS]));
        assert!(decode(&serde_json::to_vec(&transparent).unwrap()).is_err());
        let mut extra = valid;
        extra["targetWindow"] = serde_json::json!(123);
        assert!(
            decode(&serde_json::to_vec(&extra).unwrap()).is_err(),
            "pixels cannot choose a mutation target"
        );
        assert!(read_packet(&mut std::io::Cursor::new(vec![b'x'; LINE_BYTES + 1])).is_err());
        assert!(
            read_packet(&mut std::io::Cursor::new(b"{\"kind\":\"hide\"}")).is_err(),
            "partial lines cannot become commands"
        );
    }
}
