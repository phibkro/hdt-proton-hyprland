# X11 link helpers in the selected Steam namespace

Keep control-input on the host so its Hyprland IPC/runtime/PATH remain usable.
Only overlay-link apply/restore subprocesses enter the same validated matching-
prefix Proton wineserver namespace as HDT; these helpers use X11 without hyprctl.
A host X socket is not equivalent to Steam's private X socket/authentication path.

Reuse one namespace command constructor for raw HDT and scoped X11 helpers.
The launcher passes controller nonce, selected wineserver PID and Linux start
identity explicitly. Controller startup and every apply/restore revalidate the
prefix, Proton executable, deleted-fsync guard and start identity. Each X11 helper
uses selected-session DISPLAY and optional XAUTHORITY path; missing/empty DISPLAY
fails clearly, and absent authority removes inherited host authority. Never read
cookie contents. Host controller environment, Hyprland IPC, runtime, nonce and
HDT-only libXi selection remain unchanged. No host socket/global configuration
change is permitted. Existing launcher helper cleanup retains lifecycle custody.

A numeric nsenter target still has a check-to-exec lifetime race; these checks
reject observed replacement and do not claim an atomic kernel process handle.
Tests use owned synthetic proc fixtures and command/environment projections:
shared flags/target, stale host display/auth override, absent-auth removal,
nonce and unrelated caller settings preserved, prefix/start identity rejection,
and missing-display refusal. They do not establish actual Steam connectivity.
Root owns real same-session X11 apply/restore, controller status/input and cleanup
acceptance. This lane launches no live namespace/app/service actions.

Only absent input-region.json is normal waiting at its read boundary. Missing
commands/session resources and malformed input must remain visible failures;
the controller must not globally suppress nested NotFound errors.
