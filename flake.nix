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
          nativeBuildInputs = [ pkgs.bun pkgs.bash pkgs.shellcheck pkgs.actionlint pkgs.jq pkgs.coreutils pkgs.util-linux ];
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
          bash scripts/test-unit.sh
          shellcheck scripts/*.sh
          actionlint .github/workflows/check.yml
          touch "$out"
        '';
      };
    };
}
