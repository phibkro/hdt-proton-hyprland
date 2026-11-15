{ pkgs, config, ... }:
{
  packages = with pkgs; [ bash shellcheck bun nodejs_24 protontricks winetricks steam-run util-linux ripgrep jq unzip git rsync gitleaks xorg.xwininfo xorg.xprop gcc pkg-config xorg.libX11 xorg.libXcomposite xorg.libXrender ];
  env.REA_PROJECT = config.devenv.root;
  env.W_CACHE = "${config.devenv.root}/cache/winetricks";
  env.npm_config_cache = "${config.devenv.root}/cache/npm";
  scripts.hdt-launch.exec = ''exec bun "$REA_PROJECT/src/runtime/launch.ts" "$@"'';
  scripts.hdt-status.exec = ''exec bun "$REA_PROJECT/src/runtime/status.ts"'';
  scripts.hdt-build-surface-probe.exec = ''
    mkdir -p "$REA_PROJECT/evidence"
    cc -Wall -Wextra -Werror "$REA_PROJECT/src/diagnostics/x-surface-probe.c" -o "$REA_PROJECT/evidence/x-surface-probe" $(pkg-config --cflags --libs x11 xcomposite xrender)
  '';
  scripts.hdt-overlay-launch.exec = ''
    mkdir -p "$REA_PROJECT/evidence" || exit $?
    cc -Wall -Wextra -Werror "$REA_PROJECT/src/hyprland/overlay-link.c" -o "$REA_PROJECT/evidence/overlay-link" $(pkg-config --cflags --libs x11) || exit $?
    bun "$REA_PROJECT/src/hyprland/overlay-rule.ts" || exit $?
    export HDT_DIRECT=1 HDT_SOFTWARE_RENDER=1
    export HDT_RENDERER="''${HDT_RENDERER:-dxvk-software}"
    export HDT_ALPHA_FLOOR="''${HDT_ALPHA_FLOOR:-1}"
    export HDT_CONTROL_INPUT="''${HDT_CONTROL_INPUT:-1}"
    exec bun "$REA_PROJECT/src/runtime/launch.ts"
  '';
  scripts.hdt-check.exec = ''exec bash "$REA_PROJECT/scripts/check.sh" "$@"'';
  scripts.hdt-setup.exec = ''exec bash "$REA_PROJECT/scripts/setup.sh" "$@"'';
  scripts.hdt-steam-options.exec = ''exec bash "$REA_PROJECT/scripts/steam-options.sh" "$@"'';
  scripts.rea.exec = ''exec npx --yes rea-agents@6.8.0 "$@"'';
}
