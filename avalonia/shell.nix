# Reuse the parent project's one authoritative nixpkgs pin.
let
  lock = builtins.fromJSON (builtins.readFile ../devenv.lock);
  source = builtins.fetchTree lock.nodes.nixpkgs.locked;
  sets = (import "${source.outPath}/flake.nix").outputs {
    self = {};
    nixpkgs-src = builtins.fetchTree lock.nodes.nixpkgs-src.locked;
  };
  pkgs = sets.legacyPackages.x86_64-linux;
  libraries = with pkgs; [ fontconfig freetype libGL libice libsm libx11 libxcursor libxrandr libxi zlib ];
in pkgs.mkShell {
  packages = with pkgs; [ dotnet-sdk_10 xorg-server xwininfo imagemagick ];
  LD_LIBRARY_PATH = pkgs.lib.makeLibraryPath libraries;
  DOTNET_CLI_TELEMETRY_OPTOUT = "1";
  DOTNET_NOLOGO = "1";
  DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1";
  DOTNET_PROCESSOR_COUNT = "2";
  shellHook = ''
    export DOTNET_CLI_HOME="$PWD/local/dotnet"
    export NUGET_PACKAGES="$PWD/local/nuget"
  '';
}
