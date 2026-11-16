{
  description = "HDT compatibility tools for Steam Proton and Hyprland";

  outputs = { self }:
    let
      # devenv.lock is the single dependency pin for the shell and package.
      lock = builtins.fromJSON (builtins.readFile ./devenv.lock);
      nixpkgs = builtins.fetchTree lock.nodes.nixpkgs.locked;
      system = "x86_64-linux";
      packageSets = (import "${nixpkgs.outPath}/flake.nix").outputs {
        self = { };
        nixpkgs-src = builtins.fetchTree lock.nodes.nixpkgs-src.locked;
      };
      pkgs = packageSets.legacyPackages.${system};
      package = pkgs.callPackage ./nix/package.nix { };
    in
    {
      packages.${system} = {
        default = package;
        hdt-proton = package;
      };
      apps.${system}.default = {
        type = "app";
        meta.description = "Open HDT or manage its desktop integration";
        program = "${package}/bin/hdt-proton";
      };
      homeManagerModules.default = import ./nix/home-manager.nix self;
      checks.${system} = {
        inherit package;
        tests = pkgs.runCommand "hdt-proton-tests" {
          nativeBuildInputs = [ pkgs.bun pkgs.bash pkgs.shellcheck pkgs.actionlint pkgs.jq pkgs.coreutils ];
        } ''
          mkdir source
          cp -R ${./src} source/src
          cp -R ${./scripts} source/scripts
          cp -R ${./tests} source/tests
          cp -R ${./.github} source/.github
          chmod -R u+w source
          cd source
          export HOME="$TMPDIR/home" HS_APP_ID=1234567890
          mkdir -p "$HOME"
          bun test ./tests/config.test.ts ./tests/steam-launch.test.ts ./tests/desktop.test.ts ./tests/desktop-backend.test.ts ./tests/trace-export.test.ts
          shellcheck scripts/*.sh
          actionlint .github/workflows/check.yml
          touch "$out"
        '';
        desktop-x11 = pkgs.runCommand "hdt-desktop-x11-check" {
          nativeBuildInputs = with pkgs; [ bun bash coreutils ripgrep xorg.xorgserver xorg.xprop openbox xdotool stdenv.cc pkg-config libx11 libxext ];
        } ''
          mkdir -p source/scripts source/tests
          cp ${./scripts/check-x11.sh} source/scripts/check-x11.sh
          cp ${./tests/x11-conformance.ts} source/tests/x11-conformance.ts
          export HDT_X11_TEST_RUN="$TMPDIR/desktop-test" HDT_X11_PRIVATE=1
          export HOME="$HDT_X11_TEST_RUN/home" XDG_RUNTIME_DIR="$HDT_X11_TEST_RUN/runtime"
          mkdir -p "$HDT_X11_TEST_RUN"
          cp ${package}/libexec/hdt-proton/x11-desktop "$HDT_X11_TEST_RUN/"
          cc -Wall -Wextra -Werror ${./tests/x11-fixture.c} -o "$HDT_X11_TEST_RUN/x11-fixture" $(pkg-config --cflags --libs x11 xext)
          bash source/scripts/check-x11.sh --inside openbox
          cp "$HDT_X11_TEST_RUN/conformance.json" "$out"
        '';
      };
    };
}
