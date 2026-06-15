using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Logging;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TCGWorkbenchPresets
{
    internal class PresetOverlay
    {
        private readonly ConfigEntry<float> _btnHeight;
        private readonly ConfigEntry<float> _barLeft;
        private readonly ConfigEntry<float> _barBottom;
        private readonly ConfigEntry<float> _barWidth;
        private readonly ManualLogSource    _log;

        private Canvas        _canvas;
        private GameObject    _buttonBar;
        private bool          _built;
        private TMP_FontAsset _gameFont;
        private bool          _fontApplied;
        private Sprite        _buttonSprite; // loaded from button_bg.png next to DLL, or procedural fallback

        internal PresetOverlay(
            ConfigEntry<float> btnHeight,
            ConfigEntry<float> barLeft,
            ConfigEntry<float> barBottom,
            ConfigEntry<float> barWidth,
            ManualLogSource    log)
        {
            _btnHeight = btnHeight;
            _barLeft   = barLeft;
            _barBottom = barBottom;
            _barWidth  = barWidth;
            _log       = log;
        }

        internal void Build(PresetDefinition[] presets, WorkbenchConnector connector)
        {
            _buttonSprite = LoadButtonSprite();

            var root = new GameObject("WorkbenchPresetOverlay");
            root.hideFlags = HideFlags.HideAndDontSave;
            _canvas = root.AddComponent<Canvas>();
            _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 999;
            root.AddComponent<GraphicRaycaster>();

            _buttonBar = new GameObject("ButtonBar");
            _buttonBar.AddComponent<RectTransform>();
            _buttonBar.transform.SetParent(root.transform, false);

            var hlg = _buttonBar.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing              = 12f;
            hlg.padding              = new RectOffset(4, 4, 0, 0);
            hlg.childControlWidth    = true;
            hlg.childControlHeight   = true;
            hlg.childForceExpandWidth  = true;
            hlg.childForceExpandHeight = true;

            foreach (var p in presets)
                CreateButton(_buttonBar.transform, p, connector);

            root.SetActive(false);
            _built = true;
        }

        // Show/hide and reposition the overlay every frame.
        // Open condition: hierarchy must be active AND the expand animation must have settled
        // (autoWidth > 10 px). When the workbench is closed — via SetActive(false) on the
        // WorkbenchUIScreen or via the collapse animation — at least one of those conditions
        // becomes false, which hides the bar automatically.
        internal void UpdateOverlay(Slider minSlider)
        {
            if (_canvas == null || !_built) return;

            if (minSlider == null) { _canvas.gameObject.SetActive(false); return; }

            // Measure slider group first; autoWidth is our "animation settled" probe.
            var sliderGroupRect = minSlider.transform.parent?.GetComponent<RectTransform>()
                                  ?? minSlider.GetComponent<RectTransform>();
            var corners = new Vector3[4];
            sliderGroupRect.GetWorldCorners(corners);
            float autoWidth = corners[2].x - corners[0].x;

            // activeInHierarchy catches SetActive(false) close; autoWidth > 10 catches
            // the collapse animation and the title-scene preload (where everything is zero-size).
            bool open = minSlider.gameObject.activeInHierarchy && autoWidth > 10f;
            _canvas.gameObject.SetActive(open);
            if (!open) return;

            // Position from screen fractions — reliable regardless of game's internal hierarchy.
            float left   = Screen.width  * _barLeft.Value;
            float bottom = Screen.height * _barBottom.Value;
            float width  = Screen.width  * _barWidth.Value;
            float btnH   = _btnHeight.Value;
            float cx     = Screen.width  * 0.5f;
            float cy     = Screen.height * 0.5f;

            var barRect = _buttonBar.GetComponent<RectTransform>();
            barRect.anchorMin        = new Vector2(0.5f, 0.5f);
            barRect.anchorMax        = new Vector2(0.5f, 0.5f);
            barRect.pivot            = new Vector2(0f, 0f);  // bottom-left; bar grows upward
            barRect.anchoredPosition = new Vector2(left - cx, bottom - cy + 4f);
            barRect.sizeDelta        = new Vector2(width, btnH);
        }

        // Called once per frame until the game font is found and applied to button labels.
        // Accepts the cached min slider so we can sample the font from inside the workbench
        // hierarchy — avoids picking up LiberationSans SDF from unrelated Title-scene elements.
        internal void TryCacheStyle(Slider minSlider)
        {
            if (!_built || _fontApplied) return;

            // Don't grab a font until the workbench is actually open; that way we sample from
            // within the workbench's own canvas rather than the first random TMP element.
            if (minSlider == null || !minSlider.gameObject.activeInHierarchy) return;

            if (_gameFont == null)
            {
                // Walk up the slider's hierarchy to its canvas root, then find any TMP label.
                Transform node = minSlider.transform;
                while (node.parent != null) node = node.parent;
                var wb = node.GetComponentInChildren<TextMeshProUGUI>(true);
                if (wb != null && wb.font != null) _gameFont = wb.font;
            }

            // Ultimate fallback: any font anywhere.
            if (_gameFont == null)
                foreach (var t in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
                    if (t.font != null) { _gameFont = t.font; break; }

            if (_gameFont == null) return;

            foreach (Transform child in _buttonBar.transform)
            {
                var tmp = child.GetComponentInChildren<TextMeshProUGUI>();
                if (tmp == null) continue;
                tmp.font = _gameFont;
                // fontMaterial creates a per-instance material; EnableKeyword turns on the
                // outline SDF pass which the shared font material may have disabled.
                tmp.fontMaterial.EnableKeyword(ShaderUtilities.Keyword_Outline);
                tmp.outlineColor = Color.black;
                tmp.outlineWidth = 0.35f;
                tmp.ForceMeshUpdate(true);
            }
            _fontApplied = true;
            _log.LogInfo($"[Overlay] Applied game font '{_gameFont.name}' to preset buttons.");
        }

        private void CreateButton(Transform parent, PresetDefinition preset, WorkbenchConnector connector)
        {
            var go  = new GameObject(preset.Label);
            var img = go.AddComponent<Image>();
            img.sprite = _buttonSprite;
            img.type   = Image.Type.Sliced;
            img.color  = preset.TierColor;
            go.transform.SetParent(parent, false);

            // Dark border around each button.
            var outline = go.AddComponent<Outline>();
            outline.effectColor    = new Color(0f, 0f, 0f, 0.75f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = _btnHeight.Value;
            le.flexibleWidth   = 1f;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var cols = btn.colors;
            cols.normalColor      = Color.white;
            cols.highlightedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            cols.pressedColor     = new Color(0.65f, 0.65f, 0.65f, 1f);
            cols.colorMultiplier  = 1f;
            btn.colors = cols;
            btn.onClick.AddListener(() => connector.ApplyPreset(preset.Min.Value, preset.Max.Value));

            var textGO = new GameObject("Label");
            var tmp    = textGO.AddComponent<TextMeshProUGUI>();
            textGO.transform.SetParent(go.transform, false);
            tmp.text         = preset.Label;
            tmp.fontStyle    = FontStyles.Bold;
            tmp.fontSize     = 15f;
            tmp.color        = Color.white;
            tmp.outlineWidth = 0.25f;      // thick stroke like the game's own buttons
            tmp.outlineColor = Color.black;
            tmp.alignment    = TextAlignmentOptions.Center;
            if (_gameFont != null) tmp.font = _gameFont;
            var tr = textGO.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = Vector2.zero;
            tr.offsetMax = Vector2.zero;
        }

        // Tries to load button_bg.png from the same folder as the DLL.
        // If the file is absent, falls back to a programmatically generated rounded rect.
        // To ship a custom look: drop any 64×32 (or similar) PNG named button_bg.png
        // next to TCGWorkbenchPresets.dll. It will be used as a 9-sliced sprite with 8px borders.
        private Sprite LoadButtonSprite()
        {
            try
            {
                string dir  = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string path = Path.Combine(dir, "button_bg.png");
                if (File.Exists(path))
                {
                    var tex = new Texture2D(2, 2);
                    ImageConversion.LoadImage(tex, File.ReadAllBytes(path));
                    var s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                        new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                        new Vector4(8, 8, 8, 8));
                    _log.LogInfo($"[Overlay] Loaded button_bg.png ({tex.width}x{tex.height}).");
                    return s;
                }
            }
            catch (System.Exception e) { _log.LogWarning($"[Overlay] button_bg.png load failed: {e.Message}"); }

            return BuildRoundedRectSprite();
        }

        // Generates a 64×32 white rounded-rect texture at runtime.
        // Used as a 9-sliced sprite so the 8px corners scale cleanly at any button size.
        private static Sprite BuildRoundedRectSprite()
        {
            const int W = 64, H = 32, R = 8;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                // Nearest corner centre distance for the 4 corner quads.
                int qx = x < R ? R - x : x >= W - R ? x - (W - R - 1) : 0;
                int qy = y < R ? R - y : y >= H - R ? y - (H - R - 1) : 0;
                float d = Mathf.Sqrt(qx * qx + qy * qy);
                byte a  = d < R - 0.5f ? (byte)255
                        : d < R + 0.5f ? (byte)(255 * (R + 0.5f - d))
                        : (byte)0;
                px[y * W + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(px);
            tex.Apply(false);
            return Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, new Vector4(R, R, R, R));
        }
    }
}
