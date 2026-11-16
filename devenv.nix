{ pkgs, config, ... }:
let
  command = name: ''exec bash "$REA_PROJECT/scripts/hdt.sh" ${name} "$@"'';
in
{
  packages = with pkgs; [ bash shellcheck actionlint bun nodejs_24 protontricks winetricks steam-run util-linux ripgrep jq unzip git rsync gitleaks libnotify xorg.xwininfo xorg.xprop xorg.xorgserver xwayland xcompmgr openbox xdotool imagemagick bubblewrap gcc pkg-config libx11 libxext libxrandr libxcomposite libxrender strace dbus kdePackages.kwin mutter ];
  env.HDT_KWIN_BINARY = "${pkgs.kdePackages.kwin}/bin/kwin_wayland";
  env.HDT_TEST_EGL_VENDOR = "${pkgs.mesa}/share/glvnd/egl_vendor.d/50_mesa.json";
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
  scripts.hdt-replay.exec = ''exec bash "$REA_PROJECT/scripts/replay.sh" "$@"'';
  scripts.rea.exec = ''exec npx --yes rea-agents@6.8.0 "$@"'';
}
