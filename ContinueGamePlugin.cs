using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using Splatform;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ContinueGame
{
    [BepInPlugin(PluginId, "ContinueGame", "1.1.0")]
    public sealed class ContinueGamePlugin : BaseUnityPlugin
    {
        public const string PluginId = "Slikfoul.ContinueGame";
        internal static ContinueGamePlugin Instance;
        private static readonly FieldInfo PasswordField = AccessTools.Field(typeof(FejdStartup), "<ServerPassword>k__BackingField");
        private Harmony _harmony;
        private PrivateSessionStorage _storage;
        private SessionRecord _saved;
        private SessionRecord _pending;
        private string _pendingPassword;
        private bool _passwordInjected;
        private string _previousPassword;
        private Button _button;
        private FejdStartup _menu;
        private bool _busy;
        private bool _loadingScheduled;
        private bool _waitingForHostLogin;
        private SessionRecord _labelSession;
        private string _labelCaption;
        private float _labelWidth = -1f;
        private TMP_Text[] _tmpLabels;
        private UnityEngine.UI.Text[] _textLabels;
        private TMP_Text _detailsText;
        private ContinueButtonHover _buttonHover;
        private string _characterDisplayName;
        private RectTransform _buttonRect;
        private readonly Vector3[] _buttonCorners = new Vector3[4];
        private const float DetailsGap = 22f;
        private CrossPlatformPasswordProtection _passwordProtection;
        private ContinueLoadingScreen _loadingScreen;
        private readonly ConnectionAttempts _attempts = new ConnectionAttempts();
        private SessionRecord _retryTarget;
        private string _retryPassword;
        private bool _retryPasswordAvailable;

        private bool Russian => Localization.instance != null && Localization.instance.GetSelectedLanguage() == "Russian";
        private string Text(string ru, string en) => Russian ? ru : en;

        private void Awake()
        {
            Instance = this;
            if (PasswordField == null) { Logger.LogError("Required Valheim password integration is unavailable."); return; }
            _harmony = new Harmony(PluginId);
            _harmony.PatchAll(typeof(ContinueGamePlugin).Assembly);
            Logger.LogInfo("ContinueGame loaded.");
        }

        private void InitializeStorage()
        {
            _saved = null;
            _storage = null;
            _passwordProtection = null;
            try
            {
                // Resolve after the menu has initialized the game's save settings.
                // Never select Auto/Cloud storage for credentials.
                string localSavePath = Utils.GetSaveDataPath(FileHelpers.FileSource.Local);
                if (string.IsNullOrWhiteSpace(localSavePath)) throw new IOException("Local save directory is unavailable.");
                _storage = new PrivateSessionStorage(Paths.ConfigPath, Path.GetFullPath(localSavePath));
                _passwordProtection = _storage.PasswordProtection;
                _saved = _storage.Load();
            }
            catch (Exception) { Logger.LogWarning("Saved session could not be read. Join normally to save a new session."); }
        }

        private void OnDestroy()
        {
            CancelPending();
            _harmony?.UnpatchSelf();
            if (Instance == this) Instance = null;
        }

        internal void CancelPending()
        {
            ClearAttempt();
            _attempts.Cancel();
            _retryTarget = null;
            _retryPassword = null;
            _retryPasswordAvailable = false;
        }

        private void ClearAttempt()
        {
            _loadingScreen?.Dispose();
            _loadingScreen = null;
            RestorePassword();
            _pending = null;
            _pendingPassword = null;
            _busy = false;
            _loadingScheduled = false;
            _waitingForHostLogin = false;
        }

        private void RestorePassword()
        {
            if (!_passwordInjected) return;
            PasswordField.SetValue(null, _previousPassword);
            _previousPassword = null;
            _passwordInjected = false;
        }

        private void Update()
        {
            UpdateRetry();
            if (_waitingForHostLogin && _menu != null && !UnifiedPopup.IsVisible() &&
                !_menu.IsInvoking("LoadMainSceneIfBackendSelected")) CancelPending();
            if (_loadingScreen != null)
            {
                try
                {
                    _loadingScreen.Tick();
                    if (_loadingScreen.IsFinished)
                    {
                        Logger.LogInfo("Native loading artwork is ready; Continue preloading panel hidden.");
                        _loadingScreen = null;
                    }
                }
                catch (Exception)
                {
                    _loadingScreen.Dispose();
                    _loadingScreen = null;
                    Logger.LogWarning("Loading visuals could not be updated; connection will continue normally.");
                }
            }
            if (_button != null)
            {
                string caption = Text("Продолжить", "Continue");
                if (_labelSession != _saved || _labelCaption != caption)
                {
                    _labelSession = _saved;
                    _labelCaption = caption;
                    foreach (TMP_Text text in _tmpLabels) text.text = caption;
                    foreach (UnityEngine.UI.Text text in _textLabels) text.text = caption;
                    if (_detailsText != null) _detailsText.text = ResumeLabel.Details(_saved, _characterDisplayName);
                    _labelWidth = -1f;
                }
                // Excluded from the native button's horizontal layout and content-size fitting.
                Canvas canvas = _button.GetComponentInParent<Canvas>();
                float width = 600f;
                if (canvas != null)
                {
                    RectTransform canvasRect = canvas.rootCanvas.GetComponent<RectTransform>();
                    _buttonRect.GetWorldCorners(_buttonCorners);
                    float right = canvasRect.InverseTransformPoint(_buttonCorners[3]).x;
                    float scale = canvasRect.lossyScale.x / Mathf.Max(_buttonRect.lossyScale.x, 0.01f);
                    width = Mathf.Min(720f, (canvasRect.rect.xMax - right - 32f) * scale - DetailsGap);
                }
                width = Mathf.Max(120f, width);
                if (!Mathf.Approximately(width, _labelWidth))
                {
                    if (_detailsText != null)
                    {
                        float height = string.IsNullOrEmpty(_detailsText.text) ? 0f :
                            Mathf.Ceil(_detailsText.GetPreferredValues(_detailsText.text, width, Mathf.Infinity).y) + 8f;
                        _detailsText.rectTransform.sizeDelta = new Vector2(width, height);
                    }
                    _labelWidth = width;
                }
                _button.interactable = _saved != null && !_busy && !_attempts.Active;
                if (_detailsText != null)
                    _detailsText.gameObject.SetActive(_button.interactable && _buttonHover != null &&
                        _buttonHover.IsHovered && !string.IsNullOrEmpty(_detailsText.text));
            }
            if (_pending != null && _menu != null)
            {
                if (_menu.IsInvoking("LoadMainSceneIfBackendSelected")) _loadingScheduled = true;
                else if (_loadingScheduled && !_menu.m_loading.activeSelf)
                {
                    // The native DNS/backend retry loop returned to the menu before ZNet was created.
                    if (_pending.IsWorld) CancelPending();
                    else ConnectionFailed(ZNet.GetConnectionStatus());
                }
            }
            if (_pending == null || ZNet.instance == null) return;
            if (_pending.IsWorld)
            {
                // A solo game is a local ZNet server, not a connected remote client.
                World world = ZNet.GetWorldIfIsHost();
                if (!ZNet.instance.IsServer() || world == null || !_pending.MatchesWorld(world.m_worldName, world.m_uid) ||
                    ZNet.IsOpenServer() != _pending.StartServer) return;
                _loadingScreen?.Advance(LoadingStage.LoadingArea);
            }
            else
            {
                if (ZNet.instance.IsServer()) return;
                ZNet.ConnectionStatus status = ZNet.GetConnectionStatus();
                if (status > ZNet.ConnectionStatus.Connected)
                {
                    ConnectionFailed(status);
                    return;
                }
                if (status == ZNet.ConnectionStatus.Connected) _loadingScreen?.Advance(LoadingStage.LoadingArea);
                if (status != ZNet.ConnectionStatus.Connected) return;
            }
            if (Game.instance == null || Player.m_localPlayer == null) return;
            try
            {
                PlayerProfile profile = Game.instance.GetPlayerProfile();
                if (profile == null) return;
                _pending.CharacterFilename = profile.GetFilename();
                _pending.CharacterId = profile.GetPlayerID();
                _pending.CharacterName = profile.GetName();
                if (!_pending.IsWorld)
                    _pending.ServerName = ResumeLabel.ServerName(_pending, _saved, FindServerName(RestoreServer(_pending)));
                _storage.Save(_pending, _pendingPassword ?? "");
                _saved = _pending;
                Logger.LogInfo("Last successful session and character saved.");
            }
            catch (Exception) { Logger.LogWarning("The successful session could not be saved. The previous session was kept."); }
            finally { CancelPending(); }
        }

        internal void CaptureLocalWorld(bool server, bool openServer, bool publicServer, string password, World world)
        {
            if (!server || world == null || world.m_menu || _menu == null) return;
            if (world.m_dataError != World.SaveDataError.None || world.m_uid == 0 || string.IsNullOrWhiteSpace(world.m_worldName)) return;
            if (_attempts.Active) CancelPending();
            RestorePassword();
            _waitingForHostLogin = false;
            _pending = new SessionRecord
            {
                ServerKind = openServer ? "HostedWorld" : "LocalWorld", WorldName = world.m_worldName, WorldId = world.m_uid,
                StartServer = openServer, CommunityServer = openServer && publicServer,
                Crossplay = openServer && _menu.m_crossplayServerToggle.isOn
            };
            // This is the native plaintext argument before ZNet hashes it. Encrypt only after successful entry.
            _pendingPassword = openServer ? password ?? "" : "";
            Logger.LogInfo(openServer ? "Tracking a player-hosted world start." : "Tracking a single-player world start.");
        }

        internal void CaptureJoin(FejdStartup startup)
        {
            try
            {
                ServerJoinData server = startup.GetServerToJoin();
                if (!server.IsValid) return;
                SessionRecord record = RecordServer(server);
                // Invites or ordinary joins must not inherit an earlier Continue retry sequence.
                if (_attempts.Active && (!_busy || !record.MatchesServer(_retryTarget))) CancelPending();
                _pending = record;
                _pending.ServerName = ResumeLabel.ServerName(_pending, _saved, FindServerName(server));
                if (!_passwordInjected) _pendingPassword = null;
                Logger.LogInfo("Tracking a server connection attempt.");
            }
            catch (Exception) { CancelPending(); Logger.LogWarning("Could not track the connection; ordinary joining remains available."); }
        }

        internal void CapturePassword(ZNet network, string password)
        {
            if (_pending == null || network.IsServer()) return;
            _pendingPassword = password ?? "";
            if (_attempts.Active)
            {
                _retryPassword = _pendingPassword;
                _retryPasswordAvailable = true;
                _attempts.PasswordEntered();
            }
            RestorePassword();
        }

        private static SessionRecord RecordServer(ServerJoinData server)
        {
            var record = new SessionRecord { ServerOwner = server.m_owner.IsValid ? server.m_owner.ToString() : null };
            switch (server.m_type)
            {
                case ServerJoinDataType.Dedicated:
                    record.ServerKind = "Dedicated";
                    record.Address = server.Dedicated.m_host;
                    record.Port = server.Dedicated.m_port;
                    break;
                case ServerJoinDataType.SteamUser:
                    record.ServerKind = "Steam";
                    record.Address = server.SteamUser.m_joinUserID.m_SteamID.ToString(CultureInfo.InvariantCulture);
                    break;
                case ServerJoinDataType.PlayFabUser:
                    record.ServerKind = "PlayFab";
                    record.Address = server.PlayFabUser.m_remotePlayerId;
                    break;
                default: throw new InvalidOperationException("Unsupported server type.");
            }
            return record;
        }

        private static string FindServerName(ServerJoinData server)
        {
            // Names are optional presentation data. A failed lookup must never block a join or save.
            try
            {
                string name;
                return MultiBackendMatchmaking.TryGetServerName(server, out name) ? name : null;
            }
            catch (Exception) { return null; }
        }

        private static ServerJoinData RestoreServer(SessionRecord record)
        {
            ServerJoinData server;
            switch (record.ServerKind)
            {
                case "Dedicated": server = new ServerJoinData(new ServerJoinDataDedicated(record.Address, (ushort)record.Port)); break;
                case "Steam": server = new ServerJoinData(new ServerJoinDataSteamUser(ulong.Parse(record.Address, CultureInfo.InvariantCulture))); break;
                case "PlayFab": server = new ServerJoinData(new ServerJoinDataPlayFabUser(record.Address)); break;
                default: throw new InvalidDataException("Unsupported server type.");
            }
            PlatformUserID owner;
            if (PlatformUserID.TryParse(record.ServerOwner, out owner)) server.m_owner = owner;
            return server;
        }

        private void Continue()
        {
            if (_saved == null || _menu == null || _busy || _attempts.Active) return;
            CancelPending();
            if (!_saved.IsWorld)
            {
                _retryTarget = _saved;
                _attempts.Start();
            }
            ContinueAttempt(_saved);
        }

        private void ContinueAttempt(SessionRecord session)
        {
            try
            {
                _busy = true;
                // Local starts can show native save warnings before they accept the launch.
                // Only show their overlay once TransitionToMainScene actually begins.
                if (!session.IsWorld) BeginLoading(_menu);
                PlayerProfile selected = null;
                var profiles = SaveSystem.GetAllPlayerProfiles();
                foreach (PlayerProfile profile in profiles)
                {
                    if (profile.GetFilename() == session.CharacterFilename && profile.GetPlayerID() == session.CharacterId)
                    { selected = profile; break; }
                }
                if (selected == null)
                {
                    CancelPending();
                    Warning(Text("Сохранённый персонаж не найден. Войдите обычным способом, чтобы сохранить новый вход.",
                        "The saved character was not found. Join normally to save a new session."));
                    return;
                }
                AccessTools.Method(typeof(FejdStartup), "SelectCharacter").Invoke(_menu,
                    new object[] { selected.GetFilename(), selected.m_fileSource });
                if (session.IsWorld)
                {
                    ContinueLocalWorld(profiles, selected, session);
                    return;
                }
                _loadingScreen?.Advance(LoadingStage.RestoringPassword);
                string password;
                try
                {
                    password = _attempts.NeedsPassword ? null :
                        (_retryPasswordAvailable ? _retryPassword : _passwordProtection.Unprotect(session.ProtectedPassword));
                }
                catch (Exception)
                {
                    CancelPending();
                    Warning(Text("Не удалось прочитать сохранённый пароль. Войдите обычным способом и введите пароль снова.",
                        "The saved password could not be read. Join normally and enter your password again."));
                    return;
                }
                _previousPassword = (string)PasswordField.GetValue(null);
                _passwordInjected = true;
                // A passwordless server must not inherit a command-line password either.
                PasswordField.SetValue(null, string.IsNullOrEmpty(password) ? null : password);
                _pendingPassword = password;
                _loadingScreen?.Advance(LoadingStage.FindingServer);
                _menu.SetServerToJoin(RestoreServer(session));
                _menu.JoinServer();
                // Privilege/version checks can return without starting any connection.
                if (!_menu.IsInvoking("LoadMainSceneIfBackendSelected") && !ZNet.HasServerHost() &&
                    (session.ServerKind != "PlayFab" || PlayFabManager.IsLoggedIn)) CancelPending();
            }
            catch (Exception)
            {
                CancelPending();
                Logger.LogWarning("Continue could not start. Ordinary joining remains available.");
                Warning(Text("Не удалось начать подключение. Войдите на сервер обычным способом.",
                    "The connection could not be started. Join the server normally."));
            }
        }

        private void ContinueLocalWorld(System.Collections.Generic.List<PlayerProfile> profiles, PlayerProfile selected, SessionRecord session)
        {
            World world = null;
            foreach (World candidate in SaveSystem.GetWorldList())
            {
                if (!session.MatchesWorld(candidate.m_worldName, candidate.m_uid)) continue;
                if (world != null) throw new InvalidDataException("The saved world identity is ambiguous.");
                world = candidate;
            }
            if (world == null || world.m_dataError != World.SaveDataError.None)
            {
                CancelPending();
                Warning("The saved world is missing or unavailable. Start a world normally to save a new session.");
                return;
            }
            // OnWorldStart uses the selected profile for the native cloud-capacity checks.
            AccessTools.Field(typeof(FejdStartup), "m_profiles").SetValue(_menu, profiles);
            AccessTools.Field(typeof(FejdStartup), "m_profileIndex").SetValue(_menu, profiles.IndexOf(selected));
            AccessTools.Field(typeof(FejdStartup), "m_world").SetValue(_menu, world);
            string password;
            try { password = session.IsHostedWorld ? _passwordProtection.Unprotect(session.ProtectedPassword) : ""; }
            catch (Exception)
            {
                CancelPending();
                Warning("The saved hosting password could not be read. Start the world normally and enter the password again.");
                return;
            }
            // Let Valheim's normal Start Server callback handle multiplayer consent/login.
            _menu.m_openServerToggle.SetIsOnWithoutNotify(false);
            _menu.m_openServerToggle.isOn = session.StartServer;
            if (session.IsHostedWorld && !PlatformPrefs.GetBool("EulaAccepted"))
            {
                CancelPending();
                return;
            }
            _menu.m_publicServerToggle.SetIsOnWithoutNotify(session.CommunityServer);
            _menu.m_crossplayServerToggle.SetIsOnWithoutNotify(session.Crossplay);
            _menu.m_serverPassword.text = password;
            if (!(bool)AccessTools.Method(typeof(FejdStartup), "CanStartServer").Invoke(_menu, null))
            {
                CancelPending();
                Warning("The saved world server settings are no longer valid. Start the world normally to update them.");
                return;
            }
            _menu.OnWorldStart();
            if (!_menu.IsInvoking("LoadMainSceneIfBackendSelected"))
            {
                _waitingForHostLogin = session.IsHostedWorld && session.Crossplay &&
                    !PlayFabManager.IsLoggedIn && UnifiedPopup.IsVisible();
                if (!_waitingForHostLogin) CancelPending();
            }
        }

        private void Warning(string message)
        {
            UnifiedPopup.Push(new WarningPopup(Text("Продолжить", "Continue"), message,
                () => UnifiedPopup.Pop(), localizeText: false));
        }

        internal void ConnectionFailed(ZNet.ConnectionStatus status)
        {
            if (_pending == null || _pending.IsWorld) return;
            ResumeFailure failure = status == ZNet.ConnectionStatus.ErrorPassword ? ResumeFailure.Password :
                (status == ZNet.ConnectionStatus.ErrorConnectFailed || status == ZNet.ConnectionStatus.ErrorDisconnected ||
                 status == ZNet.ConnectionStatus.ErrorFull ? ResumeFailure.Network : ResumeFailure.Other);
            if (!_attempts.Fail(failure, Time.unscaledTime))
            {
                CancelPending();
                return;
            }
            if (failure == ResumeFailure.Password)
            {
                _retryPassword = null;
                _retryPasswordAvailable = false;
            }
            ClearAttempt();
            Logger.LogInfo(failure == ResumeFailure.Password ?
                "Server rejected the password; waiting for user confirmation to enter a replacement." :
                "Continue connection failed; a bounded retry is scheduled.");
        }

        internal void ShowConnectionError(FejdStartup startup, ZNet.ConnectionStatus status)
        {
            ConnectionFailed(status == ZNet.ConnectionStatus.None ? ZNet.GetConnectionStatus() : status);
            UpdateRetryNotice(startup);
        }

        private void UpdateRetryNotice(FejdStartup startup)
        {
            if (!_attempts.Active || startup == null || startup.m_connectionFailedError == null ||
                startup.m_connectionFailedPanel == null || !startup.m_connectionFailedPanel.activeSelf) return;
            if (_attempts.WaitingForPassword)
                startup.m_connectionFailedError.text = "The server password was rejected. Select OK to reconnect and enter the new password.";
            else if (_attempts.WaitingForRetry)
                startup.m_connectionFailedError.text = string.Format(CultureInfo.InvariantCulture,
                    "Connection failed. Retrying in {0}s (attempt {1}/{2}). Select OK to cancel.",
                    Mathf.Max(0, Mathf.CeilToInt((float)(_attempts.NextAt - Time.unscaledTime))),
                    _attempts.Attempt + 1, ConnectionAttempts.Maximum);
        }

        private void UpdateRetry()
        {
            UpdateRetryNotice(_menu);
            bool ready = _menu != null && Game.instance == null && ZNet.instance == null &&
                _menu.m_mainMenu != null && _menu.m_mainMenu.activeInHierarchy &&
                !_menu.IsInvoking("LoadMainSceneIfBackendSelected") && !UnifiedPopup.IsVisible();
            if (!_attempts.TryBegin(Time.unscaledTime, ready)) return;
            if (_retryTarget == null) { CancelPending(); return; }
            if (_menu.m_connectionFailedPanel != null) _menu.m_connectionFailedPanel.SetActive(false);
            ContinueAttempt(_retryTarget);
        }

        internal void ConnectionErrorAcknowledged()
        {
            if (_attempts.WaitingForPassword) _attempts.ConfirmPassword(Time.unscaledTime);
            else if (_attempts.WaitingForRetry) CancelPending();
        }

        internal void LeavingGame()
        {
            // Automatic error logout must retain a scheduled retry across the scene change.
            if (!_attempts.Active || _attempts.WaitingForRetry || _attempts.WaitingForPassword) return;
            ConnectionFailed(ZNet.GetConnectionStatus());
        }

        internal void BeginLoading(FejdStartup startup)
        {
            if (!_busy || _loadingScreen != null) return;
            try
            {
                _loadingScreen = new ContinueLoadingScreen(startup);
                if (_attempts.Active) _loadingScreen.SetAttempt(_attempts.Attempt, ConnectionAttempts.Maximum);
                if (_pending != null && _pending.IsWorld) _loadingScreen.Advance(LoadingStage.SelectingWorld);
                Logger.LogInfo("Continue loading screen shown.");
            }
            catch (Exception) { Logger.LogWarning("Loading visuals could not be created; connection will continue normally."); }
        }

        internal void UpdateNativeLoading(Hud hud)
        {
            if (_loadingScreen == null) return;
            try { _loadingScreen.HandOffToNative(hud); }
            catch (Exception)
            {
                _loadingScreen.Dispose();
                _loadingScreen = null;
                Logger.LogWarning("Preloading UI could not be hidden; connection will continue normally.");
            }
        }

        internal void CreateButton(FejdStartup startup)
        {
            if (_attempts.WaitingForRetry || _attempts.WaitingForPassword) ClearAttempt();
            else CancelPending();
            InitializeStorage();
            _menu = startup;
            _characterDisplayName = null;
            try
            {
                if (_saved != null)
                    foreach (PlayerProfile profile in SaveSystem.GetAllPlayerProfiles())
                        if (profile.GetFilename() == _saved.CharacterFilename && profile.GetPlayerID() == _saved.CharacterId)
                        { _characterDisplayName = profile.GetName(); break; }
            }
            catch (Exception) { Logger.LogWarning("Saved character display name could not be read."); }
            try
            {
                if (startup.m_menuList.transform.Find("ContinueGameButton") != null) return;
                Button[] existing = startup.m_menuList.GetComponentsInChildren<Button>(true);
                Button template = null;
                foreach (Button candidate in existing)
                    if (candidate.name == "StartGame" || candidate.name == "Start") { template = candidate; break; }
                if (template == null && existing.Length > 0) template = existing[0];
                if (template == null) throw new InvalidOperationException("Main menu button not found.");
                _button = Instantiate(template, template.transform.parent);
                _button.name = "ContinueGameButton";
                _buttonHover = _button.gameObject.AddComponent<ContinueButtonHover>();
                _button.onClick = new Button.ButtonClickedEvent();
                _button.onClick.AddListener(Continue);
                _tmpLabels = _button.GetComponentsInChildren<TMP_Text>(true);
                _textLabels = _button.GetComponentsInChildren<UnityEngine.UI.Text>(true);
                foreach (TMP_Text text in _tmpLabels)
                {
                    text.richText = false;
                    text.textWrappingMode = TextWrappingModes.NoWrap;
                    text.overflowMode = TextOverflowModes.Overflow;
                }
                foreach (UnityEngine.UI.Text text in _textLabels)
                {
                    text.supportRichText = false;
                    text.horizontalOverflow = HorizontalWrapMode.Overflow;
                    text.verticalOverflow = VerticalWrapMode.Truncate;
                }
                _labelSession = null;
                _labelCaption = null;
                _labelWidth = -1f;
                _button.transform.SetSiblingIndex(template.transform.GetSiblingIndex());
                RectTransform templateRect = template.GetComponent<RectTransform>();
                RectTransform buttonRect = _button.GetComponent<RectTransform>();
                if (template.transform.parent.GetComponent<LayoutGroup>() == null)
                {
                    float spacing = templateRect.rect.height + 8f;
                    float nearest = float.MaxValue;
                    foreach (Button candidate in existing)
                    {
                        if (candidate.transform.parent != template.transform.parent) continue;
                        float difference = Mathf.Abs(candidate.GetComponent<RectTransform>().anchoredPosition.y - templateRect.anchoredPosition.y);
                        if (difference > 1f) nearest = Mathf.Min(nearest, difference);
                    }
                    if (nearest != float.MaxValue) spacing = nearest;
                    buttonRect.anchoredPosition = templateRect.anchoredPosition + new Vector2(0, spacing);
                }
                _buttonRect = buttonRect;
                var details = new GameObject("ContinueSessionDetails", typeof(RectTransform), typeof(LayoutElement), typeof(TextMeshProUGUI));
                details.transform.SetParent(_button.transform, false);
                details.GetComponent<LayoutElement>().ignoreLayout = true;
                _detailsText = details.GetComponent<TextMeshProUGUI>();
                if (_tmpLabels.Length > 0)
                {
                    _detailsText.font = _tmpLabels[0].font;
                    _detailsText.fontSharedMaterial = _tmpLabels[0].fontSharedMaterial;
                    _detailsText.fontSize = Mathf.Clamp(_tmpLabels[0].fontSize * 0.55f, 16f, 22f);
                }
                else _detailsText.fontSize = 20f;
                _detailsText.color = new Color(0.77f, 0.72f, 0.59f, 1f);
                _detailsText.alignment = TextAlignmentOptions.Left;
                _detailsText.richText = false;
                _detailsText.textWrappingMode = TextWrappingModes.Normal;
                _detailsText.overflowMode = TextOverflowModes.Overflow;
                _detailsText.raycastTarget = false;
                RectTransform detailsRect = _detailsText.rectTransform;
                detailsRect.anchorMin = detailsRect.anchorMax = new Vector2(1f, 0.5f);
                detailsRect.pivot = new Vector2(0f, 0.5f);
                detailsRect.anchoredPosition = new Vector2(DetailsGap, 0f);
                details.SetActive(false);
                _button.gameObject.SetActive(true);
                _button.interactable = _saved != null;
                // Valheim selects array[0] without checking interactable on first keyboard/gamepad use.
                // Keep the normal Start entry first in its navigation array until a session exists.
                AccessTools.Field(typeof(FejdStartup), "m_menuButtons").SetValue(startup,
                    _saved != null ? startup.m_menuList.GetComponentsInChildren<Button>() : existing);
                Navigation navigation = _button.navigation;
                navigation.selectOnDown = template;
                _button.navigation = navigation;
                if (_saved != null)
                {
                    navigation = template.navigation;
                    navigation.selectOnUp = _button;
                    template.navigation = navigation;
                }
                Logger.LogInfo("Continue button added to the main menu.");
            }
            catch (Exception) { Logger.LogWarning("Continue button could not be added; ordinary joining remains available."); }
        }

        [HarmonyPatch(typeof(FejdStartup), "Start")]
        private static class MenuPatch
        {
            private static void Postfix(FejdStartup __instance) { Instance?.CreateButton(__instance); }
        }

        [HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.JoinServer))]
        private static class JoinPatch
        {
            private static void Prefix(FejdStartup __instance) { Instance?.CaptureJoin(__instance); }
        }

        [HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.OnStartGame))]
        private static class OrdinaryJoinPatch
        {
            private static void Prefix() { Instance?.CancelPending(); }
        }

        [HarmonyPatch(typeof(FejdStartup), "ShowConnectError")]
        private static class ConnectionErrorPatch
        {
            private static void Postfix(FejdStartup __instance, ZNet.ConnectionStatus statusOverride)
            { Instance?.ShowConnectionError(__instance, statusOverride); }
        }

        [HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.OnConnectionFailedOk))]
        private static class ConnectionErrorOkPatch
        {
            private static void Postfix() { Instance?.ConnectionErrorAcknowledged(); }
        }

        [HarmonyPatch(typeof(ZNet), "RPC_Error")]
        private static class ServerRejectionPatch
        {
            private static void Postfix(int error) { Instance?.ConnectionFailed((ZNet.ConnectionStatus)error); }
        }

        [HarmonyPatch(typeof(Game), nameof(Game.Logout))]
        private static class LogoutPatch
        {
            private static void Prefix() { Instance?.LeavingGame(); }
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.SetServer))]
        private static class LocalWorldStartPatch
        {
            private static void Prefix(bool server, bool openServer, bool publicServer, string password, World world)
            {
                try { Instance?.CaptureLocalWorld(server, openServer, publicServer, password, world); }
                catch (Exception) { Instance?.CancelPending(); Instance?.Logger.LogWarning("Could not track the local world; ordinary starting remains available."); }
            }
        }

        [HarmonyPatch(typeof(ZNet), "SendPeerInfo")]
        private static class PasswordPatch
        {
            private static void Prefix(ZNet __instance, string password) { Instance?.CapturePassword(__instance, password); }
            private static void Postfix(ZNet __instance)
            {
                if (!__instance.IsServer() && ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connecting)
                    Instance?._loadingScreen?.Advance(LoadingStage.Authenticating);
            }
        }

        [HarmonyPatch(typeof(FejdStartup), "TransitionToMainScene")]
        private static class LoadingStartPatch
        {
            private static void Prefix(FejdStartup __instance) { Instance?.BeginLoading(__instance); }
        }

        [HarmonyPatch(typeof(FejdStartup), "LoadMainScene")]
        private static class WorldLoadingPatch
        {
            private static void Prefix() { Instance?._loadingScreen?.Advance(LoadingStage.LoadingScene); }
        }

        [HarmonyPatch(typeof(ZNet), "ClientConnect")]
        private static class ConnectingPatch
        {
            private static void Prefix() { Instance?._loadingScreen?.Advance(LoadingStage.Connecting); }
        }

        [HarmonyPatch(typeof(ZNet), "RPC_ClientHandshake")]
        private static class HandshakePatch
        {
            private static void Prefix(ZNet __instance, bool needPassword)
            {
                if (!__instance.IsServer()) Instance?._loadingScreen?.Advance(
                    needPassword ? LoadingStage.SendingPassword : LoadingStage.Authenticating);
            }
        }

        [HarmonyPatch(typeof(ZNet), "RPC_PeerInfo")]
        private static class WorldInfoPatch
        {
            private static void Prefix(ZNet __instance)
            {
                if (!__instance.IsServer()) Instance?._loadingScreen?.Advance(LoadingStage.ReceivingWorld);
            }
        }

        [HarmonyPatch(typeof(Hud), "UpdateBlackScreen")]
        private static class NativeLoadingPatch
        {
            private static void Postfix(Hud __instance) { Instance?.UpdateNativeLoading(__instance); }
        }
    }
}
