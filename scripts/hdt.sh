#!/usr/bin/env bash
set -euo pipefail
source_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)
cd -- "$source_root"
command=${1:-open}
if (($#)); then shift; fi
data_root=${HDT_PROJECT_DIR:-$source_root}
native_dir=${HDT_NATIVE_DIR:-$data_root/evidence}
case "$command" in
    config | overlay-link | surface-probe | surface-check | install-plugin | configure-overlay | overlay-rule | control-input) exec bash "$source_root/scripts/native-host.sh" "$command" "$@" ;;
    trace-export) exec bash "$source_root/scripts/native-host.sh" trace-export "$@" ;;
    observe) exec bash "$source_root/scripts/native-host.sh" observe "$@" ;;
    open | restart) exec bash "$source_root/scripts/desktop.sh" "$command" "$@" ;;
    watch) exec bash "$source_root/scripts/autostart.sh" "$@" ;;
    setup) exec bash "$source_root/scripts/setup.sh" "$@" ;;
    status) exec bash "$source_root/scripts/native-host.sh" status "$@" ;;
    raw-launch) exec bash "$source_root/scripts/native-host.sh" raw-launch "$@" ;;
    steam-options) exec bash "$source_root/scripts/steam-options.sh" "$@" ;;
    steam-run) exec bash "$source_root/scripts/steam-launch.sh" "$@" ;;
    build-surface-probe)
        if [[ -n ${HDT_NATIVE_DIR:-} ]]; then
            test -x "$native_dir/x-surface-probe"
        else
            mkdir -p "$native_dir"
            printf '#!/usr/bin/env bash\nexec bash %q surface-probe "$@"\n' "$source_root/scripts/native-host.sh" >"$native_dir/x-surface-probe"
            chmod 755 "$native_dir/x-surface-probe"
        fi
        ;;
    overlay-launch)
        mkdir -p "$data_root/evidence"
        bash "$source_root/scripts/native-host.sh" overlay-rule
        export HDT_DIRECT=1 HDT_SOFTWARE_RENDER=1
        export HDT_RENDERER="${HDT_RENDERER:-dxvk-software}"
        export HDT_ALPHA_FLOOR="${HDT_ALPHA_FLOOR:-1}"
        export HDT_CONTROL_INPUT="${HDT_CONTROL_INPUT:-1}"
        exec bash "$source_root/scripts/native-host.sh" raw-launch "$@"
        ;;
    *)
        echo 'Usage: hdt-proton [open|restart|watch|setup|status|trace-export|observe|raw-launch|overlay-launch|steam-options|steam-run|build-surface-probe]' >&2
        exit 2
        ;;
esac
