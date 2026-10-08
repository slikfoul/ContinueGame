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
    [BepInPlugin(PluginId, "ContinueGame", "1.0.2")]
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
        private string _label;
        private TMP_Text[] _tmpLabels;
        private UnityEngine.UI.Text[] _textLabels;
        private CrossPlatformPasswordProtection _passwordProtection;
        private ContinueLoadingScreen _loadingScreen;

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
            _loadingScreen?.Dispose();
            _loadingScreen = null;
            RestorePassword();
            _pending = null;
            _pendingPassword = null;
            _busy = false;
            _loadingScheduled = false;
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
                string label = Text("Продолжить", "Continue");
                if (label != _label)
                {
                    foreach (TMP_Text text in _tmpLabels) text.text = label;
                    foreach (UnityEngine.UI.Text text in _textLabels) text.text = label;
                    _label = label;
                }
                _button.interactable = _saved != null && !_busy;
            }
            if (_pending != null && _menu != null)
            {
                if (_menu.IsInvoking("LoadMainSceneIfBackendSelected")) _loadingScheduled = true;
                else if (_loadingScheduled && !_menu.m_loading.activeSelf)
                {
                    // The native DNS/backend retry loop returned to the menu before ZNet was created.
                    CancelPending();
                }
            }
            if (_pending == null || ZNet.instance == null || ZNet.instance.IsServer()) return;
            ZNet.ConnectionStatus status = ZNet.GetConnectionStatus();
            if (status > ZNet.ConnectionStatus.Connected)
            {
                Logger.LogInfo("Connection did not succeed; keeping the last successful session.");
                CancelPending();
                return;
            }
            if (status == ZNet.ConnectionStatus.Connected) _loadingScreen?.Advance(LoadingStage.LoadingArea);
            if (status != ZNet.ConnectionStatus.Connected || Game.instance == null || Player.m_localPlayer == null) return;
            try
            {
                PlayerProfile profile = Game.instance.GetPlayerProfile();
                if (profile == null) return;
                _pending.CharacterFilename = profile.GetFilename();
                _pending.CharacterId = profile.GetPlayerID();
                _storage.Save(_pending, _pendingPassword ?? "");
                _saved = _pending;
                Logger.LogInfo("Last successful server and character saved; password protected.");
            }
            catch (Exception) { Logger.LogWarning("The successful session could not be saved. The previous session was kept."); }
            finally { CancelPending(); }
        }

        internal void CaptureJoin(FejdStartup startup)
        {
            try
            {
                ServerJoinData server = startup.GetServerToJoin();
                if (!server.IsValid) return;
                _pending = RecordServer(server);
                if (!_passwordInjected) _pendingPassword = null;
                Logger.LogInfo("Tracking a server connection attempt.");
            }
            catch (Exception) { CancelPending(); Logger.LogWarning("Could not track the connection; ordinary joining remains available."); }
        }

        internal void CapturePassword(ZNet network, string password)
        {
            if (_pending == null || network.IsServer()) return;
            _pendingPassword = password ?? "";
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
            if (_saved == null || _menu == null || _busy) return;
            try
            {
                _busy = true;
                BeginLoading(_menu);
                PlayerProfile selected = null;
                foreach (PlayerProfile profile in SaveSystem.GetAllPlayerProfiles())
                {
                    if (profile.GetFilename() == _saved.CharacterFilename && profile.GetPlayerID() == _saved.CharacterId)
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
                _loadingScreen?.Advance(LoadingStage.RestoringPassword);
                string password;
                try { password = _passwordProtection.Unprotect(_saved.ProtectedPassword); }
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
                PasswordField.SetValue(null, password.Length == 0 ? null : password);
                _pendingPassword = password;
                _loadingScreen?.Advance(LoadingStage.FindingServer);
                _menu.SetServerToJoin(RestoreServer(_saved));
                _menu.JoinServer();
                // Privilege/version checks can return without starting any connection.
                if (!_menu.IsInvoking("LoadMainSceneIfBackendSelected") && !ZNet.HasServerHost() &&
                    (_saved.ServerKind != "PlayFab" || PlayFabManager.IsLoggedIn)) CancelPending();
            }
            catch (Exception)
            {
                CancelPending();
                Logger.LogWarning("Continue could not start. Ordinary joining remains available.");
                Warning(Text("Не удалось начать подключение. Войдите на сервер обычным способом.",
                    "The connection could not be started. Join the server normally."));
            }
        }

        private void Warning(string message)
        {
            UnifiedPopup.Push(new WarningPopup(Text("Продолжить", "Continue"), message,
                () => UnifiedPopup.Pop(), localizeText: false));
        }

        internal void BeginLoading(FejdStartup startup)
        {
            if (!_busy || _loadingScreen != null) return;
            try
            {
                _loadingScreen = new ContinueLoadingScreen(startup);
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
            CancelPending();
            InitializeStorage();
            _menu = startup;
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
                _button.onClick = new Button.ButtonClickedEvent();
                _button.onClick.AddListener(Continue);
                _tmpLabels = _button.GetComponentsInChildren<TMP_Text>(true);
                _textLabels = _button.GetComponentsInChildren<UnityEngine.UI.Text>(true);
                _label = null;
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
