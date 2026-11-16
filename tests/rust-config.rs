//! Configuration is tested through the actual native executable at its environment/file boundary.
use serde_json::Value;
use std::{fs, process::Command};

fn command(root: &std::path::Path) -> Command {
    let mut child = Command::new(env!("CARGO_BIN_EXE_hdt-host"));
    child
        .args(["config"])
        .env_clear()
        .env("HOME", "/home/test")
        .env("HDT_PROJECT_DIR", root);
    child
}

#[test]
fn installed_native_host_uses_mutable_data_and_explicit_source() {
    let root = tempfile::tempdir().unwrap();
    fs::write(
        root.path().join("hdt.local.json"),
        r#"{"steamAppId":"6789","steamRoot":"/configured/steam"}"#,
    )
    .unwrap();
    let result = command(root.path())
        .env("HDT_SOURCE_ROOT", "/nix/store/immutable-source")
        .current_dir("/tmp")
        .output()
        .unwrap();
    assert!(
        result.status.success(),
        "{}",
        String::from_utf8_lossy(&result.stderr)
    );
    let value: Value = serde_json::from_slice(&result.stdout).unwrap();
    assert_eq!(value["projectRoot"], root.path().to_str().unwrap());
    assert_eq!(value["sourceRoot"], "/nix/store/immutable-source");
    assert_eq!(
        value["appDirectory"],
        root.path().join("app").to_str().unwrap()
    );
    assert_eq!(
        value["prefix"],
        "/configured/steam/steamapps/compatdata/6789/pfx"
    );
    let field = command(root.path())
        .arg("windowClass")
        .env("HS_APP_ID", "123")
        .output()
        .unwrap();
    assert!(field.status.success());
    assert_eq!(field.stdout, b"steam_app_123\n");
}

#[test]
fn malformed_local_config_and_invalid_overrides_fail_before_runtime() {
    let root = tempfile::tempdir().unwrap();
    for invalid in [
        r#"{"steamAppId":123}"#,
        r#"{"steamAppId":"1","unexpected":true}"#,
        r#"[]"#,
        r#"{"steamAppId":"01"}"#,
    ] {
        fs::write(root.path().join("hdt.local.json"), invalid).unwrap();
        let result = command(root.path()).output().unwrap();
        assert!(!result.status.success(), "accepted {invalid}");
    }
    fs::write(root.path().join("hdt.local.json"), r#"{"steamAppId":"1"}"#).unwrap();
    for (name, value) in [
        ("HDT_PROJECT_DIR", "relative"),
        ("HDT_PROJECT_DIR", "/nix/store/immutable"),
        ("STEAM_ROOT", "relative"),
        ("HDT_NATIVE_DIR", "relative"),
        ("HS_APP_ID", "../different-prefix"),
    ] {
        let result = command(root.path()).env(name, value).output().unwrap();
        assert!(!result.status.success(), "accepted {name}");
    }
}
