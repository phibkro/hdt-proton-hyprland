//! Bounded regular-file reads for the native UI. The opened handle owns identity.
use anyhow::{Context, Result, ensure};
use std::{
    ffi::OsString,
    fs::OpenOptions,
    io::{Read, Write},
    os::unix::fs::OpenOptionsExt,
    path::Path,
};

// The explicit caller bound remains authoritative. Full localized catalogs can
// exceed the replay limit; preview replay callers still request only 8 MiB.
pub const MAX_BYTES: usize = 32 * 1024 * 1024;

pub fn read(path: &Path, maximum: usize) -> Result<Option<Vec<u8>>> {
    ensure!(
        (1..=MAX_BYTES).contains(&maximum),
        "snapshot bound must be 1..={MAX_BYTES} bytes"
    );
    let file = match OpenOptions::new()
        .read(true)
        .custom_flags(nix::libc::O_NONBLOCK | nix::libc::O_NOFOLLOW)
        .open(path)
    {
        Ok(file) => file,
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => return Ok(None),
        Err(error) => return Err(error).context("opening snapshot"),
    };
    let metadata = file.metadata().context("reading snapshot metadata")?;
    ensure!(metadata.is_file(), "snapshot is not a regular file");
    ensure!(
        metadata.len() <= maximum as u64,
        "snapshot exceeds byte bound"
    );
    let mut bytes = Vec::new();
    file.take(maximum as u64 + 1)
        .read_to_end(&mut bytes)
        .context("reading snapshot")?;
    ensure!(bytes.len() <= maximum, "snapshot grew beyond byte bound");
    Ok(Some(bytes))
}

pub fn run(args: &[OsString]) -> Result<i32> {
    let [maximum, path] = args else {
        anyhow::bail!("Usage: hdt-host read-snapshot MAX_BYTES PATH");
    };
    let maximum = maximum
        .to_str()
        .context("snapshot bound must be UTF-8")?
        .parse::<usize>()
        .context("snapshot bound must be an unsigned integer")?;
    match read(Path::new(path), maximum)? {
        Some(bytes) => {
            std::io::stdout().lock().write_all(&bytes)?;
            Ok(0)
        }
        None => Ok(2), // Distinct missing-file result; default settings must not hide other faults.
    }
}
