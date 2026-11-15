{ pkgs, config, ... }:
let
  command = name: ''exec bash "$REA_PROJECT/scripts/hdt.sh" ${name} "$@"'';
in
{
  packages = with pkgs; [ bash shellcheck bun nodejs_24 protontricks winetricks steam-run util-linux ripgrep jq unzip git rsync gitleaks libnotify xorg.xwininfo xorg.xprop gcc pkg-config libx11 libxcomposite libxrender ];
  env.REA_PROJECT = config.devenv.root;
  env.W_CACHE = "${config.devenv.root}/cache/winetricks";
  env.npm_config_cache = "${config.devenv.root}/cache/npm";
  scripts.hdt-launch.exec = command "raw-launch";
  scripts.hdt-status.exec = command "status";
  scripts.hdt-build-surface-probe.exec = command "build-surface-probe";
  scripts.hdt-overlay-launch.exec = command "overlay-launch";
  scripts.hdt-proton.exec = ''exec bash "$REA_PROJECT/scripts/hdt.sh" "$@"'';
  scripts.hdt-check.exec = ''exec bash "$REA_PROJECT/scripts/check.sh" "$@"'';
  scripts.hdt-setup.exec = command "setup";
  scripts.hdt-steam-options.exec = command "steam-options";
  scripts.rea.exec = ''exec npx --yes rea-agents@6.8.0 "$@"'';
}
