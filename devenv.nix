{ pkgs, config, ... }:
let
  command = name: ''exec bash "$REA_PROJECT/scripts/hdt.sh" ${name} "$@"'';
in
{
  packages = with pkgs; [ bash shellcheck actionlint bun nodejs_24 dotnet-sdk_10 perf sysstat llvmPackages.llvm protontricks winetricks steam-run util-linux procps ripgrep jq unzip git rsync gitleaks libnotify xorg.xwininfo xorg.xprop xorg.xinput xorg.xorgserver xcompmgr openbox xdotool imagemagick bubblewrap gcc pkg-config libx11 libxi libxcomposite libxrender ];
  env.REA_PROJECT = config.devenv.root;
  env.W_CACHE = "${config.devenv.root}/cache/winetricks";
  env.npm_config_cache = "${config.devenv.root}/cache/npm";
  env.DOTNET_CLI_HOME = "${config.devenv.root}/cache/dotnet";
  env.NUGET_PACKAGES = "${config.devenv.root}/cache/nuget";
  env.DOTNET_CLI_TELEMETRY_OPTOUT = "1";
  env.DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1";
  env.DOTNET_NOLOGO = "1";
  env.DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = "true";
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
