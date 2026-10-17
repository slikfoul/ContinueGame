# Single-player support 1.0.3

- Tracks an accepted single-player start through the native ZNet.SetServer call and writes the session only after the matching world and local player are present. Player-hosted multiplayer worlds are not captured.
- Stores the world save name and UID alongside the character. Continue finds the existing world through SaveSystem.GetWorldList and calls the native OnWorldStart path with multiplayer toggles disabled. It never creates a missing world or modifies world/character save files itself.
- Native save-capacity warnings remain accessible: the local loading overlay starts only when the native scene transition begins. Native artwork handoff remains unchanged.
- Existing 1.0.2 server sessions remain compatible without migration. Single-player records have an empty password and need no encryption key; server encryption and private-storage location are unchanged.
- Release build succeeded with zero errors. 110 automated checks passed on Windows, including local serialization, world identity, failed-update preservation, server/solo switching, old server JSON and installed-game menu/SetServer signatures.
- The 1.0.3 ZIP passed version, icon, changelog, exact-entry and content-hash validation. Repeat registration in a disposable profile preserved configuration, unrelated entries, disabled DLL state and separate registry/cache trust values.
- The real Default profile remains on 1.0.2 because r2modman is running. No live profile changes were made for 1.0.3.
- 110 checks also passed on Ubuntu 22.04 WSL2, using .NET 10 for the isolated test runner and native Linux temporary files. Production algorithms were compiled from the same source; package DLL imports were inspected. Linux AES/HMAC, key creation/reload, session persistence, atomic replacement and solo/server switching passed. Windows DPAPI checks stay Windows-only.
- The Linux checks were repeated against the downloaded native Linux Valheim assemblies; all 110 passed. Native Linux Valheim was then started under WSLg/OpenGL with BepInEx 5.4.23.5, ContinueGame 1.0.3, Seasons 1.10.5, ConditionalConfigSync 1.0.5 and JsonDotNET 13.0.4. The log confirms Linux, successful plugin loading and creation of the Continue button.
- The clean Ubuntu install initially lacked libpulse-mainloop-glib.so.0, required by the game's Party library. Installing Ubuntu's libpulse-mainloop-glib0 resolved all native Party dependencies. After the user closed the game and it was restarted, the Party exception was absent and PlayFab login completed.
- On 2026-10-18, the Linux single-player functional check passed under Ubuntu 22.04 WSLg/XWayland: a normal solo launch saved the session and character, Continue became available after returning to the menu, and the user confirmed that Continue opened the same world with the same character. Logs confirm both successful session writes and the native loading-artwork handoff; the private last-session.json file exists on the Linux filesystem.
- WSLg rendering/input problems remain a separate test-environment limitation. Flickering was reproduced without mods; the user reported that resetting the game's graphics settings removed it. Native Wayland still had low FPS, and mouse capture remained problematic. This test does not establish compatibility with every Linux desktop or graphics stack.
- The user also confirmed another successful local Continue after the complete game-process restart. The new process log confirms ContinueGame startup and a successful local Continue/session write. Cloud-world launch still requires a separate functional check. macOS runtime remains untested.
- A subsequent ordinary Linux login to the user's separately running Steam server succeeded after a WSL-specific copy-performance workaround. ContinueGame logged a successful server session/character write; private file metadata confirms a nonempty session record and creation of the 64-byte portable encryption key. Password and key contents were not inspected. The user then confirmed that Continue rejoined the same server with the same character without prompting for the password. Server Continue after another complete process restart remains a separate follow-up check.
- The user additionally confirmed both local Continue and remote-server Continue in the successful Linux test run. After the game was closed, the verified per-process glibc copy threshold was added to the local WSL Steam launcher with an original-script backup and shell-syntax validation. The temporary diagnostic DLL was moved outside the plugin directory and its backup hash verified. No runtime diagnostic plugin is distributed in the mod ZIP. A further launch through the persisted wrapper has not yet been observed.
- The ordinary Steam-library launch in this WSL installation used the soldier container runtime, fell back to llvmpipe and did not load BepInEx. A separate local WSL launcher now reproduces the working host OpenGL/BepInEx launch through Valheim's Steam LaunchOptions; the original Steam configuration is backed up under the Linux test-state directory. A new launch requested through Steam confirmed the RTX 3080 renderer and ContinueGame startup. This WSL-specific launcher is not part of the distributed mod package; visual readiness and resume after this launch are checked separately.
- Final release verification reran all 110 Windows checks. A fresh build and the Linux-tested DLL produced identical complete IL disassembly. The release uses the exact Linux-tested DLL bytes; temporary WSL launchers and diagnostic modules remain excluded from the package. The README no longer labels Linux runtime support unverified; macOS remains unverified.

# Storage fix 1.0.2

- Saved sessions and portable encryption keys use ContinueGame/<profile-id> under the local Valheim save directory, outside the mod profile. The path comes from Utils.GetSaveDataPath(FileSource.Local), resolved after menu initialization.
- Only the local ContinueGame store is read and written. Old profile credentials are not loaded, migrated, modified or deleted. A normal successful login initializes the private store.
- No ContinueGame.Posix alias, Unix native library import, chmod call or assembly dllmap configuration remains. Only Windows DPAPI native imports remain. Unix data files use normal OS permissions; owner-only mode is not enforced or claimed.
- Release DLL builds successfully. 82 automated checks passed on Windows, including first-run behavior, ignoring old profile credentials, preserving old files byte-for-byte and native-import metadata.
- Package validation passed: five files, matching manifest/plugin/assembly versions, public changelog, 256x256 PNG, player-only README and content hashes.
- Linux/macOS runtime has not been tested. The updated mod still requires in-game verification.
- The user uploaded version 1.0.2 to Thunderstore. The downloaded archive matches the local release ZIP byte-for-byte (SHA-256: FFE6DF0B26CAD91AD2373B212C48364D3E1B6BA7B5974E4FEA7A29E360E73CF2).

# Проверки релиза 1.0.0

## Причина и подтверждение

Просмотрена предоставленная пользователем запись локально, без отправки видео во внешние сервисы. На кадрах панель мода есть примерно до 13-й секунды; около 14-й она пропадает, а штатная картинка появляется около 28-й. Между ними остаётся примерно 14 секунд пустого экрана. Журнал подтверждает загрузку ContinueGame 0.2.1 и отдельные события перехода в основную сцену, начала respawn и появления персонажа.

В Hud.UpdateBlackScreen штатный CanvasGroup включается уже при отсутствии персонажа, а ветка world loading и m_loadingProgress включаются позже, при Game.WaitingForRespawn. Прежняя проверка active/alpha считала первое затемнение появлением картинки и скрывала панель раньше времени.

## Исправление

Панель закрывается только при одновременном выполнении условий:

- Штатный CanvasGroup активен и видим.
- Включён штатный m_loadingProgress для загрузки мира.
- Штатное изображение существует, включено, содержит sprite, имеет ненулевую прозрачность цвета, находится на активном Canvas и имеет ненулевую наследуемую прозрачность CanvasRenderer.

До этого остаются Loading, один текущий этап над прежней плавной полосой. Истории выполненных шагов нет. На штатный экран не добавляются текст, дочерние объекты или другие элементы; картинка, логотип, подсказки и индикатор игры не изменяются. После передачи управления панель не возвращается.

Во второй тёмной паузе текущий этап поступает из ZNet.ClientConnect, RPC_ClientHandshake, SendPeerInfo и RPC_PeerInfo: соединение, передача пароля при необходимости, проверка входа и получение данных мира. После Connected возможна подпись загрузки области, пока сама картинка ещё не видима. Таймер не имитирует смену стадий и проценты; ввод пароля и штатные предупреждения остаются доступны.

## Проверки

- Release DLL собрана без ошибок. Предупреждение NU1900 связано с недоступностью онлайн-проверки уязвимостей NuGet; пакеты и игровые зависимости не менялись.
- Пройдены 65 автоматических проверок на Windows, включая шифрование и совместимость сохранений, повреждённые данные/ключи, атомарное сохранение, наличие используемых игровых методов/полей, отсутствие System.ValueTuple и Jotunn.
- Регрессионные проверки повторяют границу из записи: неактивная группа, нулевая прозрачность, полностью непрозрачное штатное затемнение без world-loading, включённая загрузка мира без видимой картинки — панель остаётся; фактическая готовность картинки закрывает её; поздние события не возвращают панель.
- Проверено, что текущий этап продолжает меняться во второй тёмной паузе, а запоздалые события не возвращают его назад.
- Thunderstore ZIP проверен по структуре и хешам: шесть файлов, совпадающие версии, PNG 256x256, оба Unix dllmap. README на английском языке, без номера версии и раздела проверок.
- Видео, извлечённые кадры и локальный декодер находятся только в исключённом каталоге research. В архив не входят пользовательские данные, ключи, игровые библиотеки или исследовательские файлы.

## Проверка в игре

После установки версии 0.2.2 в Default пользователь проверил вход в Valheim и подтвердил, что переход теперь соответствует ожидаемому поведению. Это пользовательская проверка в игре; автоматические проверки и анализ записи предыдущей сборки описаны отдельно выше.

Linux и macOS не запускались. Криптографический код проверен на Windows; системные вызовы и загрузка на этих ОС требуют отдельной проверки.

## Установка и история

BuildAndInstall.ps1 собирает DLL и полный пакет, обновляет локальную запись ContinueGame в Default, проверяет установленную версию, иконку, кэш и файлы по хешам. Установка требует закрытых Valheim и Thunderstore; фоновый Overwolf разрешён. Предыдущие файлы и реестр сохраняются для отката.

Версия пакета первого публичного релиза — 1.0.0. Игровое поведение совпадает с проверенной пользователем версией 0.2.2; изменены номер версии и согласованные метаданные пакета. Полная история разработки сохранена в CHANGELOG.md. Черновик публичного журнала описывает конечное поведение без нулевых версий. Публикация GitHub и последующая загрузка в Thunderstore разрешены пользователем. Release DLL и финальный архив 1.0.0 проверены, повторно пройдены 65 автоматических проверок на Windows; пакет зарегистрирован в Default. Пользовательский скриншот кнопки добавлен в README. Статус публикации фиксируется отдельно после завершения.
