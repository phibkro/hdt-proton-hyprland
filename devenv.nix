{ pkgs, config, ... }:
let
  command = name: ''exec bun "$REA_PROJECT/scripts/hdt.ts" ${name} "$@"'';
in
{
  packages = with pkgs; [ bash shellcheck actionlint bun nodejs_24 protontricks winetricks steam-run util-linux procps ripgrep jq unzip git rsync gitleaks libnotify xorg.xwininfo xorg.xprop xorg.xorgserver xcompmgr openbox xdotool imagemagick bubblewrap gcc pkg-config libx11 libxi libxcomposite libxrender ];
  env.REA_PROJECT = config.devenv.root;
  env.W_CACHE = "${config.devenv.root}/cache/winetricks";
  env.npm_config_cache = "${config.devenv.root}/cache/npm";
  scripts.hdt-launch.exec = command "raw-launch";
  scripts.hdt-status.exec = command "status";
  scripts.hdt-build-surface-probe.exec = command "build-surface-probe";
  scripts.hdt-overlay-launch.exec = command "overlay-launch";
  scripts.hdt-proton.exec = ''exec bun "$REA_PROJECT/scripts/hdt.ts" "$@"'';
  scripts.hdt-check.exec = ''exec bun "$REA_PROJECT/scripts/check.ts" "$@"'';
  scripts.hdt-setup.exec = command "setup";
  scripts.hdt-steam-options.exec = command "steam-options";
  scripts.hdt-replay.exec = ''exec bun "$REA_PROJECT/scripts/replay.ts" "$@"'';
  scripts.rea.exec = ''exec npx --yes rea-agents@6.8.0 "$@"'';
}
