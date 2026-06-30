using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SearchAnything
{
    /// <summary>
    /// The Search Anything UI. Rather than a fixed window, it is a set of small
    /// floating, frameless panels on a top-most overlay canvas: a draggable
    /// search bar, a results list that grows directly beneath it, and a "where to
    /// find" list that appears to its right once a result is selected. Each list
    /// is only as tall as its content (up to a cap, after which it scrolls), so
    /// the mod's on-screen footprint stays as small as possible.
    /// </summary>
    public sealed class SearchAnythingPanel
    {
        private readonly SearchAnythingController _owner;
        private readonly Canvas _canvas;

        private RectTransform _group;
        private Canvas _overlayCanvas;
        private RectTransform _resultsPanel;
        private RectTransform _wherePanel;
        private RectTransform _productsContainer;
        private RectTransform _sellersContainer;
        private TextMeshProUGUI _sellersHeader;
        private TMP_InputField _searchInput;

        private float _titleBarHeight = TitleHeight;

        private string _query = string.Empty;

        private const int MaxResultsShown = 80;
        private const int MaxSellersShown = 200;

        // Floating layout sizes (kept compact so the mod's UI footprint stays small).
        private const float SearchWidth = 540f;
        private const float SearchHeight = 58f;
        private const float TitleHeight = 56f;
        private const float ResultsWidth = 540f;
        private const float WhereWidth = 440f;
        private const float Gap = 8f;
        private const float HeaderHeight = 26f;
        private const float PanelPad = 8f;

        public GameObject Root => _group != null ? _group.gameObject : null;

        /// <summary>The floating group's RectTransform (null until first built).</summary>
        public RectTransform WindowRect => _group;

        public SearchAnythingPanel(SearchAnythingController owner, Canvas canvas)
        {
            _owner = owner;
            _canvas = canvas;
        }

        public bool IsVisible => _group != null && _group.gameObject.activeSelf;

        public void Show()
        {
            if (_group == null)
                Build();
            if (_group == null)
                return;

            _group.gameObject.SetActive(true);
            RebuildResults();
            RebuildWhereToFind();
        }

        public void Hide()
        {
            if (_group != null)
                _group.gameObject.SetActive(false);
        }

        /// <summary>
        /// Gives keyboard focus to the search box so the player can type straight
        /// away without clicking into it. Selects any existing text so the first
        /// keystroke starts a fresh search.
        /// </summary>
        public void FocusSearch()
        {
            if (_searchInput == null)
                return;

            if (UnityEngine.EventSystems.EventSystem.current != null)
                UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(_searchInput.gameObject);

            _searchInput.ActivateInputField();
            _searchInput.Select();
            if (!string.IsNullOrEmpty(_searchInput.text))
            {
                _searchInput.selectionAnchorPosition = 0;
                _searchInput.selectionFocusPosition = _searchInput.text.Length;
            }
        }

        public void Destroy()
        {
            if (_group != null)
                Object.Destroy(_group.gameObject);
            _group = null;

            if (_overlayCanvas != null)
                Object.Destroy(_overlayCanvas.gameObject);
            _overlayCanvas = null;
        }

        // -- Floating UI construction ---------------------------------------

        /// <summary>
        /// Builds the floating, frameless UI: a draggable search bar with the
        /// results list growing directly beneath it, and a "where to find" list
        /// that appears to its right once a result is selected. Nothing is housed
        /// in a fixed window, and each list is only as tall as its content (up to
        /// a cap, after which it scrolls) so the mod's on-screen footprint stays
        /// as small as possible.
        /// </summary>
        private void Build()
        {
            // Guard against a destroyed/missing canvas (e.g. the city scene was
            // reloaded); without this we'd build onto nothing.
            if (_canvas == null)
                return;

            var overlay = GetOverlayRoot();
            if (overlay == null)
                return;

            // The group is an invisible anchor near the top of the screen that all
            // three floating pieces hang from; dragging the search bar moves it.
            UiFactory.Rect("SearchAnythingGroup", overlay.transform, out _group);
            _group.anchorMin = new Vector2(0.5f, 1f);
            _group.anchorMax = new Vector2(0.5f, 1f);
            _group.pivot = new Vector2(0.5f, 1f);
            _group.sizeDelta = Vector2.zero;
            _group.anchoredPosition = new Vector2(0f, -48f);

            BuildTitleBar();
            BuildSearchBar();
            BuildResultsPanel();
            BuildWherePanel();
        }

        /// <summary>
        /// Builds (once) a dedicated, independent screen-space-overlay canvas that
        /// the window lives under. Being its own root canvas on the top sorting
        /// layer with the maximum sorting order guarantees it draws above every
        /// other game UI layer (HUD, smartphone, dialogs). Its scaler is copied
        /// from the game canvas so our controls keep the same scale.
        /// </summary>
        private Canvas GetOverlayRoot()
        {
            if (_overlayCanvas != null)
                return _overlayCanvas;

            var go = new GameObject("SearchAnythingOverlay");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 32767;

            // Draw on the highest available sorting layer so nothing on a layer
            // above "Default" can cover us.
            var layers = SortingLayer.layers;
            if (layers != null && layers.Length > 0)
                canvas.sortingLayerID = layers[layers.Length - 1].id;

            // Match the game canvas's scaler so sizes/fonts render identically.
            var scaler = go.AddComponent<CanvasScaler>();
            var srcScaler = _canvas != null ? _canvas.GetComponentInParent<CanvasScaler>() : null;
            if (srcScaler != null)
            {
                scaler.uiScaleMode = srcScaler.uiScaleMode;
                scaler.referenceResolution = srcScaler.referenceResolution;
                scaler.screenMatchMode = srcScaler.screenMatchMode;
                scaler.matchWidthOrHeight = srcScaler.matchWidthOrHeight;
                scaler.scaleFactor = srcScaler.scaleFactor;
                scaler.referencePixelsPerUnit = srcScaler.referencePixelsPerUnit;
            }
            else
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
            }

            go.AddComponent<GraphicRaycaster>();

            _overlayCanvas = canvas;
            return _overlayCanvas;
        }

        /// <summary>
        /// Makes the whole floating group draggable from the search bar's
        /// background, so the player can reposition the search-and-results stack
        /// anywhere on screen.
        /// </summary>
        private void SetUpDragging(GameObject handle)
        {
            var graphic = handle.GetComponent<Graphic>();
            if (graphic != null)
                graphic.raycastTarget = true;

            var drag = handle.AddComponent<WindowDragHandler>();
            drag.Target = _group;
            drag.Canvas = _overlayCanvas != null ? _overlayCanvas : _canvas;
        }

        /// <summary>
        /// Builds a simple, self-made title bar above the search box (we no longer
        /// clone the game headline — that proved fragile). It is styled as a light
        /// header to sit with the game's theme, shows "SEARCH ANYTHING", and acts
        /// as the grab handle. The game UI is still cloned, but only for the text
        /// input field itself.
        /// </summary>
        private void BuildTitleBar()
        {
            _titleBarHeight = TitleHeight;

            var headerColor = new Color(0.82f, 0.85f, 0.90f, 1f);
            var img = UiFactory.Panel("TitleBar", _group, headerColor, out var rt);
            img.sprite = UiFactory.RoundedTopSprite();
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 1f;
            img.raycastTarget = true;
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(SearchWidth, _titleBarHeight);
            rt.anchoredPosition = Vector2.zero;

            var label = UiFactory.Text("SEARCH ANYTHING", rt, 22f,
                UiFactory.ControlTextColor, TextAlignmentOptions.MidlineLeft);
            label.fontStyle = FontStyles.Bold;
            var lrt = label.rectTransform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(16f, 0f);
            lrt.offsetMax = new Vector2(-58f, 0f);

            SetUpDragging(img.gameObject);
            AddCloseButton(img.transform);
        }

        /// <summary>A red close (X) button anchored to the right of the title bar.</summary>
        private void AddCloseButton(Transform parent)
        {
            var img = UiFactory.Panel("CloseButton", parent, new Color(0.78f, 0.16f, 0.16f, 1f), out var rt);
            img.sprite = UiFactory.RoundedSprite();
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 1f;

            var le = img.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(42f, 42f);
            rt.anchoredPosition = new Vector2(-10f, 0f);
            rt.SetAsLastSibling();

            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var x = UiFactory.Text("X", img.transform, 26f, UiFactory.TextColor, TextAlignmentOptions.Center);
            UiFactory.Stretch(x.rectTransform);
            btn.onClick.AddListener(() => _owner.RequestClose());
        }

        /// <summary>
        /// Builds the floating search bar (rounded background + search field +
        /// close button), anchored just below the title bar.
        /// </summary>
        private void BuildSearchBar()
        {
            var barImg = UiFactory.Panel("SearchBar", _group, UiFactory.PanelColor, out var barRt);
            // Square top so the bar sits flush under the title bar; rounded bottom.
            barImg.sprite = UiFactory.RoundedBottomSprite();
            barImg.type = Image.Type.Sliced;
            barImg.pixelsPerUnitMultiplier = 1f;
            barImg.raycastTarget = true;
            barRt.anchorMin = new Vector2(0.5f, 1f);
            barRt.anchorMax = new Vector2(0.5f, 1f);
            barRt.pivot = new Vector2(0.5f, 1f);
            barRt.sizeDelta = new Vector2(SearchWidth, SearchHeight);
            barRt.anchoredPosition = new Vector2(0f, -_titleBarHeight);

            var hl = barImg.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.childControlWidth = true;
            hl.childControlHeight = true;
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = true;
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.spacing = 8f;
            hl.padding = new RectOffset(10, 10, 8, 8);

            // Search field fills the bar (the close button now lives in the title bar).
            _searchInput = UiFactory.SearchInput(barImg.transform, "Search for anything...", query =>
            {
                _query = query ?? string.Empty;
                RebuildResults();
                RebuildWhereToFind();
            });
            UiFactory.SetWidth(_searchInput.gameObject, null, 1f);

            // Put the field on the UI layer so the game treats it as a focused UI
            // input and suppresses its own keyboard shortcuts / player movement
            // while the player is typing here (GameManager.HasInputSelected only
            // recognises controls on the UI layer).
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0)
                _searchInput.gameObject.layer = uiLayer;
        }

        /// <summary>Builds the results list panel directly beneath the search bar.</summary>
        private void BuildResultsPanel()
        {
            _resultsPanel = BuildFloatingList("Results", ResultsWidth, out _productsContainer, out _);
            _resultsPanel.anchorMin = new Vector2(0.5f, 1f);
            _resultsPanel.anchorMax = new Vector2(0.5f, 1f);
            _resultsPanel.pivot = new Vector2(0.5f, 1f);
            _resultsPanel.anchoredPosition = new Vector2(0f, -(_titleBarHeight + SearchHeight + Gap));
        }

        /// <summary>
        /// Builds the "where to find" panel that floats to the right of the
        /// results list. Stays hidden until a result is selected.
        /// </summary>
        private void BuildWherePanel()
        {
            _wherePanel = BuildFloatingList("Where to find", WhereWidth, out _sellersContainer, out _sellersHeader);
            _wherePanel.anchorMin = new Vector2(0.5f, 1f);
            _wherePanel.anchorMax = new Vector2(0.5f, 1f);
            _wherePanel.pivot = new Vector2(0f, 1f);
            _wherePanel.anchoredPosition = new Vector2(ResultsWidth * 0.5f + Gap, -(_titleBarHeight + SearchHeight + Gap));
            _wherePanel.gameObject.SetActive(false);
        }

        /// <summary>
        /// Builds a frameless floating list panel: a rounded background, a small
        /// header label and a scroll view whose content the caller fills. The
        /// panel height is set dynamically by <see cref="FitPanel"/> so it is only
        /// as tall as its content (up to a cap, after which it scrolls).
        /// </summary>
        private RectTransform BuildFloatingList(string title, float width, out RectTransform content, out TextMeshProUGUI header)
        {
            var panelImg = UiFactory.Panel(title + "Panel", _group, UiFactory.PanelColor, out var panelRt);
            panelImg.sprite = UiFactory.RoundedSprite();
            panelImg.type = Image.Type.Sliced;
            panelImg.pixelsPerUnitMultiplier = 1f;
            panelRt.sizeDelta = new Vector2(width, HeaderHeight + 2f * PanelPad);

            header = UiFactory.Text(title, panelRt, 17f, UiFactory.MutedColor);
            var headerRt = header.rectTransform;
            headerRt.anchorMin = new Vector2(0f, 1f);
            headerRt.anchorMax = new Vector2(1f, 1f);
            headerRt.pivot = new Vector2(0.5f, 1f);
            headerRt.sizeDelta = new Vector2(-2f * PanelPad - 4f, HeaderHeight);
            headerRt.anchoredPosition = new Vector2(0f, -PanelPad);

            var contentRt = UiFactory.ScrollView(title + "Scroll", panelRt, out _, out var scrollRoot);
            scrollRoot.anchorMin = Vector2.zero;
            scrollRoot.anchorMax = Vector2.one;
            scrollRoot.offsetMin = new Vector2(PanelPad, PanelPad);
            scrollRoot.offsetMax = new Vector2(-PanelPad, -(PanelPad + HeaderHeight + 4f));

            content = contentRt;
            return panelRt;
        }

        /// <summary>
        /// Sizes a floating list panel to its content height (clamped to a cap so
        /// long lists scroll instead of filling the screen), keeping the mod's
        /// footprint as small as the current results allow.
        /// </summary>
        private void FitPanel(RectTransform panel, RectTransform content)
        {
            if (panel == null || content == null)
                return;

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            float contentH = LayoutUtility.GetPreferredHeight(content);

            float maxList = Mathf.Max(160f, ScreenHeight() * 0.6f);
            float listH = Mathf.Clamp(contentH, 0f, maxList);
            panel.sizeDelta = new Vector2(panel.sizeDelta.x, HeaderHeight + listH + 2f * PanelPad + 4f);
        }

        /// <summary>Height of the overlay canvas (falls back to the screen height).</summary>
        private float ScreenHeight()
        {
            var canvasRt = _overlayCanvas != null ? _overlayCanvas.transform as RectTransform : null;
            if (canvasRt != null && canvasRt.rect.height > 1f)
                return canvasRt.rect.height;
            return Screen.height;
        }

        // -- Results list (products + places) -------------------------------

        public void RebuildResults()
        {
            if (_productsContainer == null)
                return;

            for (int i = _productsContainer.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(_productsContainer.GetChild(i).gameObject);

            if (string.IsNullOrWhiteSpace(_query))
            {
                var hint = UiFactory.Text(
                    $"{_owner.TotalIndexed} items indexed",
                    _productsContainer, 18f, UiFactory.MutedColor);
                UiFactory.SetSize(hint.gameObject, null, 30f);
                hint.alignment = TextAlignmentOptions.MidlineLeft;
                hint.enableWordWrapping = true;
                FitPanel(_resultsPanel, _productsContainer);
                return;
            }

            var matches = _owner.Search(_query);
            if (matches.Count == 0)
            {
                var none = UiFactory.Text("No matches found.", _productsContainer, 18f, UiFactory.MutedColor);
                UiFactory.SetSize(none.gameObject, null, 30f);
                FitPanel(_resultsPanel, _productsContainer);
                return;
            }

            // A single match selects itself so the list fills in immediately.
            if (matches.Count == 1)
                _owner.SelectResult(matches[0]);

            int max = Mathf.Min(matches.Count, MaxResultsShown);
            for (int i = 0; i < max; i++)
                BuildResultRow(matches[i]);

            if (matches.Count > max)
            {
                var more = UiFactory.Text($"+ {matches.Count - max} more — keep typing to narrow it down.",
                    _productsContainer, 16f, UiFactory.MutedColor);
                UiFactory.SetSize(more.gameObject, null, 26f);
            }

            FitPanel(_resultsPanel, _productsContainer);
        }

        private void BuildResultRow(SearchResult result)
        {
            bool selected = result.Id == _owner.SelectedId;
            var rowImg = UiFactory.Panel("ResultRow", _productsContainer,
                selected ? UiFactory.ToggleOnColor : UiFactory.RowColor, out _);
            rowImg.sprite = UiFactory.RoundedSprite();
            rowImg.type = Image.Type.Sliced;
            rowImg.pixelsPerUnitMultiplier = 1f;
            UiFactory.SetSize(rowImg.gameObject, null, 52f);

            var btn = rowImg.gameObject.AddComponent<Button>();
            btn.targetGraphic = rowImg;

            var hl = rowImg.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.childControlWidth = true;
            hl.childControlHeight = true;
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = true;
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.spacing = 8f;
            hl.padding = new RectOffset(8, 10, 4, 4);

            // Icon: the product image, or the place's map-pin / type icon.
            var iconSprite = result.Kind == ResultKind.Product
                ? _owner.GetProductIcon(result.ItemName)
                : _owner.GetLocationIcon(result.Controller);
            var icon = UiFactory.Panel("Icon", rowImg.transform, Color.white, out _);
            icon.sprite = iconSprite;
            icon.type = Image.Type.Simple;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            icon.enabled = iconSprite != null;
            UiFactory.SetWidth(icon.gameObject, 38f, 0f);

            if (result.Kind == ResultKind.Product)
            {
                // Name (matched text highlighted live).
                var name = UiFactory.Text(Highlight(result.DisplayName), rowImg.transform, 19f, UiFactory.TextColor);
                UiFactory.SetWidth(name.gameObject, null, 1f);

                if (result.Price >= 0f)
                {
                    var price = UiFactory.Text($"${result.Price:0.##}", rowImg.transform, 18f,
                        UiFactory.AccentColor, TextAlignmentOptions.MidlineRight);
                    UiFactory.SetWidth(price.gameObject, 84f, 0f);
                }

                string countLabel = result.SellerCount == 1 ? "1 shop" : $"{result.SellerCount} shops";
                var count = UiFactory.Text(countLabel, rowImg.transform, 17f, UiFactory.MutedColor,
                    TextAlignmentOptions.MidlineRight);
                UiFactory.SetWidth(count.gameObject, 78f, 0f);
            }
            else
            {
                // Place: name on top, then a detail line (size, type,
                // neighbourhood) and a separate ownership line below — these don't
                // fit on one line in the narrow panel. Matching text is
                // highlighted live as the player types.
                var block = UiFactory.Rect("Text", rowImg.transform, out _);
                var bvlg = block.AddComponent<VerticalLayoutGroup>();
                bvlg.childControlWidth = true;
                bvlg.childControlHeight = true;
                bvlg.childForceExpandWidth = true;
                bvlg.childForceExpandHeight = false;
                bvlg.spacing = 0f;
                bvlg.childAlignment = TextAnchor.MiddleLeft;
                UiFactory.SetWidth(block, null, 1f);

                var name = UiFactory.Text(Highlight(result.DisplayName), block.transform, 18f, UiFactory.TextColor);
                UiFactory.SetSize(name.gameObject, null, 22f);

                float rowHeight = 8f + 22f;

                string detail = BuildLocationDetail(result);
                if (!string.IsNullOrEmpty(detail))
                {
                    var detailText = UiFactory.Text(Highlight(detail), block.transform, 14f, UiFactory.MutedColor);
                    UiFactory.SetSize(detailText.gameObject, null, 18f);
                    rowHeight += 18f;
                }

                if (!string.IsNullOrEmpty(result.Ownership))
                {
                    var ownerText = UiFactory.Text(Highlight(result.Ownership), block.transform, 14f, UiFactory.MutedColor);
                    UiFactory.SetSize(ownerText.gameObject, null, 18f);
                    rowHeight += 18f;
                }

                UiFactory.SetSize(rowImg.gameObject, null, Mathf.Max(52f, rowHeight));
            }

            // Clicking a result only selects it (filling the bottom list). Jumping
            // to the map is done from the bottom "Where to find" list, for both
            // products and places — keeping that action in one consistent place.
            btn.onClick.AddListener(() =>
            {
                _owner.SelectResult(result);
                RebuildResults();
                RebuildWhereToFind();
            });
        }

        /// <summary>Builds the place's detail line: size, type and neighbourhood (ownership is shown separately).</summary>
        private static string BuildLocationDetail(SearchResult r)
        {
            var parts = new List<string>(3);
            if (!string.IsNullOrEmpty(r.SizeCode))
                parts.Add(r.SizeCode);
            if (!string.IsNullOrEmpty(r.TypeLabel))
                parts.Add(r.TypeLabel);
            if (!string.IsNullOrEmpty(r.Neighbourhood))
                parts.Add(r.Neighbourhood);
            return string.Join("  \u00b7  ", parts);
        }

        /// <summary>The current query split into lower-cased word tokens.</summary>
        private string[] QueryTokens()
        {
            return SearchQuery.Tokenize(_query);
        }

        /// <summary>
        /// Wraps every part of <paramref name="text"/> that matches a query token
        /// in an amber, bold rich-text span so the player can see exactly what is
        /// being matched as they type.
        /// </summary>
        private string Highlight(string text)
        {
            var tokens = QueryTokens();
            if (string.IsNullOrEmpty(text) || tokens.Length == 0)
                return text;

            string lower = text.ToLowerInvariant();
            var mark = new bool[text.Length];
            foreach (var token in tokens)
            {
                if (string.IsNullOrEmpty(token))
                    continue;
                int idx = 0;
                while ((idx = lower.IndexOf(token, idx, System.StringComparison.Ordinal)) >= 0)
                {
                    for (int k = idx; k < idx + token.Length && k < mark.Length; k++)
                        mark[k] = true;
                    idx += token.Length;
                }
            }

            var sb = new System.Text.StringBuilder(text.Length + 24);
            bool open = false;
            for (int i = 0; i < text.Length; i++)
            {
                if (mark[i] && !open)
                {
                    sb.Append("<color=#FFD54A><b>");
                    open = true;
                }
                else if (!mark[i] && open)
                {
                    sb.Append("</b></color>");
                    open = false;
                }
                sb.Append(text[i]);
            }
            if (open)
                sb.Append("</b></color>");
            return sb.ToString();
        }

        // -- Where to find list (jump targets) ------------------------------

        public void RebuildWhereToFind()
        {
            if (_sellersContainer == null)
                return;

            for (int i = _sellersContainer.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(_sellersContainer.GetChild(i).gameObject);

            // No selection: hide the panel entirely so it takes up no space.
            if (string.IsNullOrEmpty(_owner.SelectedId))
            {
                if (_wherePanel != null)
                    _wherePanel.gameObject.SetActive(false);
                return;
            }

            if (_wherePanel != null)
                _wherePanel.gameObject.SetActive(true);

            var places = _owner.WhereToFind;
            if (_sellersHeader != null)
                _sellersHeader.text = $"Where to find: {places.Count}";

            if (places.Count == 0)
            {
                var none = UiFactory.Text("No locations found.",
                    _sellersContainer, 18f, UiFactory.MutedColor);
                UiFactory.SetSize(none.gameObject, null, 30f);
                FitPanel(_wherePanel, _sellersContainer);
                return;
            }

            int max = Mathf.Min(places.Count, MaxSellersShown);
            for (int i = 0; i < max; i++)
                BuildSellerRow(places[i]);

            FitPanel(_wherePanel, _sellersContainer);
        }

        private void BuildSellerRow(SellerInfo seller)
        {
            var rowImg = UiFactory.Panel("SellerRow", _sellersContainer, UiFactory.RowColor, out _);
            rowImg.sprite = UiFactory.RoundedSprite();
            rowImg.type = Image.Type.Sliced;
            rowImg.pixelsPerUnitMultiplier = 1f;
            UiFactory.SetSize(rowImg.gameObject, null, 46f);

            var btn = rowImg.gameObject.AddComponent<Button>();
            btn.targetGraphic = rowImg;

            var hl = rowImg.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.childControlWidth = true;
            hl.childControlHeight = true;
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = true;
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.spacing = 6f;
            hl.padding = new RectOffset(10, 10, 3, 3);

            // Wholesale (box) sellers get a cardboard-box icon so they stand out
            // from by-the-piece retail shops.
            if (seller.IsWholesale)
            {
                var boxSprite = _owner.WholesaleBoxIcon;
                var box = UiFactory.Panel("BoxIcon", rowImg.transform, Color.white, out _);
                box.sprite = boxSprite;
                box.type = Image.Type.Simple;
                box.preserveAspect = true;
                box.raycastTarget = false;
                box.enabled = boxSprite != null;
                UiFactory.SetWidth(box.gameObject, 30f, 0f);
            }

            // Shop name.
            var name = UiFactory.Text(seller.DisplayName, rowImg.transform, 18f, UiFactory.TextColor);
            UiFactory.SetWidth(name.gameObject, null, 1f);

            // Neighbourhood.
            if (!string.IsNullOrEmpty(seller.Neighbourhood))
            {
                var hood = UiFactory.Text(seller.Neighbourhood, rowImg.transform, 16f, UiFactory.MutedColor,
                    TextAlignmentOptions.MidlineRight);
                UiFactory.SetWidth(hood.gameObject, 140f, 0f);
            }

            // Price, when the shop lists one.
            if (seller.Price >= 0f)
            {
                var price = UiFactory.Text($"${seller.Price:0.##}", rowImg.transform, 18f, UiFactory.AccentColor,
                    TextAlignmentOptions.MidlineRight);
                UiFactory.SetWidth(price.gameObject, 80f, 0f);
            }

            var captured = seller;
            btn.onClick.AddListener(() => _owner.FocusBuilding(captured));
        }
    }
}
