using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Reflection;
using Object = UnityEngine.Object;

namespace SearchAnything
{
    /// <summary>
    /// Small helpers for building the Search Anything window from code, using
    /// TextMeshPro controls so they match the game's text rendering.
    /// </summary>
    public static class UiFactory
    {
        public static readonly Color PanelColor = new(0.10f, 0.12f, 0.16f, 0.96f);
        public static readonly Color RowColor = new(1f, 1f, 1f, 0.05f);
        public static readonly Color ControlColor = new(1f, 1f, 1f, 1f);
        public static readonly Color ControlTextColor = new(0.08f, 0.09f, 0.12f, 1f);
        public static readonly Color AccentColor = new(1f, 0.62f, 0.16f, 1f);
        // Toggle states, matching the Better Map Filter theme (grey off / blue on).
        public static readonly Color ToggleOffColor = new(0.24f, 0.28f, 0.36f, 1f);
        public static readonly Color ToggleOnColor = new(0.20f, 0.55f, 1f, 1f);
        public static readonly Color TextColor = new(0.92f, 0.94f, 0.97f, 1f);
        public static readonly Color MutedColor = new(0.65f, 0.69f, 0.76f, 1f);

        public static TMP_FontAsset Font;

        /// <summary>A live game dropdown we clone so our controls match the game.</summary>
        public static GameObject GameDropdownTemplate;

        /// <summary>The game dropdown's search input, cloned for our search box.</summary>
        public static GameObject GameInputTemplate;

        private static Sprite _uiSprite;
        private static Sprite _roundedSprite;
        private static Sprite _roundedBottomSprite;
        private static Sprite _roundedTopSprite;
        private static Sprite _circleSprite;

        private static Sprite UiSprite()
        {
            if (_uiSprite == null)
                _uiSprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
            return _uiSprite;
        }

        /// <summary>Captures the game's TMP font so our generated text matches it.</summary>
        public static void CaptureFont(Component panel)
        {
            if (Font != null || panel == null)
                return;
            var sample = panel.GetComponentInChildren<TextMeshProUGUI>(true);
            if (sample != null && sample.font != null)
                Font = sample.font;
        }

        /// <summary>
        /// Grabs the City Map filter panel's own search dropdown so we can clone
        /// its input field for our product search box (matches the game's look).
        /// </summary>
        public static void CaptureGameDropdown(Component panel)
        {
            if (panel == null)
                return;

            var inputField = typeof(UI.Elements.Dropdown).GetField(
                "selectedOptionInputField", BindingFlags.NonPublic | BindingFlags.Instance);

            if (GameDropdownTemplate == null)
            {
                var dd = panel.GetComponentInChildren<UI.Elements.Dropdown>(true);
                if (dd != null)
                {
                    GameDropdownTemplate = dd.gameObject;
                    if (inputField != null && inputField.GetValue(dd) is TMP_InputField input && input != null)
                        GameInputTemplate = input.gameObject;
                }
            }
        }

        public static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name)
                return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindDeep(root.GetChild(i), name);
                if (found != null)
                    return found;
            }
            return null;
        }

        /// <summary>
        /// Neutralises localization scripts on a clone so our custom text sticks
        /// (the game otherwise re-applies localized strings every frame).
        /// </summary>
        public static void DisableLocalization(GameObject clone)
        {
            foreach (var mb in clone.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null)
                    continue;
                var typeName = mb.GetType().Name;
                if (typeName.Contains("Localiz") || typeName.Contains("Translat"))
                    Object.DestroyImmediate(mb);
            }
        }

        /// <summary>A generated rounded-rect sprite for soft control backgrounds.</summary>
        public static Sprite RoundedSprite()
        {
            if (_roundedSprite != null)
                return _roundedSprite;

            const int size = 48;
            const int radius = 10;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float cx = Mathf.Clamp(x, radius, size - 1 - radius);
                    float cy = Mathf.Clamp(y, radius, size - 1 - radius);
                    float dist = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    float alpha = Mathf.Clamp01(radius - dist + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            _roundedSprite = Sprite.Create(tex, new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                new Vector4(radius, radius, radius, radius));
            return _roundedSprite;
        }

        /// <summary>
        /// A generated sprite rounded on the bottom two corners only (square top),
        /// so a control can sit flush beneath the title bar.
        /// </summary>
        public static Sprite RoundedBottomSprite()
        {
            if (_roundedBottomSprite != null)
                return _roundedBottomSprite;

            const int size = 48;
            const int radius = 10;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float alpha = 1f;
                    // y == 0 is the bottom row; only round the bottom corners.
                    if (y < radius && (x < radius || x > size - 1 - radius))
                    {
                        float cx = x < radius ? radius : size - 1 - radius;
                        float dist = Mathf.Sqrt((x - cx) * (x - cx) + (y - radius) * (y - radius));
                        alpha = Mathf.Clamp01(radius - dist + 0.5f);
                    }
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            _roundedBottomSprite = Sprite.Create(tex, new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                new Vector4(radius, radius, radius, radius));
            return _roundedBottomSprite;
        }

        /// <summary>
        /// A generated sprite rounded on the top two corners only (square bottom),
        /// so the title bar can sit flush above the search bar.
        /// </summary>
        public static Sprite RoundedTopSprite()
        {
            if (_roundedTopSprite != null)
                return _roundedTopSprite;

            const int size = 48;
            const int radius = 10;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float alpha = 1f;
                    // y == size-1 is the top row; only round the top corners.
                    if (y > size - 1 - radius && (x < radius || x > size - 1 - radius))
                    {
                        float cx = x < radius ? radius : size - 1 - radius;
                        float cy = size - 1 - radius;
                        float dist = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                        alpha = Mathf.Clamp01(radius - dist + 0.5f);
                    }
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            _roundedTopSprite = Sprite.Create(tex, new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                new Vector4(radius, radius, radius, radius));
            return _roundedTopSprite;
        }

        /// <summary>A generated solid circle sprite.</summary>
        public static Sprite CircleSprite()
        {
            if (_circleSprite != null)
                return _circleSprite;

            const int size = 48;
            float r = size * 0.5f;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Mathf.Sqrt((x + 0.5f - r) * (x + 0.5f - r) + (y + 0.5f - r) * (y + 0.5f - r));
                    float alpha = Mathf.Clamp01(r - dist);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            _circleSprite = Sprite.Create(tex, new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f), 100f);
            return _circleSprite;
        }

        private static TMP_FontAsset ResolveFont()
        {
            if (Font != null)
                return Font;
            if (TMP_Settings.defaultFontAsset != null)
                return TMP_Settings.defaultFontAsset;
            return null;
        }

        public static GameObject Rect(string name, Transform parent, out RectTransform rt)
        {
            var go = new GameObject(name, typeof(RectTransform));
            rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            return go;
        }

        public static Image Panel(string name, Transform parent, Color color, out RectTransform rt)
        {
            var go = Rect(name, parent, out rt);
            var img = go.AddComponent<Image>();
            img.sprite = UiSprite();
            img.type = Image.Type.Sliced;
            img.color = color;
            return img;
        }

        public static TextMeshProUGUI Text(
            string content, Transform parent, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            var go = Rect("Text", parent, out _);
            var t = go.AddComponent<TextMeshProUGUI>();
            var font = ResolveFont();
            if (font != null)
                t.font = font;
            t.text = content;
            t.fontSize = size;
            t.color = color;
            t.fontStyle = FontStyles.Normal;
            t.alignment = align;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            return t;
        }

        public static Button Button(string label, Transform parent, Color color, Action onClick)
        {
            var img = Panel("Button", parent, color, out _);
            img.sprite = RoundedSprite();
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 1f;
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.fadeDuration = 0.05f;
            btn.colors = colors;

            var text = Text(label, img.transform, 21f, BestTextColor(color), TextAlignmentOptions.Center);
            Stretch(text.rectTransform);

            if (onClick != null)
                btn.onClick.AddListener(() => onClick());
            return btn;
        }

        /// <summary>Picks black or white text depending on how light the (opaque) background is.</summary>
        public static Color BestTextColor(Color bg)
        {
            float lum = bg.r * 0.299f + bg.g * 0.587f + bg.b * 0.114f;
            return (bg.a > 0.5f && lum > 0.6f) ? ControlTextColor : TextColor;
        }

        /// <summary>
        /// Builds a single-line text search box. Prefers a clone of the game's own
        /// dropdown search input (captured via <see cref="CaptureGameDropdown"/>);
        /// falls back to a plain TMP input field.
        /// </summary>
        public static TMP_InputField SearchInput(Transform parent, string placeholderText, Action<string> onChanged)
        {
            if (GameInputTemplate != null)
            {
                var game = GameSearchInput(parent, placeholderText, onChanged);
                if (game != null)
                    return game;
            }
            return TmpSearchInput(parent, placeholderText, onChanged);
        }

        private static TMP_InputField GameSearchInput(Transform parent, string placeholderText, Action<string> onChanged)
        {
            var go = Object.Instantiate(GameInputTemplate, parent, false);
            go.name = "SearchInput";
            go.SetActive(true);
            var input = go.GetComponent<TMP_InputField>();
            if (input == null)
            {
                Object.Destroy(go);
                return null;
            }

            input.onValueChanged.RemoveAllListeners();
            input.onEndEdit.RemoveAllListeners();
            input.onSelect.RemoveAllListeners();
            input.onDeselect.RemoveAllListeners();
            input.interactable = true;
            input.readOnly = false;
            input.characterLimit = 0;
            input.contentType = TMP_InputField.ContentType.Standard;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.SetTextWithoutNotify(string.Empty);

            var bg = (input.targetGraphic as Image) ?? go.GetComponent<Image>();
            if (bg == null)
            {
                bg = go.AddComponent<Image>();
                input.targetGraphic = bg;
            }
            bg.sprite = RoundedSprite();
            bg.type = Image.Type.Sliced;
            bg.pixelsPerUnitMultiplier = 1f;
            bg.color = ControlColor;
            if (input.textComponent != null)
                input.textComponent.color = ControlTextColor;

            if (input.textViewport != null)
            {
                input.textViewport.offsetMin = new Vector2(12f, input.textViewport.offsetMin.y);
                input.textViewport.offsetMax = new Vector2(-10f, input.textViewport.offsetMax.y);
            }
            else if (input.textComponent != null)
            {
                input.textComponent.margin = new Vector4(12f, 0f, 10f, 0f);
            }

            // Stop the game's localized placeholder from coming back.
            DisableLocalization(go);
            if (input.placeholder is TMP_Text placeholder)
            {
                placeholder.text = placeholderText;
                placeholder.color = MutedColor;
                placeholder.fontStyle = FontStyles.Italic;
            }

            input.onValueChanged.AddListener(s => onChanged?.Invoke(s));
            return input;
        }

        private static TMP_InputField TmpSearchInput(Transform parent, string placeholderText, Action<string> onChanged)
        {
            var resources = default(TMP_DefaultControls.Resources);
            resources.inputField = UiSprite();
            var go = TMP_DefaultControls.CreateInputField(resources);
            go.transform.SetParent(parent, false);

            var input = go.GetComponent<TMP_InputField>();
            var img = go.GetComponent<Image>();
            if (img != null)
            {
                img.sprite = RoundedSprite();
                img.type = Image.Type.Sliced;
                img.pixelsPerUnitMultiplier = 1f;
                img.color = ControlColor;
            }

            input.contentType = TMP_InputField.ContentType.Standard;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.text = string.Empty;

            var font = ResolveFont();
            foreach (var t in go.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                t.fontSize = 20f;
                if (font != null)
                    t.font = font;
                t.color = t.gameObject.name.Contains("Placeholder") ? MutedColor : ControlTextColor;
            }
            if (input.placeholder is TMP_Text placeholder)
            {
                placeholder.text = placeholderText;
                placeholder.fontStyle = FontStyles.Italic;
            }

            input.onValueChanged.AddListener(s => onChanged?.Invoke(s));
            return input;
        }

        /// <summary>Builds a vertical scroll view; returns the Content transform to fill.</summary>
        public static RectTransform ScrollView(string name, Transform parent, out ScrollRect scrollRect, out RectTransform root)
        {
            var rootImg = Panel(name, parent, new Color(0f, 0f, 0f, 0.18f), out var rootRt);
            root = rootRt;
            scrollRect = rootImg.gameObject.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 24f;

            var viewport = Rect("Viewport", rootRt, out var viewportRt);
            Stretch(viewportRt);
            var vpImg = viewport.AddComponent<Image>();
            vpImg.color = new Color(1f, 1f, 1f, 0.001f);
            // Use RectMask2D (rect clip) rather than a stencil Mask: the game's
            // custom TMP font material does not render under UI stencil masking,
            // which would make all list text invisible.
            viewport.AddComponent<RectMask2D>();

            var content = Rect("Content", viewportRt, out var contentRt);
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = new Vector2(0f, 0f);

            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 6f;
            vlg.padding = new RectOffset(6, 6, 6, 6);

            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Vertical scrollbar on the right edge (auto-hides + shrinks the
            // viewport when the list overflows). Themed with the muted/accent
            // colours so it matches the rest of the UI.
            var sb = Rect("Scrollbar", rootRt, out var sbRt);
            sbRt.anchorMin = new Vector2(1f, 0f);
            sbRt.anchorMax = new Vector2(1f, 1f);
            sbRt.pivot = new Vector2(1f, 1f);
            sbRt.sizeDelta = new Vector2(10f, 0f);
            // Nudge toward the panel edge so it isn't right up against the content.
            sbRt.anchoredPosition = new Vector2(4f, 0f);
            var sbImg = sb.AddComponent<Image>();
            sbImg.sprite = RoundedSprite();
            sbImg.type = Image.Type.Sliced;
            // ppum 2 keeps the 9-slice corners within the 10px width so the ends
            // render as rounded caps instead of pinched points.
            sbImg.pixelsPerUnitMultiplier = 2f;
            sbImg.color = new Color(1f, 1f, 1f, 0.06f);

            var scrollbar = sb.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;

            var slide = Rect("Sliding Area", sbRt, out var slideRt);
            Stretch(slideRt);
            var handle = Rect("Handle", slideRt, out var handleRt);
            Stretch(handleRt);
            var handleImg = handle.AddComponent<Image>();
            handleImg.sprite = RoundedSprite();
            handleImg.type = Image.Type.Sliced;
            handleImg.pixelsPerUnitMultiplier = 2f;
            handleImg.color = MutedColor;
            scrollbar.handleRect = handleRt;
            scrollbar.targetGraphic = handleImg;

            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            // Positive spacing leaves a clear gap between the content and the bar.
            scrollRect.verticalScrollbarSpacing = 6f;

            scrollRect.viewport = viewportRt;
            scrollRect.content = contentRt;
            return contentRt;
        }

        public static LayoutElement SetSize(GameObject go, float? width, float? height)
        {
            var le = go.GetComponent<LayoutElement>();
            if (le == null)
                le = go.AddComponent<LayoutElement>();
            if (width.HasValue)
            {
                le.preferredWidth = width.Value;
                le.minWidth = width.Value;
            }
            if (height.HasValue)
            {
                le.preferredHeight = height.Value;
                le.minHeight = height.Value;
            }
            return le;
        }

        /// <summary>
        /// Sets a control's width via a high-priority LayoutElement so it overrides
        /// any width a cloned game control sets on itself. Pass a fixed width, or
        /// null + a flexible weight to share space.
        /// </summary>
        public static LayoutElement SetWidth(GameObject go, float? fixedWidth, float flexibleWidth)
        {
            LayoutElement le = null;
            foreach (var e in go.GetComponents<LayoutElement>())
            {
                if (e.layoutPriority >= 5)
                {
                    le = e;
                    break;
                }
            }
            if (le == null)
            {
                le = go.AddComponent<LayoutElement>();
                le.layoutPriority = 5;
            }
            if (fixedWidth.HasValue)
            {
                le.minWidth = fixedWidth.Value;
                le.preferredWidth = fixedWidth.Value;
                le.flexibleWidth = 0f;
            }
            else
            {
                le.minWidth = 0f;
                le.preferredWidth = 0f;
                le.flexibleWidth = flexibleWidth;
            }
            return le;
        }

        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
