using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ContinueGame
{
    // Covers only the menu/backend wait. Valheim owns the actual world loading screen.
    internal sealed class ContinueLoadingScreen : IDisposable
    {
        private static readonly Color Gold = new Color(0.92f, 0.76f, 0.44f, 1f);
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

                Image backdrop = Add<Image>("Backdrop", _root.transform);
                backdrop.rectTransform.anchorMin = Vector2.zero;
                backdrop.rectTransform.anchorMax = Vector2.one;
                backdrop.rectTransform.offsetMin = backdrop.rectTransform.offsetMax = Vector2.zero;
                backdrop.color = new Color(0.025f, 0.022f, 0.016f, 1f);
                backdrop.raycastTarget = false;

                TMP_Text template = startup.m_menuList.GetComponentInChildren<TMP_Text>(true);
                TMP_Text heading = Label("Title", template, 40f);
                heading.text = "Loading";
                Place(heading.rectTransform, new Vector2(0.5f, 0.55f), new Vector2(1000f, 80f));
                _stageText = Label("Status", template, 30f);
                Place(_stageText.rectTransform, new Vector2(0.5f, 0.45f), new Vector2(1100f, 80f));

                Image border = Add<Image>("BarBorder", _root.transform);
                Place(border.rectTransform, new Vector2(0.5f, 0.39f), new Vector2(564f, 12f));
                border.color = new Color(Gold.r, Gold.g, Gold.b, 0.32f);
                border.raycastTarget = false;
                Image track = Add<Image>("BarTrack", _root.transform);
                Place(track.rectTransform, new Vector2(0.5f, 0.39f), new Vector2(560f, 8f));
                track.color = new Color(0.1f, 0.08f, 0.04f, 1f);
                track.raycastTarget = false;
                track.gameObject.AddComponent<RectMask2D>();

                // A continuous travelling glow indicates activity, not a made-up completion percentage.
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

        // Called after Valheim updates its loading screen, before this frame is rendered.
        public void HandOffToNative(Hud hud)
        {
            if (!IsFinished && hud != null && hud.m_loadingScreen != null
                && hud.m_loadingScreen.gameObject.activeInHierarchy && hud.m_loadingScreen.alpha > 0f)
                Dispose();
        }

        public void Tick()
        {
            if (_root == null || IsFinished) return;
            HandOffToNative(Hud.instance);
            if (IsFinished) return;
            string status = _state.Stage == LoadingStage.FindingServer
                ? (Russian ? "Поиск сервера" : "Finding server")
                : (Russian ? "Загрузка мира" : "Loading world");
            if (_stageText.text != status) _stageText.text = status;
            float phase = Mathf.Repeat((Time.unscaledTime - _startedAt + 0.9f) / 2.6f, 1f);
            _shine.anchoredPosition = new Vector2(Mathf.Lerp(-95f, 655f, Mathf.SmoothStep(0f, 1f, phase)), 0f);
            _visibility.alpha = UnifiedPopup.IsVisible() || (ZNet.instance != null && ZNet.instance.InPasswordDialog()) ? 0f : 1f;
        }

        private TMP_Text Label(string name, TMP_Text template, float size)
        {
            var text = Add<TextMeshProUGUI>(name, _root.transform);
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

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 size)
        { rect.anchorMin = rect.anchorMax = anchor; rect.pivot = new Vector2(0.5f, 0.5f); rect.sizeDelta = size; rect.anchoredPosition = Vector2.zero; }

        public void Dispose()
        {
            if (IsFinished) return;
            IsFinished = true;
            // Destroy is deferred: hide now so the native screen is visible in this same frame.
            if (_root != null) { _root.SetActive(false); UnityEngine.Object.Destroy(_root); }
            if (_shineSprite != null) UnityEngine.Object.Destroy(_shineSprite);
            if (_shineTexture != null) UnityEngine.Object.Destroy(_shineTexture);
        }
    }
}
