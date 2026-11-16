//! Independent read-only device-lifecycle capture. Never subscribes to user input.
use anyhow::{Context, Result, bail, ensure};
use serde_json::{Value, json};
use std::{
    ffi::OsString,
    fs::OpenOptions,
    io::Write,
    os::unix::fs::OpenOptionsExt,
    path::PathBuf,
    time::{Duration, Instant},
};
use x11rb::{
    connection::Connection,
    protocol::{
        Event,
        xinput::{ConnectionExt as _, EventMask, XIEventMask},
    },
};

const LIMIT: u64 = 32 * 1024 * 1024;
const ALL_DEVICES: u16 = 0; // XIAllDevices, defined by the XInput2 protocol.

fn options(args: &[OsString]) -> Result<(PathBuf, u64)> {
    let [output, path, seconds, duration] = args else {
        bail!("Usage: hdt-host xinput-watch --output NEW_FILE --seconds 1..86400");
    };
    ensure!(
        output == "--output" && seconds == "--seconds",
        "Expected explicit --output and --seconds"
    );
    let seconds: u64 = duration
        .to_str()
        .context("Duration must be UTF-8")?
        .parse()?;
    ensure!(
        (1..=86400).contains(&seconds),
        "Duration must be 1..86400 seconds"
    );
    ensure!(!path.is_empty(), "Output path must not be empty");
    Ok((PathBuf::from(path), seconds))
}

struct Capture {
    file: std::fs::File,
    started: Instant,
    bytes: u64,
}
impl Capture {
    fn record(&mut self, kind: &str, data: Value) -> Result<()> {
        let mut row = serde_json::to_vec(&json!({
            "schema": 1, "utc": chrono::Utc::now().to_rfc3339(),
            "elapsedMs": self.started.elapsed().as_millis(), "kind": kind, "data": data
        }))?;
        row.push(b'\n');
        ensure!(
            self.bytes + row.len() as u64 <= LIMIT,
            "XInput capture reached its 32 MiB limit"
        );
        self.file.write_all(&row)?;
        self.file.flush()?;
        self.bytes += row.len() as u64;
        Ok(())
    }
}

pub fn run(args: &[OsString]) -> Result<()> {
    let (output, seconds) = options(args)?;
    // create_new also rejects existing symlinks; never truncate a previous capture.
    let file = OpenOptions::new()
        .write(true)
        .create_new(true)
        .mode(0o600)
        .open(&output)?;
    let mut capture = Capture {
        file,
        started: Instant::now(),
        bytes: 0,
    };
    let result = (|| -> Result<()> {
        let (connection, screen) = x11rb::connect(None)?;
        let version = connection.xinput_xi_query_version(2, 0)?.reply()?;
        ensure!(version.major_version >= 2, "XInput2 is required");
        connection
            .xinput_xi_select_events(
                connection.setup().roots[screen].root,
                &[EventMask {
                    deviceid: ALL_DEVICES,
                    mask: vec![XIEventMask::HIERARCHY | XIEventMask::DEVICE_CHANGED],
                }],
            )?
            .check()?;
        connection.flush()?;
        let devices = connection.xinput_xi_query_device(ALL_DEVICES)?.reply()?;
        capture.record("capture.start", json!({"seconds": seconds, "limitBytes": LIMIT,
            "devices": devices.infos.iter().map(|d| json!({"id": d.deviceid,
                "attachment": d.attachment, "type": u16::from(d.type_), "enabled": d.enabled})).collect::<Vec<_>>() }))?;
        while capture.started.elapsed() < Duration::from_secs(seconds) {
            // One event per turn keeps the deadline enforceable even during an event flood.
            match connection.poll_for_event()? {
                Some(Event::XinputHierarchy(event)) => capture.record(
                    "input.hierarchy",
                    json!({
                    "serverTime": event.time, "flags": u32::from(event.flags),
                    "devices": event.infos.iter().map(|d| json!({"id": d.deviceid,
                        "attachment": d.attachment, "type": u16::from(d.type_),
                        "enabled": d.enabled, "flags": u32::from(d.flags)})).collect::<Vec<_>>() }),
                )?,
                Some(Event::XinputDeviceChanged(event)) => capture.record(
                    "input.deviceChanged",
                    json!({
                    "serverTime": event.time, "device": event.deviceid, "source": event.sourceid,
                    "reason": u8::from(event.reason), "classCount": event.classes.len() }),
                )?,
                Some(Event::Error(error)) => bail!("XInput observer protocol error: {error:?}"),
                Some(_) => (),
                None => std::thread::sleep(Duration::from_millis(20)),
            }
        }
        capture.record("capture.stop", json!({"reason": "deadline"}))
    })();
    if let Err(error) = &result {
        // Preserve the first failure even if recording the diagnostic also fails.
        let _ = capture.record("capture.failed", json!({"error": format!("{error:#}")}));
    }
    result
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn capture_requires_explicit_bounded_duration() {
        for args in [
            vec![],
            vec!["--output", "x", "--seconds", "0"],
            vec!["--output", "x", "--seconds", "86401"],
            vec!["--output", "", "--seconds", "5"],
        ] {
            assert!(options(&args.into_iter().map(OsString::from).collect::<Vec<_>>()).is_err());
        }
        assert_eq!(
            options(&["--output", "x", "--seconds", "60"].map(OsString::from)).unwrap(),
            (PathBuf::from("x"), 60)
        );
    }
    #[test]
    fn exhaustion_preserves_prior_records() {
        let temp = tempfile::tempdir().unwrap();
        let path = temp.path().join("capture.jsonl");
        let mut capture = Capture {
            file: std::fs::File::create(&path).unwrap(),
            started: Instant::now(),
            bytes: 0,
        };
        capture.record("test", json!({})).unwrap();
        let before = std::fs::read(&path).unwrap();
        capture.bytes = LIMIT;
        assert!(capture.record("overflow", json!({})).is_err());
        assert_eq!(std::fs::read(&path).unwrap(), before);
    }
}
