{ pkgs }:
let
  patched = (pkgs.callPackage ./libxi.nix { }).patched;
in
pkgs.runCommand "libxi-open-device-regression" {
  nativeBuildInputs = [ pkgs.stdenv.cc pkgs.pkg-config pkgs.xorg-server pkgs.coreutils pkgs.ripgrep ];
  buildInputs = [ pkgs.libx11 pkgs.libxi ];
} ''
  mkdir -p "$out"
  cc -std=c11 -Wall -Wextra -Werror -pthread \
    ${../tests/native/libxi-open-device.c} -o "$out/open-device" \
    $(pkg-config --cflags --libs x11 xi)
  cd "$out"
  bash ${../tests/native/libxi-open-device.sh} \
    "${pkgs.lib.getLib pkgs.libxi}/lib" "${pkgs.lib.getLib patched}/lib" \
    "$out/open-device" "${pkgs.xorg-server}/bin/Xvfb"
''
