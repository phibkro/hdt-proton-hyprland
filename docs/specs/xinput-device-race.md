# Fatal XInput mapping errors

Repeated observed HDT exits on Proton Experimental ended with `XI_BadDevice`,
request `X_GetDeviceButtonMapping`, and process status 1. The in-process recorder
stopped without a managed exception. These exits are distinct from UI freezes.

## A device can disappear after it opens

The isolated fixture opens a virtual pointer and successfully reads its button
mapping. The fixture then removes the pointer and repeats the mapping query.
Xlib's default error handler terminates the process with the same error class,
request name, and exit status as the observed HDT exits.

A scoped test handler accepts only `XI_BadDevice` for the expected display,
request serial, extension opcode, and mapping request. The query then returns
zero instead of terminating the fixture. A valid device still returns its map.
An unrelated window error, a mapping error with another serial, and `BadMatch`
remain fatal. This handler is a single-threaded test demonstration. It is not
installed in HDT, Wine, or the desktop.

The error's printed device value does not identify the removed pointer in this
fixture. The actual pointer ID is 10, while the error reports another value.
The desktop's reported `0x158` therefore does not establish which device failed.

The fixture demonstrates a possible failure mechanism. It does not establish
that device removal triggered the observed desktop crashes. Those crashes have
no native call stack or device-lifecycle trace.

## The installed driver has an unguarded query

LLVM disassembly of the installed Experimental `winex11.so` shows
`update_device_mapping` calling `XOpenDevice`, allocating a map, and calling
`XGetDeviceButtonMapping` without a scoped error check. The separate tablet
mapping path calls `X11DRV_check_error` after its mapping query.

The inspected ELF has SHA-256
`26fd052aeed349cd85b8b81e3b4987f75752663d13c2755edb199411d365d7e5`.
This establishes the local call sequence, not a complete match between the
installed driver and an upstream source revision.

The corresponding source pattern is visible in Valve Wine's
[mouse.c at 5b557605b4e51f0853ec8e0e4f0eb536c669fae3](https://github.com/ValveSoftware/wine/blob/5b557605b4e51f0853ec8e0e4f0eb536c669fae3/dlls/winex11.drv/mouse.c).
Wine already provides scoped error handling in
[x11drv_main.c](https://github.com/ValveSoftware/wine/blob/5b557605b4e51f0853ec8e0e4f0eb536c669fae3/dlls/winex11.drv/x11drv_main.c).
The [libXi specification](https://xorg.freedesktop.org/releases/current/doc/libXi/inputlib.html)
documents both `BadDevice` and `BadMatch` for this query.

The correct integration point is Wine's existing error-handling mechanism.
A driver fix must account for device disappearance during open, query, and close,
and avoid publishing an invalid mapping. It must also preserve unrelated errors.
A matching Wine build and runtime test are required before deployment. A global
X11 error suppressor would hide failures outside this operation.

## Isolation and limits

`scripts/check-xinput.sh` copies the fixture into a new evidence directory,
compiles it, and runs it through Bubblewrap. The namespace contains the Nix
store, a private temporary directory, and the capture directory. Host desktop
sockets, the game prefix, home directories, and inherited credentials are absent.
The fixture creates its own Xvfb server and uses only that server's display.

Source and binary hashes accompany the results. Each case retains stdout,
stderr, and exit status. The server has a startup deadline, each fixture has an
execution deadline, and cleanup stops the owned server.

This is an Xlib and libXi test on Xvfb. It does not run the installed Wine driver,
HDT, XWayland, HearthMirror, or ArenaSmith. The code uses public X11 APIs and copies
no upstream implementation. The project-owned fixture uses the repository's MIT
license. Any future Wine source patch must retain Wine's LGPL provenance.

REA's ELF provider was unavailable during this investigation. Installed LLVM
tools supplied the symbol and disassembly evidence. No REA result is claimed
for the native call sequence.

## Further desktop incidents and independent capture

Two additional native error records on 16 November 2026, at 18:24:16 and
18:26:40 Europe/Oslo, report the same error and mapping request. Neither adds a
native stack or establishes a click-triggered device race. Original records
and recorder sessions are retained privately, outside the source tree.

The independent Rust `xinput-watch` command uses the existing x11rb dependency
to subscribe only to hierarchy/device-change events. It cannot catch another
process's Xlib error or change Wine's mapping behavior. The private acceptance
journey adds/removes devices through the existing project-owned C fixture,
then lets that separate client exit through Xlib's default fatal handler. The
observer must remain alive, retain the device events and end normally.
See [capture instructions](../guides/freeze-capture.md#native-xinput-exits).

## Observed Wine caller stack

The 16 November 2026 19:04:44 Europe/Oslo exit has a native caller stack,
captured before Xlib terminated HDT. The recorded path is:

```text
X11DRV_GenericEvent
  → update_device_mapping
  → XGetDeviceButtonMapping
  → _XReply / _XError
  → Wine error_handler
  → _XDefaultError
```

This directly identifies the failing Wine function, rather than inferring it
from the standalone fixture. The loaded `winex11.so` hash matches the inspected
ELF above. Installed version metadata reads
`experimental-11.0-20261112-x86_64`. Matching that build to a source revision
remains separate work.

Independent observation near the failure recorded `DeviceChanged` source
switches between two device IDs. No nearby hierarchy-removal event was observed
by that capture. The recorder's clock is userspace receipt time, and the native
error journal is written after debugger handling: adjacency alone does not prove
which event caused the request. Device removal is therefore still a possible
fixture mechanism, not the established live cause.

After saving the stack, GDB 17.2 itself aborted while printing shared libraries,
with `find_debug_base_for_solib: solib->objfile != nullptr`. The stack and GDB
core are preserved privately. This debugger assertion is separate from the
already intercepted Wine/Xlib error; no complete-capture marker was reached.
The capture now avoids `info sharedlibrary` and saves `/proc` mappings outside
the debugger. A matching-driver private reproducer and a scoped Wine fix are
still unverified.
