#!/usr/bin/env bash
set -euo pipefail
source_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)
cd -- "$source_root"
command=${1:-open}
if (( $# )); then shift; fi
data_root=${HDT_PROJECT_DIR:-$source_root}
native_dir=${HDT_NATIVE_DIR:-$data_root/evidence}
case "$command" in
    trace-export) exec bun "$source_root/src/diagnostics/trace-export.ts" "$@" ;;
    open|restart) exec bash "$source_root/scripts/desktop.sh" "$command" "$@" ;;
    watch) exec bash "$source_root/scripts/autostart.sh" "$@" ;;
    setup) exec bash "$source_root/scripts/setup.sh" "$@" ;;
    status) exec bun "$source_root/src/runtime/status.ts" "$@" ;;
    raw-launch) exec bun "$source_root/src/runtime/launch.ts" "$@" ;;
    steam-options) exec bash "$source_root/scripts/steam-options.sh" "$@" ;;
    steam-run) exec bash "$source_root/scripts/steam-launch.sh" "$@" ;;
    build-surface-probe)
        if [[ -n ${HDT_NATIVE_DIR:-} ]]; then
            test -x "$native_dir/x-surface-probe"
        else
            mkdir -p "$native_dir"
            read -r -a flags <<< "$(pkg-config --cflags --libs x11 xcomposite xrender)"
            cc -Wall -Wextra -Werror "$source_root/src/diagnostics/x-surface-probe.c" -o "$native_dir/x-surface-probe" "${flags[@]}"
        fi
        ;;
    overlay-launch)
        mkdir -p "$data_root/evidence"
        if [[ -n ${HDT_NATIVE_DIR:-} ]]; then
            test -x "$native_dir/overlay-link"
        else
            read -r -a flags <<< "$(pkg-config --cflags --libs x11)"
            cc -Wall -Wextra -Werror "$source_root/src/hyprland/overlay-link.c" -o "$native_dir/overlay-link" "${flags[@]}"
        fi
        bun "$source_root/src/hyprland/overlay-rule.ts"
        export HDT_DIRECT=1 HDT_SOFTWARE_RENDER=1
        export HDT_RENDERER="${HDT_RENDERER:-dxvk-software}"
        export HDT_ALPHA_FLOOR="${HDT_ALPHA_FLOOR:-1}"
        export HDT_CONTROL_INPUT="${HDT_CONTROL_INPUT:-1}"
        exec bun "$source_root/src/runtime/launch.ts" "$@"
        ;;
    *) echo 'Usage: hdt-proton [open|restart|watch|setup|status|trace-export|raw-launch|overlay-launch|steam-options|steam-run|build-surface-probe]' >&2; exit 2 ;;
esac
