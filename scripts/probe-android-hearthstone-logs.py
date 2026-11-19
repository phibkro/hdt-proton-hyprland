#!/usr/bin/env python3
"""Check Hearthstone's Android external log files without reading their contents."""

import argparse
import math
import subprocess
import sys
import time
from dataclasses import dataclass


PROBE = r"""
exec 2>&1
package=com.blizzard.wtcg.hearthstone
user=$(am get-current-user) || exit 1
case "$user" in
    ''|*[!0-9]*) echo 'Could not identify the active Android user.' >&2; exit 1 ;;
esac
root=/storage/emulated/$user/Android/data/$package/files
printf 'directory\t%s\n' "$root"
if ! pm path --user "$user" "$package" >/dev/null; then
    echo 'Could not find the Hearthstone package for the active Android user.' >&2
    exit 1
fi
find "$root" -type f \( -name Power.log -o -name LoadingScreen.log -o -name Arena.log \) \
    -exec sh -c '
        if ( : < "$1" ) 2>/dev/null; then
            access=readable
        else
            access=unreadable
        fi
        stat -c "file|$access|%i|%s|%Y|%n" "$1"
    ' sh {} \;
"""


@dataclass(frozen=True)
class LogFile:
    access: str
    inode: int
    size: int
    modified: int


def snapshot(adb, label):
    result = subprocess.run(
        [*adb, "shell", "-T", "sh", "-s"], input=PROBE,
        capture_output=True, text=True, timeout=30, check=False,
    )
    files = {}
    directory = None
    diagnostics = []
    for line in result.stdout.splitlines():
        if line.startswith("directory\t"):
            _, directory = line.split("\t", 1)
        elif line.startswith("file|"):
            _, access, inode, size, modified, path = line.split("|", 5)
            files[path] = LogFile(access, int(inode), int(size), int(modified))
        elif line.strip():
            diagnostics.append(line)
    print(f"{label} snapshot", flush=True)
    if directory:
        print(f"  Directory {directory}", flush=True)
    if result.returncode or diagnostics or directory is None:
        detail = "\n".join(diagnostics) or result.stderr.strip()
        detail = detail or f"Unexpected response (exit {result.returncode})."
        raise RuntimeError(f"Probe could not complete. {detail}")
    for path, entry in sorted(files.items()):
        relative = path.removeprefix(directory + "/")
        print(f"  {entry.size:>10} bytes  {relative} ({entry.access})")
    if not files:
        print("  No matching logs found in the scanned directory.")
    return directory, files


def describe_change(before, after):
    if before is None:
        return "appeared"
    if after is None:
        return "disappeared"
    if before.inode != after.inode:
        return "replaced"
    if after.size > before.size:
        return f"grew by {after.size - before.size} bytes"
    if after.size < before.size:
        return "shrunk"
    if before.modified != after.modified:
        return "modified with the same size"
    return "same size and modification time"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", default="adb", help="path to the ADB executable")
    parser.add_argument("--serial", help="target device from adb devices -l")
    parser.add_argument("--interval", type=float, default=0, metavar="SECONDS",
                        help="take a second snapshot after this delay")
    args = parser.parse_args()
    if not math.isfinite(args.interval) or args.interval < 0:
        parser.error("--interval must be a finite nonnegative number")

    adb = [args.adb]
    if args.serial:
        adb.extend(["-s", args.serial])
    try:
        directory, before = snapshot(adb, "Initial")
        if args.interval:
            print(f"\nWaiting {args.interval:g} seconds for Hearthstone activity.\n", flush=True)
            time.sleep(args.interval)
            after_directory, after = snapshot(adb, "Follow-up")
            if after_directory != directory:
                raise RuntimeError("The active Android user changed between snapshots.")
            if before or after:
                print("\nChanges")
                for path in sorted(before.keys() | after.keys()):
                    relative = path.removeprefix(directory + "/")
                    change = describe_change(before.get(path), after.get(path))
                    print(f"  {relative}: {change}")
    except (OSError, subprocess.TimeoutExpired, RuntimeError, ValueError) as error:
        print(str(error), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
