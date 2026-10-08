using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ContinueGame
{
    internal sealed class ContinueLoadingScreen : IDisposable
    {
        private static readonly Color Gold = new Color(0.92f, 0.76f, 0.44f, 1f);
        private static readonly string[] RussianStages = {
            "Выбор персонажа", "Восстановление пароля", "Поиск сервера", "Загрузка игровой сцены",
            "Подключение к серверу", "Передача пароля", "Проверка входа", "Получение данных мира",
            "Загрузка области мира", "Подготовка персонажа", "Готово"
        };
        private static readonly string[] EnglishStages = {
            "Selecting character", "Restoring password", "Finding server", "Loading game scene",
            "Connecting to server", "Sending password", "Authenticating", "Receiving world data",
            "Loading world area", "Preparing character", "Ready"
        };
        private readonly LoadingState _state = new LoadingState();
        private readonly GameObject _root;
        private readonly CanvasGroup _visibility;
        private readonly TMP_Text _stageText;
        private readonly TMP_Text _historyText;
        private readonly RectTransform _shine;
        private readonly Texture2D _shineTexture;
        private readonly Sprite _shineSprite;
        private readonly float _startedAt;
        private GameObject _nativeCaption;
        private CanvasGroup _nativeVisibility;
        private TMP_Text _nativeStageText;
        private TMP_Text _nativeHistoryText;
        private bool _preloadingRemoved;
        private int _displayedCount = -1;
        private bool _displayedRussian;
        public bool IsFinished { get; private set; }

        public ContinueLoadingScreen(FejdStartup startup)
        {
            _root = new GameObject("ContinueGamePreloading", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            try
            {
                UnityEngine.Object.DontDestroyOnLoad(_root);
                Canvas canvas = _root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 32000;
                CanvasScaler scaler = _root.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
                _visibility = _root.GetComponent<CanvasGroup>();
                _visibility.interactable = false;
                _visibility.blocksRaycasts = false;
                _startedAt = Time.unscaledTime;

                Image backdrop = Add<Image>("Backdrop", _root.transform);
                Stretch(backdrop.rectTransform);
                backdrop.color = new Color(0.025f, 0.022f, 0.016f, 1f);
                backdrop.raycastTarget = false;
                TMP_Text template = startup.m_menuList.GetComponentInChildren<TMP_Text>(true);
                TMP_Text heading = Label("Title", template, 40f, _root.transform);
                heading.text = "Loading";
                Place(heading.rectTransform, new Vector2(0.5f, 0.55f), new Vector2(1000f, 80f));
                _stageText = Label("Status", template, 30f, _root.transform);
                Place(_stageText.rectTransform, new Vector2(0.5f, 0.45f), new Vector2(1100f, 80f));
                _historyText = Label("CompletedStages", template, 20f, _root.transform);
                _historyText.color = new Color(Gold.r, Gold.g, Gold.b, 0.7f);
                Place(_historyText.rectTransform, new Vector2(0.5f, 0.29f), new Vector2(1300f, 140f));

                Image border = Add<Image>("BarBorder", _root.transform);
                Place(border.rectTransform, new Vector2(0.5f, 0.39f), new Vector2(564f, 12f));
                border.color = new Color(Gold.r, Gold.g, Gold.b, 0.32f);
                border.raycastTarget = false;
                Image track = Add<Image>("BarTrack", _root.transform);
                Place(track.rectTransform, new Vector2(0.5f, 0.39f), new Vector2(560f, 8f));
                track.color = new Color(0.1f, 0.08f, 0.04f, 1f);
                track.raycastTarget = false;
                track.gameObject.AddComponent<RectMask2D>();
                _shineTexture = new Texture2D(96, 1, TextureFormat.RGBA32, false);
                _shineTexture.wrapMode = TextureWrapMode.Clamp;
                for (int i = 0; i < 96; i++)
                {
                    float glow = Mathf.Sin(Mathf.PI * i / 95f);
                    _shineTexture.SetPixel(i, 0, new Color(Gold.r, Gold.g, Gold.b, glow * glow));
                }
                _shineTexture.Apply(false, true);
                _shineSprite = Sprite.Create(_shineTexture, new Rect(0, 0, 96, 1), new Vector2(0.5f, 0.5f));
                Image shine = Add<Image>("BarGlow", track.transform);
                shine.sprite = _shineSprite;
                shine.raycastTarget = false;
                _shine = shine.rectTransform;
                Place(_shine, new Vector2(0f, 0.5f), new Vector2(190f, 8f));
                Tick();
            }
            catch { Dispose(); throw; }
        }

        private static bool Russian => Localization.instance != null && Localization.instance.GetSelectedLanguage() == "Russian";
        public void Advance(LoadingStage stage) { _state.Advance(stage); }
        public void Complete() { _state.Complete(); }

        // Remove the opaque panel in this frame; late stages use text under the native fade group.
        public void HandOffToNative(Hud hud)
        {
            if (IsFinished || _preloadingRemoved || hud == null || hud.m_loadingScreen == null
                || !hud.m_loadingScreen.gameObject.activeInHierarchy || hud.m_loadingScreen.alpha <= 0f) return;
            _nativeCaption = new GameObject("ContinueGameConnectionStages", typeof(RectTransform), typeof(CanvasGroup));
            _nativeCaption.transform.SetParent(hud.m_loadingScreen.transform, false);
            _nativeVisibility = _nativeCaption.GetComponent<CanvasGroup>();
            _nativeVisibility.interactable = false;
            _nativeVisibility.blocksRaycasts = false;
            Place(_nativeCaption.GetComponent<RectTransform>(), new Vector2(0.5f, 0.84f), new Vector2(1540f, 170f));
            TMP_Text template = hud.m_loadingTip != null ? hud.m_loadingTip : _stageText;
            _nativeStageText = Label("CurrentStage", template, 28f, _nativeCaption.transform);
            Place(_nativeStageText.rectTransform, new Vector2(0.5f, 0.8f), new Vector2(1540f, 65f));
            _nativeHistoryText = Label("CompletedStages", template, 18f, _nativeCaption.transform);
            _nativeHistoryText.color = new Color(Gold.r, Gold.g, Gold.b, 0.7f);
            Place(_nativeHistoryText.rectTransform, new Vector2(0.5f, 0.3f), new Vector2(1540f, 100f));
            RemovePreloading();
            _displayedCount = -1;
            RefreshLabels();
        }

        private void RefreshLabels()
        {
            bool russian = Russian;
            int count = _state.Visited.Count;
            if (_displayedCount == count && _displayedRussian == russian) return;
            string[] labels = russian ? RussianStages : EnglishStages;
            string[] completed = new string[count - 1];
            for (int i = 0; i < completed.Length; i++) completed[i] = labels[(int)_state.Visited[i]];
            string history = completed.Length == 0 ? "" : (russian ? "Пройдено: " : "Completed: ") + string.Join("  •  ", completed);
            TMP_Text stageText = _preloadingRemoved ? _nativeStageText : _stageText;
            TMP_Text historyText = _preloadingRemoved ? _nativeHistoryText : _historyText;
            if (stageText != null) stageText.text = labels[(int)_state.Stage];
            if (historyText != null) historyText.text = history;
            _displayedCount = count;
            _displayedRussian = russian;
        }

        public void Tick()
        {
            if (IsFinished) return;
            Hud hud = Hud.instance;
            HandOffToNative(hud);
            RefreshLabels();
            bool dialogVisible = UnifiedPopup.IsVisible() || (ZNet.instance != null && ZNet.instance.InPasswordDialog());
            if (_preloadingRemoved)
            {
                if (_nativeCaption == null || (_state.Completed && (hud == null || hud.m_loadingScreen == null
                    || !hud.m_loadingScreen.gameObject.activeInHierarchy || hud.m_loadingScreen.alpha <= 0f)))
                { Dispose(); return; }
                _nativeVisibility.alpha = dialogVisible ? 0f : 1f;
            }
            else
            {
                if (_state.Completed) { Dispose(); return; }
                float phase = Mathf.Repeat((Time.unscaledTime - _startedAt + 0.9f) / 2.6f, 1f);
                _shine.anchoredPosition = new Vector2(Mathf.Lerp(-95f, 655f, Mathf.SmoothStep(0f, 1f, phase)), 0f);
                _visibility.alpha = dialogVisible ? 0f : 1f;
            }
        }

        private static TMP_Text Label(string name, TMP_Text template, float size, Transform parent)
        {
            var text = Add<TextMeshProUGUI>(name, parent);
            if (template != null) { text.font = template.font; text.fontSharedMaterial = template.fontSharedMaterial; }
            text.fontSize = size;
            text.color = Gold;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return text;
        }

        private static T Add<T>(string name, Transform parent) where T : Component
        {
            var item = new GameObject(name, typeof(RectTransform));
            item.transform.SetParent(parent, false);
            return item.AddComponent<T>();
        }
        private static void Stretch(RectTransform rect)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
        private static void Place(RectTransform rect, Vector2 anchor, Vector2 size)
        { rect.anchorMin = rect.anchorMax = anchor; rect.pivot = new Vector2(0.5f, 0.5f); rect.sizeDelta = size; rect.anchoredPosition = Vector2.zero; }

        private void RemovePreloading()
        {
            if (_preloadingRemoved) return;
            _preloadingRemoved = true;
            if (_root != null) { _root.SetActive(false); UnityEngine.Object.Destroy(_root); }
            if (_shineSprite != null) UnityEngine.Object.Destroy(_shineSprite);
            if (_shineTexture != null) UnityEngine.Object.Destroy(_shineTexture);
        }

        public void Dispose()
        {
            if (IsFinished) return;
            IsFinished = true;
            RemovePreloading();
            if (_nativeCaption != null) { _nativeCaption.SetActive(false); UnityEngine.Object.Destroy(_nativeCaption); }
        }
    }
}
