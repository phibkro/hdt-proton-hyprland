//! Refusal paths must reject malformed input before attempting an X connection.
use std::process::Command;

#[test]
fn malformed_surface_regions_are_refused_before_display_access() {
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
        let mut args = valid;
        args[index] = value;
        let output = Command::new(env!("CARGO_BIN_EXE_hdt-host"))
            .arg("surface-probe")
            .args(args)
            .env("DISPLAY", "")
            .output()
            .unwrap();
        assert!(!output.status.success());
        let error = String::from_utf8(output.stderr).unwrap();
        assert!(
            error.contains("invalid argument"),
            "{index}: {value}: {error}"
        );
        assert!(!error.contains("cannot open display"));
    }
}

#[test]
fn int_coordinate_limits_reach_the_display_boundary() {
    for coordinate in ["-2147483648", "2147483647"] {
        let output = Command::new(env!("CARGO_BIN_EXE_hdt-host"))
            .args(["surface-probe", "1", "1", coordinate, "0", "32", "32"])
            .env("DISPLAY", "")
            .output()
            .unwrap();
        assert!(!output.status.success());
        assert!(
            String::from_utf8(output.stderr)
                .unwrap()
                .contains("cannot open display")
        );
    }
}
