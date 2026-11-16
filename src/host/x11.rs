//! Scoped X11 operations: only HDT properties are writable; game windows are references.
use std::ffi::OsString;

use anyhow::{Context, Result, bail, ensure};
use x11rb::{
    connection::Connection,
    errors::ReplyError,
    protocol::{
        ErrorKind,
        composite::ConnectionExt as _,
        render::{ConnectionExt as _, PictType},
        xproto::{
            AtomEnum, ConnectionExt as _, ImageFormat, ImageOrder, MapState, PropMode, Window,
        },
    },
    rust_connection::RustConnection,
    wrapper::ConnectionExt as _,
};

fn text(arg: &OsString) -> Result<&str> {
    arg.to_str().context("argument is not UTF-8")
}

fn unsigned(value: &str, automatic_base: bool) -> Result<u32> {
    let value = value.trim_start_matches(char::is_whitespace);
    ensure!(!value.starts_with('-'), "invalid argument");
    let value = value.strip_prefix('+').unwrap_or(value);
    let parsed = if automatic_base {
        if let Some(hex) = value
            .strip_prefix("0x")
            .or_else(|| value.strip_prefix("0X"))
        {
            u32::from_str_radix(hex, 16)
        } else if value.starts_with('0') && value.len() > 1 {
            u32::from_str_radix(&value[1..], 8)
        } else {
            value.parse()
        }
    } else {
        value.parse()
    };
    parsed.context("invalid argument")
}

fn atom(conn: &RustConnection, name: &[u8]) -> Result<u32> {
    Ok(conn.intern_atom(false, name)?.reply()?.atom)
}

fn property(
    conn: &RustConnection,
    window: Window,
    name: u32,
    kind: AtomEnum,
) -> Result<x11rb::protocol::xproto::GetPropertyReply> {
    Ok(conn
        .get_property(false, window, name, kind, 0, u32::MAX)?
        .reply()?)
}

fn owner(conn: &RustConnection, window: Window, pid_atom: u32, pid: u32) -> Result<bool> {
    let value = property(conn, window, pid_atom, AtomEnum::CARDINAL)?;
    Ok(value.type_ == u32::from(AtomEnum::CARDINAL)
        && value.format == 32
        && value.value_len == 1
        && value.value32().and_then(|mut values| values.next()) == Some(pid))
}

fn identity(
    conn: &RustConnection,
    window: Window,
    pid_atom: u32,
    pid: u32,
    title: &str,
    class: &str,
) -> Result<bool> {
    let name = property(conn, window, AtomEnum::WM_NAME.into(), AtomEnum::STRING)?;
    if name.format != 8 || name.value != title.as_bytes() {
        return Ok(false);
    }
    let classes = property(conn, window, AtomEnum::WM_CLASS.into(), AtomEnum::STRING)?;
    let mut parts = classes.value.split(|byte| *byte == 0);
    let _instance = parts.next();
    if classes.format != 8 || parts.next() != Some(class.as_bytes()) {
        return Ok(false);
    }
    owner(conn, window, pid_atom, pid)
}

fn vanished(error: &anyhow::Error) -> bool {
    matches!(error.downcast_ref::<ReplyError>(), Some(ReplyError::X11Error(error)) if error.error_kind == ErrorKind::Window)
}

fn find(
    conn: &RustConnection,
    root: Window,
    pid_atom: u32,
    pid: u32,
    title: &str,
    class: &str,
) -> Result<Window> {
    let mut found = None;
    for window in conn.query_tree(root)?.reply()?.children {
        match identity(conn, window, pid_atom, pid, title, class) {
            Ok(true) => {
                ensure!(found.is_none(), "ambiguous window identity");
                found = Some(window);
            }
            Ok(false) => {}
            Err(error) if vanished(&error) => {}
            Err(error) => return Err(error),
        }
    }
    found.context("expected window not found")
}

fn transient(conn: &RustConnection, window: Window) -> Result<Option<Window>> {
    let value = property(
        conn,
        window,
        AtomEnum::WM_TRANSIENT_FOR.into(),
        AtomEnum::WINDOW,
    )?;
    Ok(
        (value.type_ == u32::from(AtomEnum::WINDOW) && value.format == 32 && value.value_len == 1)
            .then(|| value.value32().and_then(|mut values| values.next()))
            .flatten(),
    )
}

/// Apply or guardedly restore an HDT overlay's transient relationship.
pub fn overlay_link(args: &[OsString]) -> Result<()> {
    ensure!(
        args.len() == 4 || args.len() == 6,
        "expected apply PID GAME_PID CLASS, or restore PID CLASS XID GAME_XID ORIGINAL"
    );
    let operation = text(&args[0])?;
    let pid = unsigned(text(&args[1])?, true)?;
    ensure!(pid != 0, "missing overlay PID");
    let (conn, screen) = x11rb::connect(None).context("cannot open X display")?;
    let pid_atom = atom(&conn, b"_NET_WM_PID")?;
    match (operation, args.len()) {
        ("apply", 4) => {
            let class = text(&args[3])?;
            let root = conn.setup().roots[screen].root;
            let overlay = find(&conn, root, pid_atom, pid, "HearthstoneOverlay", class)?;
            let game = find(
                &conn,
                root,
                pid_atom,
                unsigned(text(&args[2])?, true)?,
                "Hearthstone",
                class,
            )?;
            let original = transient(&conn, overlay)?.unwrap_or(0);
            conn.change_property32(
                PropMode::REPLACE,
                overlay,
                AtomEnum::WM_TRANSIENT_FOR,
                AtomEnum::WINDOW,
                &[game],
            )?
            .check()?;
            ensure!(
                transient(&conn, overlay)? == Some(game),
                "transient relationship readback failed"
            );
            println!(
                "{}",
                serde_json::json!({"overlay": overlay, "game": game, "original": original})
            );
        }
        ("restore", 6) => {
            let overlay = unsigned(text(&args[3])?, true)?;
            match identity(
                &conn,
                overlay,
                pid_atom,
                pid,
                "HearthstoneOverlay",
                text(&args[2])?,
            ) {
                Ok(false) => return Ok(()),
                Err(error) if vanished(&error) => return Ok(()),
                other => {
                    other?;
                }
            }
            let game = unsigned(text(&args[4])?, true)?;
            let original = unsigned(text(&args[5])?, true)?;
            ensure!(
                transient(&conn, overlay)? == Some(game),
                "overlay relationship changed; refusing to overwrite"
            );
            if original == 0 {
                conn.delete_property(overlay, AtomEnum::WM_TRANSIENT_FOR.into())?
                    .check()?;
            } else {
                conn.change_property32(
                    PropMode::REPLACE,
                    overlay,
                    AtomEnum::WM_TRANSIENT_FOR,
                    AtomEnum::WINDOW,
                    &[original],
                )?
                .check()?;
            }
            ensure!(
                transient(&conn, overlay)? == (original != 0).then_some(original),
                "restore readback failed"
            );
        }
        _ => bail!("invalid operation"),
    }
    Ok(())
}

struct Region {
    window: u32,
    pid: u32,
    x: i32,
    y: i32,
    width: u16,
    height: u16,
}
impl Region {
    fn parse(args: &[OsString]) -> Result<Self> {
        ensure!(
            args.len() == 6,
            "expected XID, PID, screen x, y, width, height"
        );
        let window = unsigned(text(&args[0])?, true)?;
        let pid = unsigned(text(&args[1])?, false)?;
        let x = text(&args[2])?
            .trim_start()
            .parse()
            .context("invalid argument")?;
        let y = text(&args[3])?
            .trim_start()
            .parse()
            .context("invalid argument")?;
        let width = unsigned(text(&args[4])?, false)?;
        let height = unsigned(text(&args[5])?, false)?;
        ensure!(
            window > 0 && pid > 0 && (1..=8192).contains(&width) && (1..=8192).contains(&height),
            "invalid argument"
        );
        Ok(Self {
            window,
            pid,
            x,
            y,
            width: width as u16,
            height: height as u16,
        })
    }
}

fn clip(
    region: &Region,
    root_x: i16,
    root_y: i16,
    width: u16,
    height: u16,
) -> Result<(i16, i16, u16, u16)> {
    let left = i64::from(region.x) - i64::from(root_x);
    let top = i64::from(region.y) - i64::from(root_y);
    let right = (left + i64::from(region.width)).min(i64::from(width));
    let bottom = (top + i64::from(region.height)).min(i64::from(height));
    let left = left.max(0);
    let top = top.max(0);
    ensure!(
        right > left && bottom > top,
        "preview does not intersect overlay"
    );
    Ok((
        i16::try_from(left).context("X11 coordinate exceeds protocol range")?,
        i16::try_from(top).context("X11 coordinate exceeds protocol range")?,
        u16::try_from(right - left)?,
        u16::try_from(bottom - top)?,
    ))
}

/// Read only the named overlay backing pixmap. Never redirects or captures the game/root.
pub fn surface_probe(args: &[OsString]) -> Result<()> {
    let region = Region::parse(args)?;
    let (conn, _) = x11rb::connect(None).context("cannot open display")?;
    let title = property(
        &conn,
        region.window,
        AtomEnum::WM_NAME.into(),
        AtomEnum::STRING,
    )?;
    ensure!(
        title.format == 8 && title.value == b"HearthstoneOverlay",
        "target is not HDT overlay"
    );
    ensure!(
        owner(
            &conn,
            region.window,
            atom(&conn, b"_NET_WM_PID")?,
            region.pid
        )?,
        "overlay owner mismatch"
    );
    let attrs = conn.get_window_attributes(region.window)?.reply()?;
    ensure!(
        attrs.map_state == MapState::VIEWABLE,
        "overlay not viewable"
    );
    let geometry = conn.get_geometry(region.window)?.reply()?;
    let formats = conn.render_query_pict_formats()?.reply()?;
    let format_id = formats
        .screens
        .iter()
        .flat_map(|screen| &screen.depths)
        .flat_map(|depth| &depth.visuals)
        .find(|visual| visual.visual == attrs.visual)
        .context("overlay visual unavailable")?
        .format;
    let picture = formats
        .formats
        .iter()
        .find(|format| format.id == format_id)
        .context("overlay format unavailable")?;
    ensure!(
        picture.type_ == PictType::DIRECT && picture.direct.alpha_mask != 0,
        "overlay has no direct alpha channel"
    );
    let origin = conn
        .translate_coordinates(region.window, geometry.root, 0, 0)?
        .reply()?;
    ensure!(origin.same_screen, "cannot translate bounds");
    let (x, y, width, height) = clip(
        &region,
        origin.dst_x,
        origin.dst_y,
        geometry.width,
        geometry.height,
    )?;
    let version = conn.composite_query_version(0, 2)?.reply()?;
    ensure!(
        version.major_version > 0 || version.minor_version >= 2,
        "Composite unavailable"
    );
    let pixmap = conn.generate_id()?;
    conn.composite_name_window_pixmap(region.window, pixmap)?
        .check()?;
    let result = (|| -> Result<()> {
        let image = conn
            .get_image(ImageFormat::Z_PIXMAP, pixmap, x, y, width, height, u32::MAX)?
            .reply()?;
        let format = conn
            .setup()
            .pixmap_formats
            .iter()
            .find(|format| format.depth == image.depth)
            .context("unknown image format")?;
        let bits = usize::from(format.bits_per_pixel);
        ensure!(
            [8, 16, 24, 32].contains(&bits),
            "unsupported image pixel width"
        );
        let pad = usize::from(format.scanline_pad);
        ensure!(pad > 0 && pad % 8 == 0, "invalid image scanline padding");
        let stride = (usize::from(width) * bits).div_ceil(pad) * (pad / 8);
        ensure!(
            image.data.len() >= stride * usize::from(height),
            "truncated backing image"
        );
        let mut counts = [0_u64; 4];
        let mut hash = 14_695_981_039_346_656_037_u64;
        for row in image.data.chunks_exact(stride).take(usize::from(height)) {
            for bytes in row.chunks_exact(bits / 8).take(usize::from(width)) {
                let pixel = if conn.setup().image_byte_order == ImageOrder::LSB_FIRST {
                    bytes
                        .iter()
                        .enumerate()
                        .fold(0_u32, |value, (index, byte)| {
                            value | (u32::from(*byte) << (index * 8))
                        })
                } else {
                    bytes
                        .iter()
                        .fold(0_u32, |value, byte| (value << 8) | u32::from(*byte))
                };
                let alpha =
                    (pixel >> picture.direct.alpha_shift) & u32::from(picture.direct.alpha_mask);
                counts[if alpha == 0 {
                    0
                } else if alpha == u32::from(picture.direct.alpha_mask) {
                    1
                } else {
                    2
                }] += 1;
                counts[3] += u64::from(alpha == 1);
                hash = (hash ^ u64::from(pixel)).wrapping_mul(1_099_511_628_211);
            }
        }
        println!(
            "{}",
            serde_json::json!({"width":width,"height":height,"transparentPixels":counts[0],"opaquePixels":counts[1],"partialAlphaPixels":counts[2],"oneAlphaPixels":counts[3],"pixelHash":format!("{hash:016x}")})
        );
        Ok(())
    })();
    let freed = conn.free_pixmap(pixmap)?.check();
    result?;
    freed?;
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    fn args(values: &[&str]) -> Vec<OsString> {
        values.iter().map(OsString::from).collect()
    }
    #[test]
    fn strict_numeric_bounds() {
        let valid = ["1", "1", "0", "0", "32", "32"];
        for (index, value) in [
            (0, ""),
            (0, "0"),
            (0, "-1"),
            (0, "4294967296"),
            (1, "0"),
            (1, "99999999999999999999999999999999999"),
            (2, "2147483648"),
            (2, "-2147483649"),
            (2, "oops"),
            (3, "2147483648"),
            (4, "0"),
            (4, "8193"),
            (5, "-1"),
        ] {
            let mut input = valid;
            input[index] = value;
            assert!(Region::parse(&args(&input)).is_err(), "{index}: {value}");
        }
        for value in ["-2147483648", "2147483647"] {
            let mut input = valid;
            input[2] = value;
            let parsed = Region::parse(&args(&input)).unwrap();
            assert!(clip(&parsed, 1, 0, 100, 100).is_err());
        }
        assert_eq!(unsigned("0xff", true).unwrap(), 255);
        assert_eq!(unsigned("077", true).unwrap(), 63);
        assert!(unsigned("1 ", true).is_err());
    }
    #[test]
    fn clipping_uses_wide_arithmetic() {
        let region = Region::parse(&args(&["1", "1", "-5", "8", "32", "32"])).unwrap();
        assert_eq!(clip(&region, 0, 0, 20, 20).unwrap(), (0, 8, 20, 12));
    }
}
