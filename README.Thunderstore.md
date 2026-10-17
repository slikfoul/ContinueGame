# ContinueGame

Continue your last single-player world or Valheim server session with one click from the main menu.

![Continue button in the Valheim main menu](https://raw.githubusercontent.com/slikfoul/ContinueGame/main/docs/screenshots/continue-button.png)

## Features

- Adds a **Continue** button that selects your saved character and resumes your last single-player world or server.
- Remembers the world or server and character after you successfully enter the game. Server passwords are encrypted.
- Shows **Loading**, the current connection stage and an animated gold bar while waiting for Valheim's loading screen.
- Supports dedicated server addresses, Steam connections and crossplay servers through PlayFab.

## Installation

Requires **BepInEx 5** configured for your Valheim installation. Install the mod on your game client. Runtime compatibility on macOS has not yet been verified.

1. Close Valheim.
2. Copy the archive's `BepInEx` folder into your game directory or existing mod manager profile. The DLL belongs in `BepInEx/plugins/ContinueGame/`.
3. Launch Valheim.

## How to use

Start a single-player world or connect to a server normally once, selecting your character and entering a server password if required. After your character appears in the game, return to the main menu or restart the game and press **Continue**.

The button becomes available once a session has been saved. Each successful start or connection updates the saved session; failed attempts keep the previous one. If the server password changes, connect normally with the new password to save it.

Single-player worlds resume in single-player mode, including worlds stored in Valheim cloud saves. Worlds opened by your own client as a multiplayer server are not remembered. If a saved world or character has been removed, start a game normally to choose a new session.

## Saved data

Saved logins are stored in the local Valheim save directory, under `ContinueGame/<profile-id>/`, separately from character and world files. The path is obtained from Valheim and follows the game's selected local save directory.

On Windows, the default location is `%USERPROFILE%/AppData/LocalLow/IronGate/Valheim/ContinueGame/<profile-id>/`.

The directory contains `last-session.json` and, when saving a server password on Linux or macOS, `keys/password.key`. Single-player sessions do not need a password or encryption key. Each mod manager profile has a separate saved login. The mod uses local files and does not upload credentials through Valheim's cloud save system.

Passwords are encrypted. Windows uses protection tied to your Windows account. Linux and macOS use a separate encryption key. Access to these files follows your system's normal permissions. These protections do not prevent access by software running as your account. Do not share the ContinueGame directory when sharing or backing up game saves.

To forget a saved login, close Valheim and delete its ContinueGame profile directory. After moving to another account or operating system, connect normally to save a new login.

