# Install the compatibility tools with Nix

Use Nix with `nix-command` and `flakes` enabled. This guide installs the launcher, desktop entry, and native helpers. The package supports `x86_64-linux` with Hyprland's Lua API and the host's `hyprctl` command.

Hearthstone, Battle.net, HDT, and native .NET must already exist in the Steam prefix. The package contains the compatibility plugin's source, not those applications or their account data.

## Install from the checkout

1. Open a terminal in this Git checkout.
2. Build and test the package:

   ```sh
   nix flake check
   ```

3. Install the package in your user profile:

   ```sh
   nix profile add .#default
   ```

4. Prepare a mutable data directory:

   ```sh
   mkdir -p "${XDG_DATA_HOME:-$HOME/.local/share}/hdt-proton"
   ```

5. Copy `hdt.local.example.json` into that directory as `hdt.local.json`.
6. Set `steamAppId` to the existing Battle.net shortcut's ID.
7. Set `steamRoot` if Steam uses another directory.
8. Point the data directory's `app` link at the complete HDT installation's `current/` directory.

If your existing checkout already contains the configuration and installation, reuse that directory instead. Set `HDT_PROJECT_DIR` to its absolute path before each command. A Home Manager configuration can declare that directory for the desktop entry and service.

If a user-profile install does not appear in Vicinae, register the installed entry in its watched user directory:

```sh
mkdir -p "$HOME/.local/share/applications"
ln -s "$HOME/.nix-profile/share/applications/io.github.phibkro.hdt-proton.desktop" "$HOME/.local/share/applications/io.github.phibkro.hdt-proton.desktop"
```

Create the link only if that desktop entry does not already exist. Home Manager creates the entry directly, so that workflow does not need the link.

## Select the optional corrected libXi

Use the updated package from this checkout. Start Battle.net through its existing
Steam shortcut first and set `HDT_PROJECT_DIR` to your existing HDT data directory,
as described above. When HDT is already running, close only HDT before this manual
launch; keep Hearthstone and Battle.net running.

```sh
hdt_runtime=$(nix build .#libxi-steam-runtime --no-link --print-out-paths)
nix run .#default -- libxi-check "$hdt_runtime/lib/libXi.so.6"
HDT_LIBXI_LIBRARY="$hdt_runtime/lib/libXi.so.6" nix run .#default -- raw-launch
```

The library is selected only for this HDT launch and its descendants. The game
prefix and shared Steam libraries are unchanged. `libxi-check` validates the
path and ELF header; the loader and runtime checks establish dynamic compatibility.
The isolated library regression reproduces the defect, but does not prove that
every HDT crash is prevented.

For automatic startup, declare the same absolute `HDT_LIBXI_LIBRARY` path in the
existing HDT service environment through your desktop configuration. Keep that
configuration as its source of truth. To roll back, remove that selection and
restart only HDT; do not replace a shared Steam library.

## Configure Home Manager

Use the module to install the package, register its desktop entry, and declare the watcher service.

1. Add the repository's default branch to your flake inputs:

   ```nix
   inputs.hdt-proton.url = "github:phibkro/hdt-proton-hyprland";
   ```

2. Import the module in your Home Manager configuration:

   ```nix
   imports = [ inputs.hdt-proton.homeManagerModules.default ];
   programs.hdt-proton.enable = true;
   programs.hdt-proton.autoStart = true;
   ```

3. If you reuse an existing data directory, set `programs.hdt-proton.dataDirectory` to its absolute path.
4. If HDT is outside that directory, set `programs.hdt-proton.appDirectory` to the complete application directory.
5. Remove a previous declaration of `hdt-proton-autostart.service` before you import the module. Keep a recovery copy of the old declaration.
6. Apply your Home Manager configuration through your normal deployment process.

The module leaves `hdt.local.json` under your control. It does not copy the file, the game prefix, or account data into the Nix store.

For a user-profile installation without Home Manager, configure `hdt-proton-autostart.service` separately. The desktop Open and Restart actions require that service. The command that the service runs is `hdt-proton watch`. Its environment must select the same data directory as the desktop entry.

## Update an existing Home Manager installation

1. If your input selects `feat/nix-desktop`, change its URL to the default-branch URL above.
2. Update that input from your configuration repository:

   ```sh
   nix flake update hdt-proton
   ```

3. Apply your Home Manager configuration through your normal deployment process.
4. Follow [Apply the plugin](#apply-the-plugin) to compile and install the updated plugin against your HDT installation.

Update the package before rebuilding the plugin. An older pinned package contains older plugin source and can overwrite a manually installed fix. Plugin version 0.5.0 includes the Arena particle workaround. See the [compatibility record](../reference/compatibility.md) for its verification limits.

## Apply the plugin

Finish the current tracking test before you run setup.

1. Start Battle.net through its existing Steam shortcut.
2. Confirm that HDT has created its Roaming settings.
3. Apply the compatibility plugin and settings:

   ```sh
   hdt-proton setup
   ```

4. Start the watcher service:

   ```sh
   systemctl --user start hdt-proton-autostart.service
   ```

5. Open Hearthstone from Battle.net.
6. Run `hdt-proton status` to inspect startup and control updates.

Setup compiles the plugin with the prefix's native .NET compiler. The installed package supplies the native X11 helpers, so startup does not require a C compiler or devenv.

## Open or restart HDT

1. Open your application launcher. On the tested desktop, press **Super+Space** to open Vicinae.
2. Search for **Hearthstone Deck Tracker** or **HDT**.
3. Open the application to focus its existing window.
4. To recover a stalled tracker, choose its **Restart tracker** action.

The equivalent commands are:

```sh
hdt-proton open
hdt-proton restart
```

If Hearthstone is closed, Open starts the watcher and asks you to open the game through Steam. Restart refuses if Hearthstone belongs to the tracker service. If HDT was started outside that service, close HDT normally before you reopen it from the launcher.

## Configure Steam startup

1. Generate the Launch Options line:

   ```sh
   hdt-proton steam-options
   ```

2. Paste the line into the existing Battle.net shortcut's Steam **Launch Options**.
3. Keep `%command%`, the shortcut target, and the selected Proton version unchanged.
4. Confirm that Steam's environment can find `hdt-proton` from your user profile.
5. Run [Test automatic startup](testing.md#test-automatic-startup) on the next launch.

The installed option invokes `hdt-proton steam-run %command%`. It starts the existing service and executes Steam's original arguments.

## Build without installing

Run the package's status command with `nix run`:

```sh
nix run . -- status
```

If you use the checkout's existing mutable data, select it explicitly:

```sh
HDT_PROJECT_DIR="$PWD" nix run . -- status
```

The source dependency pin is `devenv.lock`. The flake reads the locked devenv-nixpkgs source from that file. It does not maintain a second nixpkgs revision in `flake.lock`.
