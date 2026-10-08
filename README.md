# ContinueGame

A client-side Valheim mod that adds a **Continue** button to the main menu.

After your first successful normal connection, the mod remembers the server, character and password. The next time you press Continue, it selects that character and connects to the same server using the saved password. The saved session is updated only after a successful connection and the character appears in the world. A failed attempt does not replace your last successful session.

Throughout the dark wait before Valheim's loading artwork appears, the mod shows **Loading**, a single current-stage label immediately above a continuous gold bar, and a smooth moving glow. The label follows actual operations: selecting the character, restoring the saved password, finding the server, loading the game scene, connecting, submitting the password when required, authenticating and receiving world data. Fast operations can change the label immediately; the connection is never delayed to keep a stage on screen. The bar indicates activity rather than a completion percentage.

The mod panel stays visible through the initial native black fade and connection wait. It disappears when Valheim's world-loading artwork is actually visible. The standard screen keeps its original artwork, logo, tips and loading indicator, with no text or other UI added by ContinueGame. Native error messages and password prompts remain accessible.

## Installation

The only required dependency is **BepInEx 5**, configured for your Valheim version and operating system. The mod does not need to be installed on the server. Password storage is implemented for Windows, Linux and macOS; runtime use on Linux and macOS has not yet been verified. Those systems require a working BepInEx setup for the corresponding game build; a Windows loader package alone does not provide that setup.

1. Close Valheim.
2. Copy the archive's `BepInEx` folder into the game directory or mod manager profile that already contains your `BepInEx` folder. Keep **both** `ContinueGame.dll` and `ContinueGame.dll.config` in `BepInEx/plugins/ContinueGame/`. The configuration file is required on Linux and macOS to load the system library used for file permissions. Remove any older copy of ContinueGame installed in a different folder.
3. Launch the game and connect normally once: select a character and enter the server password. Continue is disabled until the first successful session has been saved.
4. Return to the main menu or restart the game, then press Continue.

If the saved character no longer exists or the password cannot be decrypted, the mod displays a message and leaves normal joining available. Unavailable servers and changed passwords use Valheim's native connection errors. Join normally with the new password to update the saved session.

## Saved data

`BepInEx/config/ContinueGame.last-session.json` stores the last successful session. The mod does not write the password to its log.

- **Windows:** the password is protected with Windows DPAPI and tied to your current Windows account. Saved sessions from earlier development builds remain compatible. Moving to another account or operating system requires a new normal connection.
- **Linux and macOS:** the password is encrypted with AES-256, and HMAC-SHA256 verifies integrity before decryption. A random key is stored separately in `BepInEx/config/ContinueGame.keys/password.key`. Directory permissions are set to `0700`, and file permissions to `0600`. Protection relies on local account permissions rather than an operating system credential vault: anyone who obtains both the session file and the key can decrypt the password. Both files are needed for migration; joining normally again is simpler. A missing or damaged key causes automatic joining to display a warning.

To forget the saved session, close the game and delete the session file. On Linux and macOS, you can also delete the `ContinueGame.keys` directory. The mod does not change your other character or world saves.

Dedicated server addresses, Steam connections and crossplay servers through PlayFab are supported. Automatic joining uses Valheim's native compatibility and access checks.

## Building from source

Use the combined build and local installation script during development. Close Valheim and the Thunderstore application before updating the profile. Overwolf may remain running in the system tray:

```powershell
.\BuildAndInstall.ps1
```

This builds the DLL, creates and validates the complete ZIP with its icon and manifest, and registers the package as a **local mod** in the Thunderstore `Default` profile. Subsequent builds update the same entry without installing a duplicate DLL. If the mod is disabled in the manager, that state is preserved. Previous files and the profile registry are backed up. If the game or manager is open, the archive remains ready and installation stops until they are closed. Nothing is published.

For another existing profile, pass `-Profile 'ProfileName'`. To build only the archive without installing it, run `BuildPackage.ps1`.

You can override the Valheim and BepInEx profile paths using the MSBuild properties `ValheimDir` and `ModProfile`. Game assemblies are used only for compilation and are not included in the package. Decompiled research files under `research` are excluded from both compilation and distribution.

The Thunderstore archive contains `manifest.json`, `README.md`, `CHANGELOG.md` and `icon.png` (256 x 256) at its root, plus both mod files under `BepInEx/plugins/ContinueGame/`. It also supports manual installation. The script validates the package but does not publish it. When uploading to Thunderstore, select the **AI Generated** category.

Author: Slikfoul.
