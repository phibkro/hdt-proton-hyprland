# Incorrect device IDs returned by libXi

A private two-thread test reproduces an incorrect ID returned by libXi 1.8.4.
`XOpenDevice` requests a valid pointer while another thread sends `XNoOp`
requests on the same display. The unmodified library can return a different
`device_id`. No device is removed and no pointer or keyboard input is injected.

## Cause and patch

`XOpenDevice` writes the requested ID into an Xlib output request, waits in
`_XReply`, then reads `req->deviceid` back into the returned `XDevice`.
The reply wait can release the display lock. Another thread can reuse the
output buffer before that read. The patch uses the function's original `id`
argument instead of the request buffer.

The one-line patch is in
[`patches/libxi/0001-retain-open-device-id.patch`](../../patches/libxi/0001-retain-open-device-id.patch).
It applies to official libXi upstream commit
`9ea632ab9dce9ad6630ef7db9d1850ea3d0a2419` (`libXi 1.8.4`). Upstream source is
[on freedesktop.org](https://gitlab.freedesktop.org/xorg/lib/libXi/-/blob/9ea632ab9dce9ad6630ef7db9d1850ea3d0a2419/src/XOpenDev.c).
The patch changes upstream code under its existing Open Group and
Hewlett-Packard license notices. The regression fixture is original project
code under this repository's MIT license.

## Reproduce and verify

```sh
nix build .#checks.x86_64-linux.libxi-regression --print-build-logs --no-link --max-jobs 1 --cores 2
nix path-info .#checks.x86_64-linux.libxi-regression
```

The check builds a patched libXi from the pinned Nix package source and compiles
one fixture linked against the original library. Each invocation selects the
original or patched library with `LD_LIBRARY_PATH`. It runs its own Xvfb server
inside the Nix build sandbox, with no inherited desktop display. It does not
launch Wine, HDT, Hearthstone, or an account session.

Three original-library runs must exit 1 with an ID mismatch. Three patched runs
must complete 50,000 open/close cycles each with zero mismatches. Other failures,
timeouts, or an original run that does not expose the race fail the check.
Because this is a concurrent race, the iteration at failure is not a contract;
the fixture checks requested/returned identity rather than a particular wrong ID.
Raw per-round logs and exit statuses are retained in the Nix build log
(`nix log .#checks.x86_64-linux.libxi-regression`). The output contains only the
fixture executable and canonical result, so scheduling-dependent iteration counts
do not make successful rebuilds differ.

Initial sandbox results: all three original runs failed at iteration zero,
requesting ID 4 and receiving 127. All three patched runs completed normally.
A separate local build with Clang also passed 50,000 patched cycles after an
unmodified-library failure. These are library regression results, not live-game
acceptance or a benchmark.

## Relationship to HDT exits

The desktop native stack identifies Wine's `update_device_mapping` calling
`XGetDeviceButtonMapping` before a fatal `XI_BadDevice`. One capture recorded a
query for ID 18, absent from the independent device inventory, after repeated
queries for IDs 6 and 7. The libXi defect can explain an unexpected ID without
any device removal. However, a live requested-ID/returned-ID comparison and a
private patched Proton/HDT run are still needed to establish that it caused
that particular desktop exit.

This patch does not handle real device disappearance between open and query.
A narrowly scoped Wine error guard remains a separate robustness improvement.
The installed Steam runtime, Proton files, game prefix, and HDT launcher are
unchanged. The patched library is used only by the isolated check.

## Upstream submission draft

Target: [libXi merge requests](https://gitlab.freedesktop.org/xorg/lib/libXi/-/merge_requests),
following [X.Org's patch workflow](https://www.x.org/wiki/Development/Documentation/SubmittingPatches/).
The source repository was read successfully through Git. The author reviewed
the Developer Certificate of Origin and authorized the patch's `Signed-off-by`
certification on 16 November 2026. The signed patch was sent as plain text to
`xorg-devel@lists.x.org` that day, using X.Org's documented email workflow.
Its subject is `[PATCH libXi] XOpenDevice: retain the requested ID across the reply wait`.
The mail service confirmed it was sent; mailing-list acceptance and maintainer
review remain pending. No GitLab merge request has been created.

Suggested title: **XOpenDevice: retain the requested ID across the reply wait**.

Suggested description:

> A concurrent caller can overwrite Xlib's output request buffer while
> XOpenDevice waits in _XReply. Reading req->deviceid after the wait can return
> an unrelated device ID. Use the original id argument instead.
>
> A real two-thread Xvfb reproducer issues XNoOp requests on the same display
> while repeatedly opening a valid pointer. With libXi 1.8.4, three runs returned
> ID 127 for requested ID 4 at their first iteration. The patched library passed
> three runs of 50,000 open/close cycles. No device removal or input injection is
> involved. The patch applies to upstream 9ea632ab9dce9ad6630ef7db9d1850ea3d0a2419.
>
> Investigated after a Wine application exited in XGetDeviceButtonMapping with
> XI_BadDevice. The library reproducer is independent of Wine; live application
> acceptance remains pending.
