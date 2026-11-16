{ lib, stdenv, makeWrapper, host, bash, jq, coreutils, util-linux, systemd, libnotify, desktop-file-utils }:
let
  commands = {
    hdt-proton = "";
    hdt-launch = "raw-launch";
    hdt-overlay-launch = "overlay-launch";
    hdt-status = "status";
    hdt-setup = "setup";
    hdt-steam-options = "steam-options";
    hdt-build-surface-probe = "build-surface-probe";
  };
  runtimePath = lib.makeBinPath [ bash jq coreutils util-linux systemd libnotify ];
in
stdenv.mkDerivation {
  pname = "hdt-proton";
  version = "0.1.0";
  src = lib.cleanSourceWith {
    src = ../.;
    filter = path: type:
      let relative = lib.removePrefix ((toString ../.) + "/") (toString path);
      in type == "directory" && (relative == "src" || relative == "scripts" || lib.hasPrefix "src/" relative || lib.hasPrefix "scripts/" relative)
        || type == "regular" && ((lib.hasPrefix "src/" relative && (lib.hasSuffix ".cs" relative || lib.hasSuffix ".lua" relative)) || (lib.hasPrefix "scripts/" relative && lib.hasSuffix ".sh" relative) || relative == "LICENSE");
  };
  nativeBuildInputs = [ makeWrapper desktop-file-utils ];
  dontBuild = true;
  installPhase = ''
    runHook preInstall
    mkdir -p "$out/share/hdt-proton" "$out/libexec/hdt-proton" "$out/bin" "$out/share/applications"
    cp -R src scripts LICENSE "$out/share/hdt-proton/"
    makeWrapper ${host}/bin/hdt-host "$out/libexec/hdt-proton/overlay-link" --add-flags overlay-link
    makeWrapper ${host}/bin/hdt-host "$out/libexec/hdt-proton/x-surface-probe" --add-flags surface-probe
    ${lib.concatStringsSep "\n" (lib.mapAttrsToList (name: command: ''
      makeWrapper ${bash}/bin/bash "$out/bin/${name}" \
        --add-flags "$out/share/hdt-proton/scripts/hdt.sh ${command}" \
        --prefix PATH : "$out/bin:${runtimePath}" \
        --set HDT_HOST_BINARY "${host}/bin/hdt-host" \
        --set HDT_NATIVE_DIR "$out/libexec/hdt-proton" \
        --set HDT_INSTALLED 1 \
        --run 'export HDT_PROJECT_DIR="''${HDT_PROJECT_DIR:-''${XDG_DATA_HOME:-$HOME/.local/share}/hdt-proton}"'
    '') commands)}
    substitute ${./desktop-entry.desktop.in} "$out/share/applications/io.github.phibkro.hdt-proton.desktop" \
      --replace-fail @launcher@ "$out/bin/hdt-proton"
    desktop-file-validate "$out/share/applications/io.github.phibkro.hdt-proton.desktop"
    runHook postInstall
  '';
  meta = {
    description = "HDT compatibility launcher and desktop integration for Steam Proton and Hyprland";
    license = lib.licenses.mit;
    platforms = [ "x86_64-linux" ];
    mainProgram = "hdt-proton";
  };
}
