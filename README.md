# ContinueGame

Continue your last Valheim server session with one click from the main menu.

## Features

- Adds a **Continue** button that selects your saved character and reconnects to your last server.
- Remembers the server, character and encrypted password after a successful connection.
- Shows **Loading**, the current connection stage and an animated gold bar while waiting for Valheim's loading screen.
- Supports dedicated server addresses, Steam connections and crossplay servers through PlayFab.

## Installation

Requires **BepInEx 5** configured for your Valheim installation. Install the mod on your game client. Runtime compatibility on Linux and macOS has not yet been verified.

1. Close Valheim.
2. Copy the archive's `BepInEx` folder into your game directory or existing mod manager profile. Keep both `ContinueGame.dll` and `ContinueGame.dll.config` together in `BepInEx/plugins/ContinueGame/`.
3. Launch Valheim.

## How to use

Connect to a server normally once, selecting your character and entering the password if required. After a successful connection, return to the main menu or restart the game and press **Continue**.

The button becomes available once a session has been saved. Each successful connection updates the saved session; failed attempts keep the previous one. If the server password changes, connect normally with the new password to save it.

## Saved data

The last session is stored in `BepInEx/config/ContinueGame.last-session.json`.

Passwords are encrypted on Windows, Linux and macOS. On Windows, protection is tied to your Windows account. On Linux and macOS, the encryption key is stored separately in `BepInEx/config/ContinueGame.keys/password.key` with access restricted to your account. Keep the session file and key private: together they allow the password to be decrypted.

To forget the saved session, close Valheim and delete the session file. On Linux and macOS, you can also delete the `ContinueGame.keys` directory. After moving to another account or operating system, connect normally to save a new session.

## Building from source

Use the combined build and local installation script during development. Close Valheim and the Thunderstore application before updating the profile. Overwolf may remain running in the system tray:

```powershell
.\BuildAndInstall.ps1
```

This builds the DLL, creates and validates the complete ZIP with its icon and manifest, and registers the package as a **local mod** in the Thunderstore `Default` profile. Subsequent builds update the same entry without installing a duplicate DLL. If the mod is disabled in the manager, that state is preserved. Previous files and the profile registry are backed up. If the game or manager is open, the archive remains ready and installation stops until they are closed. Nothing is published.

For another existing profile, pass `-Profile 'ProfileName'`. To build only the archive without installing it, run `BuildPackage.ps1`.

You can override the Valheim and BepInEx profile paths using the MSBuild properties `ValheimDir` and `ModProfile`. Game assemblies are used only for compilation and are not included in the package. Decompiled research files under `research` are excluded from both compilation and distribution.

Author: Slikfoul.
