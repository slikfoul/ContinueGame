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
