{ pkgs }:
let
  patched = pkgs.libxi.overrideAttrs (old: {
    patches = (old.patches or [ ]) ++ [ ../patches/libxi/0001-retain-open-device-id.patch ];
  });
in
{
  inherit patched;
  # Steam supplies the dependency libraries in its own namespace. Do not add
  # the Nix-built X11/glibc closure to the Windows process's library search.
  steamRuntime = pkgs.runCommand "hdt-libxi-steam-runtime" {
    nativeBuildInputs = [ pkgs.patchelf pkgs.binutils pkgs.gnutar pkgs.xz ];
    meta = {
      inherit (pkgs.libxi.meta) license platforms;
      description = "Patched libXi for an explicitly selected HDT Steam runtime";
    };
  } ''
    mkdir -p "$out/lib" "$out/share/licenses/libXi" "$out/share/doc/libXi"
    tar -xOf ${pkgs.libxi.src} libXi-${pkgs.libxi.version}/COPYING > "$out/share/licenses/libXi/COPYING"
    cp ${../patches/libxi/0001-retain-open-device-id.patch} "$out/share/doc/libXi/0001-retain-open-device-id.patch"
    cp ${pkgs.lib.getLib patched}/lib/libXi.so.6 "$out/lib/libXi.so.6"
    chmod u+w "$out/lib/libXi.so.6"
    patchelf --remove-rpath "$out/lib/libXi.so.6"
    test -z "$(patchelf --print-rpath "$out/lib/libXi.so.6")"
    test "$(patchelf --print-soname "$out/lib/libXi.so.6")" = libXi.so.6
    readelf --version-info "$out/lib/libXi.so.6" > "$out/glibc-versions.txt"
    sha256sum "$out/lib/libXi.so.6" > "$out/library-sha256.txt"
  '';
}
