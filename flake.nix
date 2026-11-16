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
      devShells.${system}.default = pkgs.mkShell {
        packages = import ./nix/tooling.nix { inherit pkgs; };
        DOTNET_CLI_TELEMETRY_OPTOUT = "1";
        DOTNET_NOLOGO = "1";
      };
      checks.${system} = {
        inherit package;
        native-quality = pkgs.runCommand "hdt-native-source-quality" {
          nativeBuildInputs = import ./nix/tooling.nix { inherit pkgs; };
        } ''
          mkdir source
          cp -R ${./src} source/src
          cp -R ${./tests} source/tests
          cp -R ${./scripts} source/scripts
          cp ${./autostart.sh} source/autostart.sh
          cp ${./.envrc} source/.envrc
          mkdir -p source/.githooks
          cp ${./.githooks/pre-commit} source/.githooks/pre-commit
          cp ${./tooling/shell/shellcheckrc} source/.shellcheckrc
          cp ${./.editorconfig} source/.editorconfig
          cp -L ${./tooling/c/.clang-format} source/.clang-format
          cp -L ${./tooling/c/.clang-tidy} source/.clang-tidy
          cp ${./tooling/lua/stylua.toml} source/stylua.toml
          cp ${./tooling/lua/selene.toml} source/selene.toml
          cp ${./tooling/lua/hdt-hyprland.yml} source/hdt-hyprland.yml
          chmod -R u+w source
          cd source
          export HOME="$TMPDIR/home"
          mkdir -p "$HOME"
          bun scripts/quality.ts check c
          bun scripts/quality.ts check lua
          bun scripts/quality.ts check csharp
          bun scripts/quality.ts check shell
          touch "$out"
        '';
        tests = pkgs.runCommand "hdt-proton-tests" {
          nativeBuildInputs = [ pkgs.bun pkgs.bash pkgs.shellcheck pkgs.actionlint pkgs.jq pkgs.coreutils pkgs.util-linux pkgs.ripgrep ];
        } ''
          mkdir source
          cp -R ${./src} source/src
          cp -R ${./scripts} source/scripts
          cp ${./autostart.sh} source/autostart.sh
          cp ${./.envrc} source/.envrc
          mkdir -p source/.githooks
          cp ${./.githooks/pre-commit} source/.githooks/pre-commit
          cp ${./tooling/shell/shellcheckrc} source/.shellcheckrc
          cp -R ${./tests} source/tests
          cp -R ${./.github} source/.github
          chmod -R u+w source
          cd source
          export HOME="$TMPDIR/home" HS_APP_ID=1234567890
          mkdir -p "$HOME"
          bash scripts/test-unit.sh
          shellcheck scripts/*.sh
          actionlint .github/workflows/check.yml
          touch "$out"
        '';
      };
    };
}
