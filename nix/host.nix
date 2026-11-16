{ lib, rustPlatform, bash }:
rustPlatform.buildRustPackage {
  pname = "hdt-host";
  version = "0.1.0";
  src = lib.cleanSourceWith {
    src = ../.;
    filter = path: type:
      let relative = lib.removePrefix ((toString ../.) + "/") (toString path);
      in type == "directory" && (relative == "src" || relative == "src/host" || relative == "tests")
        || type == "regular" && (lib.hasPrefix "src/host/" relative
          || relative == "Cargo.toml" || relative == "Cargo.lock" || relative == "LICENSE"
          || (lib.hasPrefix "tests/rust-" relative && lib.hasSuffix ".rs" relative));
  };
  cargoLock.lockFile = ../Cargo.lock;
  # Rust subprocess fixtures use the declared interpreter inside the Nix sandbox.
  postPatch = ''
    substituteInPlace src/host/launch.rs --replace-fail '"/bin/sh"' '"${bash}/bin/bash"'
  '';
  nativeCheckInputs = [ bash ];
  meta = {
    description = "Native Linux host for the HDT Proton compatibility plugin";
    license = lib.licenses.mit;
    platforms = [ "x86_64-linux" ];
    mainProgram = "hdt-host";
  };
}
