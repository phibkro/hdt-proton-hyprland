# Reuse the parent project's one authoritative nixpkgs pin.
let
  lock = builtins.fromJSON (builtins.readFile ../devenv.lock);
  source = builtins.fetchTree lock.nodes.nixpkgs.locked;
  sets = (import "${source.outPath}/flake.nix").outputs {
    self = {};
    nixpkgs-src = builtins.fetchTree lock.nodes.nixpkgs-src.locked;
  };
  pkgs = sets.legacyPackages.x86_64-linux;
  libraries = with pkgs; [ fontconfig freetype icu libGL libice libsm libx11 libxcursor libxrandr libxi zlib ];
in pkgs.mkShell {
  packages = with pkgs; [ dotnet-sdk_10 cargo rustc rustfmt clippy xorg-server xwininfo imagemagick coreutils
    bash jq ripgrep bubblewrap gcc pkg-config libx11 libxcomposite libxrender xdotool ];
  LD_LIBRARY_PATH = pkgs.lib.makeLibraryPath libraries;
  DOTNET_CLI_TELEMETRY_OPTOUT = "1";
  DOTNET_NOLOGO = "1";
  DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1";
  DOTNET_SYSTEM_GLOBALIZATION_INVARIANT = "0";
  DOTNET_PROCESSOR_COUNT = "2";
  shellHook = ''
    export CARGO_TARGET_DIR="$PWD/local/avalonia-rust-target"
    export CARGO_HOME="$PWD/local/avalonia-cargo"
    export CARGO_BUILD_JOBS=2
    export HDT_HOST_BINARY="$CARGO_TARGET_DIR/debug/hdt-host"
    export DOTNET_CLI_HOME="$PWD/local/dotnet"
    export NUGET_PACKAGES="$PWD/local/nuget"
  '';
}
