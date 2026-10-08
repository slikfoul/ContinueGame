using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ContinueGame
{
    // This UI owns only the wait before Valheim's native loading screen becomes visible.
    internal sealed class ContinueLoadingScreen : IDisposable
    {
        private static readonly Color Gold = new Color(0.92f, 0.76f, 0.44f, 1f);
        private static readonly string[] RussianStages = {
            "Выбор персонажа", "Восстановление пароля", "Поиск сервера", "Загрузка игровой сцены",
            "Подключение к серверу", "Передача пароля", "Проверка входа", "Получение данных мира", "Загрузка области мира"
        };
        private static readonly string[] EnglishStages = {
            "Selecting character", "Restoring password", "Finding server", "Loading game scene",
            "Connecting to server", "Sending password", "Authenticating", "Receiving world data", "Loading world area"
        };
        private readonly LoadingState _state = new LoadingState();
        private readonly GameObject _root;
        private readonly CanvasGroup _visibility;
        private readonly TMP_Text _stageText;
        private readonly RectTransform _shine;
        private readonly Texture2D _shineTexture;
        private readonly Sprite _shineSprite;
        private readonly float _startedAt;
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
                Image backdrop = Add<Image>("Backdrop");
                Stretch(backdrop.rectTransform);
                backdrop.color = new Color(0.025f, 0.022f, 0.016f, 1f);
                backdrop.raycastTarget = false;
                TMP_Text template = startup.m_menuList.GetComponentInChildren<TMP_Text>(true);
                TMP_Text heading = Label("Title", template, 40f);
                heading.text = "Loading";
                Place(heading.rectTransform, new Vector2(0.5f, 0.55f), new Vector2(1000f, 80f));
                _stageText = Label("CurrentStage", template, 30f);
                Place(_stageText.rectTransform, new Vector2(0.5f, 0.45f), new Vector2(1100f, 80f));
                Image border = Add<Image>("BarBorder");
                Place(border.rectTransform, new Vector2(0.5f, 0.39f), new Vector2(564f, 12f));
                border.color = new Color(Gold.r, Gold.g, Gold.b, 0.32f);
                border.raycastTarget = false;
                Image track = Add<Image>("BarTrack");
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

        // Only read native visibility. Never add content to, or modify, Valheim's loading UI.
        public void HandOffToNative(Hud hud)
        {
            if (IsFinished || hud == null || hud.m_loadingScreen == null) return;
            Image artwork = hud.m_loadingImage;
            bool artworkVisible = artwork != null && artwork.isActiveAndEnabled && artwork.sprite != null
                && artwork.color.a > 0f && artwork.canvas != null && artwork.canvas.isActiveAndEnabled
                && artwork.canvasRenderer.GetInheritedAlpha() > 0f;
            bool worldLoadingActive = hud.m_loadingProgress != null && hud.m_loadingProgress.activeInHierarchy;
            _state.ObserveNativeLoading(hud.m_loadingScreen.gameObject.activeInHierarchy,
                hud.m_loadingScreen.alpha, worldLoadingActive, artworkVisible);
            if (_state.Completed) Dispose();
        }

        public void Tick()
        {
            if (IsFinished) return;
            HandOffToNative(Hud.instance);
            if (IsFinished) return;
            string status = (Russian ? RussianStages : EnglishStages)[(int)_state.Stage];
            if (_stageText.text != status) _stageText.text = status;
            float phase = Mathf.Repeat((Time.unscaledTime - _startedAt + 0.9f) / 2.6f, 1f);
            _shine.anchoredPosition = new Vector2(Mathf.Lerp(-95f, 655f, Mathf.SmoothStep(0f, 1f, phase)), 0f);
            _visibility.alpha = UnifiedPopup.IsVisible() || (ZNet.instance != null && ZNet.instance.InPasswordDialog()) ? 0f : 1f;
        }

        private TMP_Text Label(string name, TMP_Text template, float size)
        {
            var text = Add<TextMeshProUGUI>(name);
            if (template != null) { text.font = template.font; text.fontSharedMaterial = template.fontSharedMaterial; }
            text.fontSize = size;
            text.color = Gold;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return text;
        }
        private T Add<T>(string name, Transform parent = null) where T : Component
        {
            var item = new GameObject(name, typeof(RectTransform));
            item.transform.SetParent(parent != null ? parent : _root.transform, false);
            return item.AddComponent<T>();
        }
        private static void Stretch(RectTransform rect)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
        private static void Place(RectTransform rect, Vector2 anchor, Vector2 size)
        { rect.anchorMin = rect.anchorMax = anchor; rect.pivot = new Vector2(0.5f, 0.5f); rect.sizeDelta = size; rect.anchoredPosition = Vector2.zero; }

        public void Dispose()
        {
            if (IsFinished) return;
            IsFinished = true;
            if (_root != null) { _root.SetActive(false); UnityEngine.Object.Destroy(_root); }
            if (_shineSprite != null) UnityEngine.Object.Destroy(_shineSprite);
            if (_shineTexture != null) UnityEngine.Object.Destroy(_shineTexture);
        }
    }
}
