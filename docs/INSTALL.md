# Apply HDT compatibility fixes

Use this guide to apply the plugin to an existing HDT installation and start HDT alongside Hearthstone.

## Prepare the installation

Before you start, confirm that you have these prerequisites:

- Nix and devenv.
- Native Steam with the existing Battle.net shortcut and Hearthstone installation.
- Hyprland with its Lua API and XWayland.
- Native Microsoft .NET Framework 4.8 in the game's prefix.
- A complete HDT installation with its companion files and Velopack updater.

Keep the existing Steam shortcut and Proton selection. The [tested environment](COMPATIBILITY.md#tested-environment) uses Proton Experimental. For a version comparison, use [Test stable Proton](TESTING.md#test-stable-proton).

This guide starts after HDT and .NET installation. For the installation requirements, see the [HDT installation reference](REFERENCE.md#hdt-installation).

1. Open a terminal in the repository checkout.
2. If `hdt.local.json` does not exist, copy the example:

   ```sh
   cp hdt.local.example.json hdt.local.json
   ```

3. Set `steamAppId` in `hdt.local.json` to the numeric ID of the existing Battle.net Steam shortcut.
4. If Steam uses another directory, set `steamRoot` to its absolute path. See [Configuration](REFERENCE.md#configuration).
5. If `app` already points to the complete HDT installation, keep that link. Otherwise, set `HDT_APP_DIR` to its `current/` directory before each command:

   ```sh
   export HDT_APP_DIR="$PWD/managed/current"
   ```

   Use that command only when `managed/current` contains your installed HDT application.

## Apply the plugin

Setup can close HDT. Finish any tracking test before you run setup.

1. Start Battle.net through the existing Steam shortcut.
2. If HDT has not created its Roaming settings, start HDT once:

   ```sh
   HDT_DIRECT=1 devenv shell -- hdt-launch
   ```

3. Leave HDT open or close HDT normally.
4. Apply the compatibility plugin and settings:

   ```sh
   devenv shell -- hdt-setup
   ```

   On success, the command prints `Compatibility setup complete.` followed by the launch command. If HDT refuses to exit, close HDT manually and retry. Setup does not force-stop HDT.

5. Start HDT with the compatibility settings:

   ```sh
   devenv shell -- hdt-overlay-launch
   ```

6. Start Hearthstone from Battle.net when you are ready.
7. Run the [desktop checks](TESTING.md#test-the-overlay).

## Start HDT from the Steam shortcut

This procedure requires `hdt-proton-autostart.service` in your desktop session. The tested machine's Home Manager declaration is in [homelab PR #92](https://github.com/phibkro/homelab/pull/92).

1. Confirm that the desktop service exists:

   ```sh
   systemctl --user status hdt-proton-autostart.service
   ```

   If the unit is missing, configure the service before you continue. The Steam wrapper does not install a service.

2. Generate the Launch Options line for your checkout:

   ```sh
   devenv shell -- hdt-steam-options
   ```

3. In Steam, open the existing Battle.net shortcut's **Properties**.
4. Paste the generated line into **Launch Options**. Keep `%command%` in the line.
5. If you already use a wrapper, preserve that wrapper around `%command%`.
6. Keep the shortcut target, app ID, and compatibility selection unchanged.
7. On the next launch, run [Test automatic startup](TESTING.md#test-automatic-startup).

Use Steam's **Launch Options** field. Battle.net's per-game arguments go to Hearthstone and cannot start the host service.

## Remove automatic startup

To remove the Steam integration, delete the wrapper from the shortcut's **Launch Options**.

To stop desktop startup, follow these steps outside play:

1. Disable the service declaration in your desktop configuration. On the tested machine, edit the Home Manager declaration.
2. Stop the service:

   ```sh
   systemctl --user stop hdt-proton-autostart.service
   ```

The service can own HDT's processes. Stopping the service can close HDT.

For plugin removal, see [Remove the compatibility plugin](DEVELOPMENT.md#remove-the-compatibility-plugin).
