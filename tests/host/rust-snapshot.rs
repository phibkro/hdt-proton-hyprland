//! Exercise the executable's actual file-type, size and output boundary.
use std::{fs, os::unix::fs::symlink, process::Command};

fn read(path: &std::path::Path, maximum: &str) -> std::process::Output {
    let mut child = Command::new(env!("CARGO_BIN_EXE_hdt-host"))
        .args(["read-snapshot", maximum])
        .arg(path)
        .stdout(std::process::Stdio::piped())
        .stderr(std::process::Stdio::piped())
        .spawn()
        .unwrap();
    let started = std::time::Instant::now();
    while child.try_wait().unwrap().is_none() {
        if started.elapsed() > std::time::Duration::from_secs(2) {
            child.kill().unwrap();
            child.wait().unwrap();
            panic!("snapshot child exceeded deadline; owned child was killed and reaped");
        }
        std::thread::sleep(std::time::Duration::from_millis(5));
    }
    child.wait_with_output().unwrap()
}

#[test]
fn accepts_only_bounded_regular_files_and_preserves_exact_bytes() {
    let root = tempfile::tempdir().unwrap();
    let path = root.path().join("snapshot");
    let data = b"{\"state\":\"observed\"}\n";
    fs::write(&path, data).unwrap();
    let result = read(&path, &data.len().to_string());
    assert!(result.status.success());
    assert_eq!(result.stdout, data);
    for maximum in [
        "0".to_owned(),
        (hdt_host::snapshot::MAX_BYTES + 1).to_string(),
        "bad".to_owned(),
        "1".to_owned(),
    ] {
        let result = read(&path, &maximum);
        assert!(!result.status.success());
        assert!(result.stdout.is_empty());
    }
    assert_eq!(
        read(&root.path().join("missing"), "1024").status.code(),
        Some(2)
    );
    let link = root.path().join("link");
    symlink(&path, &link).unwrap();
    for nonregular in [&link, root.path(), std::path::Path::new("/dev/zero")] {
        let result = read(nonregular, "1024");
        assert!(!result.status.success());
        assert!(result.stdout.is_empty());
    }
    let fifo = root.path().join("fifo");
    nix::unistd::mkfifo(
        &fifo,
        nix::sys::stat::Mode::S_IRUSR | nix::sys::stat::Mode::S_IWUSR,
    )
    .unwrap();
    let before = std::time::Instant::now();
    let result = read(&fifo, "1024");
    assert!(!result.status.success());
    assert!(result.stdout.is_empty());
    assert!(
        before.elapsed() < std::time::Duration::from_secs(2),
        "FIFO with no writer must not block"
    );
}
