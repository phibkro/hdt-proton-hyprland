flake:
{ config, lib, pkgs, ... }:
let
  cfg = config.programs.hdt-proton;
  package = flake.packages.${pkgs.stdenv.hostPlatform.system}.default;
  environment = { HDT_PROJECT_DIR = cfg.dataDirectory; HDT_DESKTOP_BACKEND = cfg.desktopBackend; }
    // lib.optionalAttrs (cfg.appDirectory != null) { HDT_APP_DIR = cfg.appDirectory; };
  launcher = pkgs.writeShellScript "hdt-proton-desktop" ''
    ${lib.concatStringsSep "\n" (lib.mapAttrsToList (key: value: "export ${key}=${lib.escapeShellArg value}") environment)}
    exec ${package}/bin/hdt-proton "$@"
  '';
in
{
  options.programs.hdt-proton = {
    enable = lib.mkEnableOption "HDT compatibility tools and desktop integration";
    dataDirectory = lib.mkOption {
      type = lib.types.str;
      default = "${config.xdg.dataHome}/hdt-proton";
      description = "Mutable directory containing hdt.local.json, app, backups, and evidence.";
    };
    appDirectory = lib.mkOption {
      type = lib.types.nullOr lib.types.str;
      default = null;
      description = "Complete HDT application directory, or null to use dataDirectory/app.";
    };
    autoStart = lib.mkOption {
      type = lib.types.bool;
      default = true;
      description = "Start the HDT watcher with the Hyprland session.";
    };
    desktopBackend = lib.mkOption {
      type = lib.types.enum [ "auto" "hyprland" "x11" ];
      default = "auto";
      description = "Desktop window/input backend. x11 supports experimental EWMH/XWayland integration.";
    };
    sessionTarget = lib.mkOption {
      type = lib.types.str;
      default = "hyprland-session.target";
      description = "Session target that starts and stops the watcher. Use graphical-session.target on KDE or GNOME.";
    };
  };
  config = lib.mkIf cfg.enable {
    assertions = [ {
      assertion = lib.hasPrefix "/" cfg.dataDirectory && cfg.dataDirectory != "/nix/store" && !(lib.hasPrefix "/nix/store/" cfg.dataDirectory);
      message = "programs.hdt-proton.dataDirectory must be absolute and outside the Nix store.";
    } ];
    home.packages = [ package ];
    xdg.dataFile."applications/io.github.phibkro.hdt-proton.desktop" = {
      text = builtins.replaceStrings [ "@launcher@" ] [ "${launcher}" ] (builtins.readFile ./desktop-entry.desktop.in);
      force = true;
    };
    systemd.user.services.hdt-proton-autostart = {
      Unit = {
        Description = "Start HDT compatibility launcher when Hearthstone opens";
        After = [ cfg.sessionTarget ];
        PartOf = [ cfg.sessionTarget ];
        ConditionPathExists = "${cfg.dataDirectory}/hdt.local.json";
      };
      Service = {
        Type = "simple";
        ExecStart = "${package}/bin/hdt-proton watch";
        Environment = lib.mapAttrsToList (key: value: "${key}=${value}") environment;
        Restart = "on-failure";
        RestartSec = 5;
      };
      Install.WantedBy = lib.optional cfg.autoStart cfg.sessionTarget;
    };
  };
}
