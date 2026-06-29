using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SearchAnything
{
    /// <summary>
    /// The Search Anything window. It is a clone of the real City Map filter
    /// panel (so it inherits the game's frame, headline bar, scroll view and
    /// fonts) with the game's filter behaviour stripped out and our own controls
    /// — a search box, a list of matching products and the live list of shops
    /// that sell the selected product — built into its scroll content.
    /// </summary>
    public sealed class SearchAnythingPanel
    {
        private readonly SearchAnythingController _owner;
        private readonly Canvas _canvas;

        private GameObject _window;
        private Canvas _overlayCanvas;
        private RectTransform _content;
        private RectTransform _productsContainer;
        private RectTransform _sellersContainer;
        private TextMeshProUGUI _sellersHeader;
        private TMP_InputField _searchInput;

        private string _query = string.Empty;

        private const int MaxResultsShown = 80;
        private const int MaxSellersShown = 200;

        public GameObject Root => _window;

        /// <summary>The window's RectTransform (null until first built).</summary>
        public RectTransform WindowRect => _window != null ? _window.transform as RectTransform : null;

        public SearchAnythingPanel(SearchAnythingController owner, Canvas canvas)
        {
            _owner = owner;
            _canvas = canvas;
        }

        public bool IsVisible => _window != null && _window.activeSelf;

        public void Show()
        {
            if (_window == null)
                Build();
            if (_window == null)
                return;

            _window.SetActive(true);
            RebuildResults();
            RebuildWhereToFind();
        }

        public void Hide()
        {
            if (_window != null)
                _window.SetActive(false);
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
            if (_window != null)
                Object.Destroy(_window);
            _window = null;

            if (_overlayCanvas != null)
                Object.Destroy(_overlayCanvas.gameObject);
            _overlayCanvas = null;
        }

        // -- Window construction (clone of the real panel) ------------------

        private void Build()
        {
            var source = _owner.PanelObject;
            if (source == null)
                return;

            // Guard against a destroyed/missing canvas (e.g. the city scene was
            // reloaded); without this _canvas.transform throws an NRE.
            if (_canvas == null)
                return;

            // Instantiate while parented to an inactive holder so none of the
            // game's panel scripts (which would hide or repopulate it) ever run.
            var holder = new GameObject("SA_Holder");
            holder.SetActive(false);

            _window = Object.Instantiate(source, holder.transform);
            _window.name = "SearchAnythingWindow";

            StripGameBehaviour();

            // Re-home onto our own dedicated, top-most overlay canvas (built fresh
            // so it isn't nested under the game's map canvas — a nested canvas
            // can't out-sort UI on a higher sorting layer, which is why the window
            // was being covered). Then drop the inactive holder.
            _window.transform.SetParent(GetOverlayRoot().transform, false);
            Object.Destroy(holder);

            SetUpContent();
            SetUpHeader();
            SetUpDragging();
            BuildCloseButton();

            _window.SetActive(true);

            BuildSections();
            PositionWindow();
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
        /// Places the window at a deterministic centre-screen position with a
        /// fixed size. The clone source (the City Map filter panel) may never have
        /// been laid out by the game's draggable-window system when we open from
        /// outside the map, so we can't trust its inherited geometry. Logs the
        /// resulting rect so layout issues can be diagnosed from the player log.
        /// </summary>
        private void PositionWindow()
        {
            var rt = _window.transform as RectTransform;
            if (rt == null)
                return;

            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            // Size relative to the canvas so the window is large on any screen
            // while still leaving a margin around it.
            float w = 820f, h = 1000f;
            var sizeCanvas = _overlayCanvas != null ? _overlayCanvas : _canvas;
            var canvasRt = sizeCanvas != null ? sizeCanvas.transform as RectTransform : null;
            if (canvasRt != null && canvasRt.rect.width > 1f && canvasRt.rect.height > 1f)
            {
                w = Mathf.Clamp(canvasRt.rect.width * 0.46f, 760f, 1000f);
                h = Mathf.Clamp(canvasRt.rect.height * 0.94f, 820f, 1400f);
            }
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = Vector2.zero;
            rt.localScale = Vector3.one;

            var parentCanvas = _canvas;
            Debug.Log(
                $"[Mod:SearchAnything] window geometry: active={_window.activeInHierarchy} " +
                $"sizeDelta={rt.sizeDelta} anchoredPos={rt.anchoredPosition} rect={rt.rect} " +
                $"parentCanvas={(parentCanvas != null ? parentCanvas.name : "null")} " +
                $"renderMode={(parentCanvas != null ? parentCanvas.renderMode.ToString() : "n/a")}");
        }

        /// <summary>
        /// A rounded square close (X) button on the headline's right edge.
        /// </summary>
        private void BuildCloseButton()
        {
            var headline = UiFactory.FindDeep(_window.transform, "Headline");
            var parent = headline != null ? headline : _window.transform;

            var img = UiFactory.Panel("CloseButton", parent, new Color(0.78f, 0.16f, 0.16f, 1f), out var rt);
            img.sprite = UiFactory.RoundedSprite();
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 1f;

            var le = img.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(50f, 50f);
            rt.anchoredPosition = new Vector2(-24f, 0f);
            rt.SetAsLastSibling();

            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var x = UiFactory.Text("X", img.transform, 24f, UiFactory.TextColor, TextAlignmentOptions.Center);
            UiFactory.Stretch(x.rectTransform);
            btn.onClick.AddListener(() => _owner.RequestClose());
        }

        /// <summary>Removes the game's filter logic and leftover contents from the clone.</summary>
        private void StripGameBehaviour()
        {
            var cmf = _window.GetComponent<CityMapFilters>();
            if (cmf != null)
            {
                RemoveCloseButton(cmf);
                Object.DestroyImmediate(cmf);
            }

            // Drop our own injected headline button if it got cloned along.
            foreach (var t in _window.GetComponentsInChildren<Transform>(true))
            {
                if (t != null && t != _window.transform && t.name == "SearchAnythingButton")
                {
                    Object.DestroyImmediate(t.gameObject);
                    break;
                }
            }

            // Remove the vanilla top controls (search box + enable-all switch).
            var topControls = UiFactory.FindDeep(_window.transform, "Top Controls");
            if (topControls != null)
                Object.DestroyImmediate(topControls.gameObject);

            // Sweep up any leftover game buttons that live outside the scroll
            // content (e.g. the orange "Close" bar at the bottom). Our own
            // controls are built later, so nothing we need exists yet.
            var scrollContent = _window.GetComponentInChildren<ScrollRect>(true)?.content;
            foreach (var btn in _window.GetComponentsInChildren<Button>(true))
            {
                if (btn == null || btn.transform == _window.transform)
                    continue;
                if (scrollContent != null && btn.transform.IsChildOf(scrollContent))
                    continue;
                Object.DestroyImmediate(btn.gameObject);
            }

            // Stop localized labels from overwriting our custom text.
            UiFactory.DisableLocalization(_window);
        }

        /// <summary>
        /// Removes the panel's "Close Voogle Maps [ESC]" button, found via the
        /// game's private <c>closeLabel</c> reference on the cloned component.
        /// </summary>
        private void RemoveCloseButton(CityMapFilters cmf)
        {
            var field = typeof(CityMapFilters).GetField(
                "closeLabel",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field == null)
                return;

            if (field.GetValue(cmf) is Component label && label != null)
            {
                var button = label.GetComponentInParent<Button>();
                var target = button != null ? button.gameObject : label.gameObject;
                Object.DestroyImmediate(target);
            }
        }

        private void SetUpContent()
        {
            var scroll = _window.GetComponentInChildren<ScrollRect>(true);
            _content = scroll != null ? scroll.content : null;
            if (_content == null)
                return;

            // Tighten the window margins so content isn't floating in the middle
            // of a large frame. Leave room at the top for the headline bar.
            var scrollRt = scroll.GetComponent<RectTransform>();
            if (scrollRt != null)
            {
                float topInset = 64f;
                if (UiFactory.FindDeep(_window.transform, "Headline") is RectTransform headlineRt
                    && headlineRt.rect.height > 1f)
                    topInset = headlineRt.rect.height + 6f;

                scrollRt.anchorMin = Vector2.zero;
                scrollRt.anchorMax = Vector2.one;
                scrollRt.offsetMin = new Vector2(20f, 20f);
                scrollRt.offsetMax = new Vector2(-20f, -topInset);
                if (scroll.viewport != null)
                    UiFactory.Stretch(scroll.viewport);
            }

            // Normalise the cloned content so it spans the viewport edge-to-edge.
            _content.anchorMin = new Vector2(0f, _content.anchorMin.y);
            _content.anchorMax = new Vector2(1f, _content.anchorMax.y);
            _content.offsetMin = new Vector2(0f, _content.offsetMin.y);
            _content.offsetMax = new Vector2(0f, _content.offsetMax.y);

            // Clear the cloned filter rows / templates.
            for (int i = _content.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(_content.GetChild(i).gameObject);

            var vlg = _content.GetComponent<VerticalLayoutGroup>();
            if (vlg == null)
                vlg = _content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 6f;
            vlg.padding = new RectOffset(2, 2, 4, 4);

            var fitter = _content.GetComponent<ContentSizeFitter>();
            if (fitter == null)
                fitter = _content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private void SetUpHeader()
        {
            var headline = UiFactory.FindDeep(_window.transform, "Headline");
            if (headline == null)
                return;

            // Keep the cloned label's original colour (dark, to suit the light
            // headline bar) — only change the text.
            var label = headline.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
            {
                label.text = "SEARCH ANYTHING";
                label.fontStyle = FontStyles.Normal;
            }
        }

        private void SetUpDragging()
        {
            var headline = UiFactory.FindDeep(_window.transform, "Headline");
            var handleTarget = headline != null ? headline.gameObject : _window;

            var graphic = handleTarget.GetComponent<Graphic>();
            if (graphic != null)
            {
                graphic.raycastTarget = true;
            }
            else
            {
                var hit = handleTarget.AddComponent<Image>();
                hit.color = new Color(1f, 1f, 1f, 0.004f);
                hit.raycastTarget = true;
            }

            var drag = handleTarget.AddComponent<WindowDragHandler>();
            drag.Target = _window.GetComponent<RectTransform>();
            drag.Canvas = _overlayCanvas != null ? _overlayCanvas : _canvas;
        }

        private void BuildSections()
        {
            if (_content == null)
                return;

            BuildSearchRow();

            var productsLabel = UiFactory.Text("Results", _content, 23f, UiFactory.TextColor);
            UiFactory.SetSize(productsLabel.gameObject, null, 28f);

            _productsContainer = BuildContainer("Products");

            _sellersHeader = UiFactory.Text("Where to find", _content, 23f, UiFactory.TextColor);
            UiFactory.SetSize(_sellersHeader.gameObject, null, 28f);

            _sellersContainer = BuildContainer("SellersList");
        }

        private RectTransform BuildContainer(string name)
        {
            UiFactory.Rect(name, _content, out var rt);
            var vlg = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 5f;

            var fitter = rt.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rt;
        }

        private void BuildSearchRow()
        {
            _searchInput = UiFactory.SearchInput(_content, "Search for anything...", query =>
            {
                _query = query ?? string.Empty;
                RebuildResults();
                RebuildWhereToFind();
            });
            UiFactory.SetSize(_searchInput.gameObject, null, 56f);

            // Put the field on the UI layer so the game treats it as a focused UI
            // input and suppresses its own keyboard shortcuts / player movement
            // while the player is typing here (GameManager.HasInputSelected only
            // recognises controls on the UI layer).
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0)
                _searchInput.gameObject.layer = uiLayer;
        }

        // -- Results list (products + places) -------------------------------

        public void RebuildResults()
        {
            if (_productsContainer == null)
                return;

            for (int i = _productsContainer.childCount - 1; i >= 0; i--)
                Object.Destroy(_productsContainer.GetChild(i).gameObject);

            if (string.IsNullOrWhiteSpace(_query))
            {
                var hint = UiFactory.Text(
                    $"Start typing to search. {_owner.TotalIndexed} items indexed across the city.",
                    _productsContainer, 18f, UiFactory.MutedColor);
                UiFactory.SetSize(hint.gameObject, null, 30f);
                hint.alignment = TextAlignmentOptions.MidlineLeft;
                hint.enableWordWrapping = true;
                return;
            }

            var matches = _owner.Search(_query);
            if (matches.Count == 0)
            {
                var none = UiFactory.Text("No matches found.", _productsContainer, 18f, UiFactory.MutedColor);
                UiFactory.SetSize(none.gameObject, null, 30f);
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
                // Place: name on top with a detail line below (size, type,
                // neighbourhood and who owns/rents it) so the player can see why
                // it matched. The matching text is highlighted live as they type.
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

                string detail = BuildLocationDetail(result);
                if (!string.IsNullOrEmpty(detail))
                {
                    var detailText = UiFactory.Text(Highlight(detail), block.transform, 14f, UiFactory.MutedColor);
                    UiFactory.SetSize(detailText.gameObject, null, 18f);
                }
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

        /// <summary>Builds the place's detail line: size, type, neighbourhood and ownership.</summary>
        private static string BuildLocationDetail(SearchResult r)
        {
            var parts = new List<string>(4);
            if (!string.IsNullOrEmpty(r.SizeCode))
                parts.Add(r.SizeCode);
            if (!string.IsNullOrEmpty(r.TypeLabel))
                parts.Add(r.TypeLabel);
            if (!string.IsNullOrEmpty(r.Neighbourhood))
                parts.Add(r.Neighbourhood);
            if (!string.IsNullOrEmpty(r.Ownership))
                parts.Add(r.Ownership);
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
                Object.Destroy(_sellersContainer.GetChild(i).gameObject);

            if (string.IsNullOrEmpty(_owner.SelectedId))
            {
                if (_sellersHeader != null)
                    _sellersHeader.text = "Where to find";
                var hint = UiFactory.Text("Select a result to see where it is.",
                    _sellersContainer, 18f, UiFactory.MutedColor);
                UiFactory.SetSize(hint.gameObject, null, 30f);
                return;
            }

            var places = _owner.WhereToFind;
            if (_sellersHeader != null)
                _sellersHeader.text = $"Where to find: {places.Count}";

            if (places.Count == 0)
            {
                var none = UiFactory.Text("No locations found.",
                    _sellersContainer, 18f, UiFactory.MutedColor);
                UiFactory.SetSize(none.gameObject, null, 30f);
                return;
            }

            int max = Mathf.Min(places.Count, MaxSellersShown);
            for (int i = 0; i < max; i++)
                BuildSellerRow(places[i]);
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
