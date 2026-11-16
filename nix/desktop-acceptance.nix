# Optional private compositor diagnostics, using the project's existing pin.
let
  lock = builtins.fromJSON (builtins.readFile ../devenv.lock);
  source = builtins.fetchTree lock.nodes.nixpkgs.locked;
  sets = (import "${source.outPath}/flake.nix").outputs {
    self = { };
    nixpkgs-src = builtins.fetchTree lock.nodes.nixpkgs-src.locked;
  };
  pkgs = sets.legacyPackages.x86_64-linux;
in
pkgs.mkShell {
  packages = with pkgs; [ bash coreutils bubblewrap dbus jq xprop gawk gnugrep git ];
  HDT_KWIN_BINARY = "${pkgs.kdePackages.kwin}/bin/kwin_wayland";
  HDT_MESA_EGL_VENDOR = "${pkgs.mesa}/share/glvnd/egl_vendor.d/50_mesa.json";
}
