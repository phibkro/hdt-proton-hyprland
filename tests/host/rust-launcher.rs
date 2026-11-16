//! Native launcher contracts that do not require a game, prefix, or compositor.
use hdt_host::launch::{renderer_environment, tracker_environment};
use std::{collections::BTreeMap, ffi::OsString, path::Path};
fn environment(entries: &[(&str, &str)]) -> BTreeMap<OsString, OsString> {
    entries.iter().map(|(k, v)| (k.into(), v.into())).collect()
}
#[test]
fn steam_preloads_are_removed_without_changing_other_libraries() {
    let result = tracker_environment(environment(&[
        (
            "LD_PRELOAD",
            "/steam/gameoverlayrenderer.so:/keep/a.so /keep/b.so\tgameoverlayrenderer.so",
        ),
        ("KEEP", "private-value"),
    ]));
    assert_eq!(
        result.get(std::ffi::OsStr::new("LD_PRELOAD")),
        Some(&OsString::from("/keep/a.so:/keep/b.so"))
    );
    assert_eq!(
        result.get(std::ffi::OsStr::new("KEEP")),
        Some(&OsString::from("private-value"))
    );
    assert!(
        !tracker_environment(environment(&[("LD_PRELOAD", "gameoverlayrenderer.so")]))
            .contains_key(std::ffi::OsStr::new("LD_PRELOAD"))
    );
}
#[test]
fn gdi_preserves_other_dll_and_renderer_settings() {
    let mut input = environment(&[
        ("WINEDLLOVERRIDES", "d3d9,dxgi=n,b;foo=b"),
        ("WINE_D3D_CONFIG", "renderer=vulkan,check_float_constants=1"),
    ]);
    renderer_environment(&mut input, "wine-gdi", Path::new("/evidence")).unwrap();
    assert_eq!(
        input.get(std::ffi::OsStr::new("WINEDLLOVERRIDES")),
        Some(&OsString::from("dxgi=n,b;foo=b;d3d9=b"))
    );
    assert_eq!(
        input.get(std::ffi::OsStr::new("WINE_D3D_CONFIG")),
        Some(&OsString::from("check_float_constants=1;renderer=gdi"))
    );
}
#[test]
fn invalid_renderer_and_dll_overrides_fail_loudly() {
    assert!(renderer_environment(&mut environment(&[]), "typo", Path::new("/evidence")).is_err());
    assert!(
        renderer_environment(
            &mut environment(&[("WINEDLLOVERRIDES", "malformed")]),
            "wine-gdi",
            Path::new("/evidence")
        )
        .is_err()
    );
}

#[test]
fn exact_preload_filter_preserves_similar_names_and_absent_loader_variable() {
    let original = environment(&[
        ("WINEPREFIX", "/game/prefix"),
        (
            "LD_PRELOAD",
            "/steam/steamrt32/gameoverlayrenderer.so:/tools/libMangoHud.so /steam/steamrt64/gameoverlayrenderer.so\t/tools/custom.so",
        ),
        ("EXTRA", "unchanged"),
    ]);
    let result = tracker_environment(original.clone());
    assert_eq!(
        result.get(std::ffi::OsStr::new("LD_PRELOAD")),
        Some(&OsString::from("/tools/libMangoHud.so:/tools/custom.so"))
    );
    assert!(
        original
            .get(std::ffi::OsStr::new("LD_PRELOAD"))
            .unwrap()
            .to_string_lossy()
            .contains("steamrt32")
    );
    assert_eq!(
        result.get(std::ffi::OsStr::new("EXTRA")),
        original.get(std::ffi::OsStr::new("EXTRA"))
    );
    assert_eq!(
        tracker_environment(environment(&[("WINEPREFIX", "/prefix")])),
        environment(&[("WINEPREFIX", "/prefix")])
    );
    let similar = "/tools/gameoverlayrenderer.so.backup:/tools/mygameoverlayrenderer.so";
    assert_eq!(
        tracker_environment(environment(&[("LD_PRELOAD", similar)]))
            .get(std::ffi::OsStr::new("LD_PRELOAD")),
        Some(&OsString::from(similar))
    );
}
