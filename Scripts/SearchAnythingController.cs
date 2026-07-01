using System.Collections.Generic;
using BAModAPI;
using BigAmbitions.InputSystem;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SearchAnything
{
    /// <summary>
    /// Runtime driver for the Search Anything mod. Lives on a persistent
    /// GameObject and opens a standalone search window from anywhere in the game
    /// via a configurable keyboard shortcut (Ctrl+F by default). It builds a
    /// catalogue of every product sold in the city and, when the player clicks a
    /// result, focuses the city map on that building — opening the map first if
    /// it is closed.
    /// </summary>
    // Run before the game's default-order scripts so we can reset the "Interact"
    // action before the game reads it (used to nullify F while the modifier is held).
    [DefaultExecutionOrder(-1000)]
    public sealed class SearchAnythingController : MonoBehaviour
    {
        private ModContext _context;

        // The City Map filter panel is only used as a styled clone source + font
        // sample; it stays present in-game even while the map is closed.
        private CityMapFilters _panel;
        private Canvas _canvas;
        private SearchAnythingPanel _window;

        // product key -> the shops that sell it
        private readonly Dictionary<string, List<SellerInfo>> _sellersByProduct = new();
        private readonly List<ProductInfo> _products = new();

        // Vehicle products are keyed by their showcase item name (e.g.
        // "ba:itemname_mersaididashshowcase"), but their display name and price
        // come from the linked VehicleType ("ba:vehicletype_mersaididash"), not
        // the item's own market data. _vehicleSearchByItem holds extra keywords
        // ("vehicle", the vehicle category and its features) so vehicles can be
        // found by more than just their name.
        private readonly Dictionary<string, string> _vehicleNameByItem = new();
        private readonly Dictionary<string, float> _vehiclePriceByItem = new();
        private readonly Dictionary<string, string> _vehicleSearchByItem = new();
        private readonly Dictionary<string, string> _vehicleDetailByItem = new();
        private readonly List<LocationEntry> _locations = new();
        private readonly Dictionary<string, Sprite> _iconCache = new();

        // The currently selected result ("p:<item>" / "l:<id>") and the places it
        // maps to, shown in the bottom "Where to find" list.
        private string _selectedId;
        private readonly List<SellerInfo> _whereToFind = new();

        private readonly HashSet<CityBuildingController> _highlighted = new();
        private readonly HashSet<CityBuildingController> _desired = new();

        private static System.Reflection.FieldInfo _hoveredField;

        private bool _active;
        private bool _tornDown;
        private bool _focusPending;
        // True while the current key hold began with the modifier already down
        // (i.e. a real "modifier first, then key" combo) — only then do we suppress
        // the game action bound to the key.
        private bool _comboKeyActive;
        private float _nextScan;
        private float _nextReassert;

        // -- Hotkey configuration -------------------------------------------

        /// <summary>Modifier choices shown in the mod config (index 0..3).</summary>
        public static readonly string[] ModifierChoiceKeys =
        {
            "sa_mod_none", "sa_mod_ctrl", "sa_mod_shift", "sa_mod_alt",
        };

        /// <summary>Selectable shortcut keys and their localization keys, in lock-step.</summary>
        public static readonly KeyCode[] HotkeyCodes =
        {
            KeyCode.F, KeyCode.G, KeyCode.H, KeyCode.J, KeyCode.K, KeyCode.L,
            KeyCode.B, KeyCode.N, KeyCode.M, KeyCode.O, KeyCode.P, KeyCode.U,
            KeyCode.Y, KeyCode.I, KeyCode.T, KeyCode.R,
            KeyCode.F1, KeyCode.F2, KeyCode.F3, KeyCode.F4, KeyCode.F5, KeyCode.F6,
            KeyCode.F7, KeyCode.F8, KeyCode.F9, KeyCode.F10, KeyCode.F11, KeyCode.F12,
            KeyCode.Insert, KeyCode.Home, KeyCode.PageUp, KeyCode.PageDown,
            KeyCode.Delete, KeyCode.End, KeyCode.ScrollLock, KeyCode.Pause,
        };

        public static readonly string[] HotkeyChoiceKeys =
        {
            "sa_key_f", "sa_key_g", "sa_key_h", "sa_key_j", "sa_key_k", "sa_key_l",
            "sa_key_b", "sa_key_n", "sa_key_m", "sa_key_o", "sa_key_p", "sa_key_u",
            "sa_key_y", "sa_key_i", "sa_key_t", "sa_key_r",
            "sa_key_f1", "sa_key_f2", "sa_key_f3", "sa_key_f4", "sa_key_f5", "sa_key_f6",
            "sa_key_f7", "sa_key_f8", "sa_key_f9", "sa_key_f10", "sa_key_f11", "sa_key_f12",
            "sa_key_insert", "sa_key_home", "sa_key_pageup", "sa_key_pagedown",
            "sa_key_delete", "sa_key_end", "sa_key_scrolllock", "sa_key_pause",
        };

        // Default: Ctrl + F.
        private int _modifierIndex = 1;
        private KeyCode _hotkey = KeyCode.F;

        public void Configure(ModContext context) => _context = context;

        /// <summary>Applies a shortcut chosen in the mod config (modifier + key indices).</summary>
        public void SetHotkey(int modifierIndex, int keyIndex)
        {
            _modifierIndex = Mathf.Clamp(modifierIndex, 0, ModifierChoiceKeys.Length - 1);
            if (keyIndex >= 0 && keyIndex < HotkeyCodes.Length)
                _hotkey = HotkeyCodes[keyIndex];
        }

        public void SetModifierIndex(int modifierIndex) => SetHotkey(modifierIndex, System.Array.IndexOf(HotkeyCodes, _hotkey));

        public void SetKeyIndex(int keyIndex) => SetHotkey(_modifierIndex, keyIndex);

        // -- Panel-facing API ----------------------------------------------

        /// <summary>The id of the selected result ("p:&lt;item&gt;" / "l:&lt;id&gt;"), or null.</summary>
        public string SelectedId => _selectedId;

        /// <summary>The places shown in the bottom "Where to find" list.</summary>
        public IReadOnlyList<SellerInfo> WhereToFind => _whereToFind;

        /// <summary>How many things (products + places) are indexed.</summary>
        public int TotalIndexed => _products.Count + _locations.Count;

        /// <summary>The live filter panel GameObject we clone as our window.</summary>
        public GameObject PanelObject => _panel != null ? _panel.gameObject : null;

        /// <summary>
        /// Searches both products and places. A result matches when every word of
        /// the query is found in its searchable text — for products that's the
        /// name/key, for places it's the name plus the building/business type and
        /// neighbourhood (so "ret" finds retail places, products named "...ret..."
        /// and places named "...ret...").
        /// </summary>
        public List<SearchResult> Search(string query)
        {
            var results = new List<SearchResult>();
            if (string.IsNullOrWhiteSpace(query))
                return results;

            var tokens = SearchQuery.Tokenize(query);
            if (tokens.Length == 0)
                return results;

            // Products first so they stay visible even when many places match.
            foreach (var product in _products)
            {
                // For vehicles, also match the extra keywords ("vehicle", the
                // category and features) collected during the scan.
                string extra = _vehicleSearchByItem.TryGetValue(product.ItemName ?? string.Empty, out var v) ? v : string.Empty;
                string text = ((product.DisplayName ?? string.Empty) + " " +
                    (product.ItemName ?? string.Empty) + " " + extra).ToLowerInvariant();
                if (!MatchesAll(text, tokens))
                    continue;

                results.Add(new SearchResult
                {
                    Kind = ResultKind.Product,
                    Id = "p:" + product.ItemName,
                    DisplayName = product.DisplayName,
                    ItemName = product.ItemName,
                    Price = product.Price,
                    SellerCount = product.SellerCount,
                    Detail = _vehicleDetailByItem.TryGetValue(product.ItemName ?? string.Empty, out var d) ? d : null,
                });
            }

            // Then places, matched on name + type + neighbourhood.
            foreach (var loc in _locations)
            {
                if (!MatchesAll(loc.SearchText, tokens))
                    continue;

                results.Add(new SearchResult
                {
                    Kind = ResultKind.Location,
                    Id = "l:" + (loc.Controller != null ? loc.Controller.GetInstanceID() : 0),
                    DisplayName = loc.DisplayName,
                    Controller = loc.Controller,
                    TypeLabel = loc.TypeLabel,
                    Neighbourhood = loc.Neighbourhood,
                    SizeCode = loc.SizeCode,
                    Ownership = loc.Ownership,
                });
            }

            return results;
        }

        private static bool MatchesAll(string text, string[] tokens)
        {
            if (string.IsNullOrEmpty(text))
                return false;
            foreach (var token in tokens)
            {
                if (!text.Contains(token))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Selects a result and fills the "Where to find" list: a product maps to
        /// every shop that sells it; a place maps to just itself. Highlights those
        /// places on the map (only while the map is open). Never jumps — jumping is
        /// always done from the bottom list.
        /// </summary>
        public void SelectResult(in SearchResult result)
        {
            _selectedId = result.Id;
            _whereToFind.Clear();

            if (result.Kind == ResultKind.Product)
            {
                if (result.ItemName != null && _sellersByProduct.TryGetValue(result.ItemName, out var sellers))
                    _whereToFind.AddRange(sellers);
            }
            else
            {
                _whereToFind.Add(new SellerInfo
                {
                    Controller = result.Controller,
                    DisplayName = result.DisplayName,
                    Neighbourhood = result.Neighbourhood,
                    Price = -1f,
                });
            }

            ApplyHighlight();
        }

        /// <summary>Loads (and caches) the product's inventory icon.</summary>
        public Sprite GetProductIcon(string itemName)
        {
            if (string.IsNullOrEmpty(itemName))
                return null;
            if (_iconCache.TryGetValue(itemName, out var cached))
                return cached;

            Sprite icon = null;

            // Vehicles have no per-item 2D icon (their art is a 3D showcase model,
            // so ItemHelper falls back to a cardboard box). Use the game's generic
            // vehicle icon instead so dealership stock reads as a vehicle.
            if (_vehicleNameByItem.ContainsKey(itemName))
            {
                icon = GetVehicleIcon();
            }
            else
            {
                try { icon = ItemHelper.GetIconWithFallback(itemName); }
                catch { /* missing addressable; leave null */ }
            }

            _iconCache[itemName] = icon;
            return icon;
        }

        /// <summary>The game's generic vehicle icon, shared by all vehicle products.</summary>
        private static Sprite GetVehicleIcon()
        {
            try
            {
                var refs = InstanceBehavior<GlobalReferences>.Instance;
                return refs != null ? refs.vehiclePOIIcon : null;
            }
            catch { return null; }
        }

        /// <summary>The map-pin icon a place would use (its business or building type icon).</summary>
        public Sprite GetLocationIcon(CityBuildingController controller)
        {
            if (controller == null)
                return null;
            try
            {
                GetPinVisual(controller, out _, out var icon);
                return icon;
            }
            catch { return null; }
        }

        /// <summary>A cardboard-box icon used to mark wholesale (box) sellers.</summary>
        public Sprite WholesaleBoxIcon => GetProductIcon("ba:itemname_closedcardboardbox");

        /// <summary>
        /// Focuses the city map on a shop. If the map is closed, the game opens
        /// the Voogle Maps app and jumps to the building automatically.
        /// </summary>
        public void FocusBuilding(in SellerInfo seller)
        {
            try
            {
                var manager = InstanceBehavior<CityManager>.Instance;
                if (manager != null && manager.cityMap != null && seller.Controller != null)
                    manager.cityMap.FocusOnBuilding(seller.Controller);
            }
            catch (System.Exception e)
            {
                _context?.Logger.Info($"SearchAnything: focus failed ({e.Message}).");
            }
        }

        /// <summary>Closes the Search Anything window (used by the panel's close button).</summary>
        public void RequestClose() => SetActive(false);

        // -- Lifecycle ------------------------------------------------------

        private void Update()
        {
            if (_tornDown)
                return;

            // Auto-focus the search box the frame after opening (a one-frame delay
            // keeps the shortcut keypress that opened it from being typed).
            if (_active && _focusPending)
            {
                _focusPending = false;
                _window?.FocusSearch();
            }

            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 1f;
                if (_panel == null)
                    TryAttach();
            }

            // Stop the bound key from also triggering whatever game action it is
            // mapped to while the modifier is held.
            SuppressGameShortcut();

            if (HotkeyPressed())
                SetActive(!_active);

            // Escape closes the window when it is open.
            if (_active && Input.GetKeyDown(KeyCode.Escape))
                SetActive(false);
        }

        private void LateUpdate()
        {
            if (_tornDown || !_active)
                return;

            // The game re-asserts building outlines from its own ApplyFilters; keep
            // re-applying ours a few times a second so they win while we're open.
            if (Time.unscaledTime >= _nextReassert)
            {
                _nextReassert = Time.unscaledTime + 0.3f;
                ApplyHighlight();
            }
        }

        /// <summary>True while the configured modifier (Ctrl/Shift/Alt) is held, or always when none is set.</summary>
        private bool ModifierHeld() => _modifierIndex switch
        {
            1 => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl),
            2 => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift),
            3 => Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt),
            _ => true,
        };

        /// <summary>True on the frame the configured shortcut is pressed.</summary>
        private bool HotkeyPressed()
        {
            if (_panel == null || _hotkey == KeyCode.None)
                return false;

            if (!ModifierHeld())
                return false;

            // Without a modifier the bare key could collide with typing, so ignore
            // it while a text field has focus.
            if (_modifierIndex == 0 && IsTypingInTextField())
                return false;

            return Input.GetKeyDown(_hotkey);
        }

        /// <summary>
        /// While a modifier is configured and held together with the bound key,
        /// reset every game input action that is bound to that key so the combo
        /// (e.g. Ctrl+F) opens the search window without also firing the game
        /// action mapped to the key. This is binding-aware: it works for any
        /// chosen key and honours the player's in-game rebinds (the colliding
        /// action is discovered from the live bindings, not hard-coded to F). It
        /// only fires when the key was pressed while the modifier was already
        /// held, so pressing the key first (or on its own) still triggers the
        /// game normally, and nothing is touched when no modifier is configured.
        /// The class runs at an early execution order, so the reset lands before
        /// the game reads the action that frame.
        /// </summary>
        private void SuppressGameShortcut()
        {
            if (_modifierIndex == 0 || _hotkey == KeyCode.None)
                return;

            // Only treat the key as part of the combo when the modifier was
            // already held the moment the key went down. Pressing the key first
            // (and adding the modifier afterwards) must NOT be suppressed.
            if (Input.GetKeyDown(_hotkey))
                _comboKeyActive = ModifierHeld();
            if (Input.GetKeyUp(_hotkey))
                _comboKeyActive = false;

            if (!_comboKeyActive || !Input.GetKey(_hotkey))
                return;

            try
            {
                var keyboard = Keyboard.current;
                if (keyboard == null)
                    return;

                // The mod's key list and the Input System Key enum share names
                // (F, G, F1, PageUp, ...), so we can resolve the matching control.
                if (!System.Enum.TryParse<Key>(_hotkey.ToString(), out var key))
                    return;
                InputControl control = keyboard[key];
                if (control == null)
                    return;

                ResetActionsUsingControl(InputActionHelper.PlayerInputActionMap.Values, control);
                ResetActionsUsingControl(InputActionHelper.InteriorDesignerInputActionMap.Values, control);
            }
            catch
            {
                // The input system may not be initialised yet (e.g. on the main
                // menu); ignore until it is.
            }
        }

        /// <summary>Resets any action whose live bindings resolve to the given keyboard control.</summary>
        private static void ResetActionsUsingControl(IEnumerable<InputAction> actions, InputControl control)
        {
            foreach (var action in actions)
            {
                if (action == null)
                    continue;
                var controls = action.controls;
                for (int i = 0; i < controls.Count; i++)
                {
                    if (controls[i] == control)
                    {
                        action.Reset();
                        break;
                    }
                }
            }
        }

        private static bool IsTypingInTextField()
        {
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (selected == null)
                return false;
            var tmp = selected.GetComponent<TMP_InputField>();
            if (tmp != null && tmp.isFocused)
                return true;
            var legacy = selected.GetComponent<InputField>();
            return legacy != null && legacy.isFocused;
        }

        private void TryAttach()
        {
            var panel = FindObjectOfType<CityMapFilters>(true);
            if (panel == null)
                return;

            // We only get here when the previous panel was destroyed (e.g. the
            // city scene reloaded). The cached window was cloned from that old
            // panel and lived under a now-destroyed canvas, so discard it; it
            // will be rebuilt against the fresh panel/canvas on next open.
            if (_window != null)
            {
                _window.Destroy();
                _window = null;
            }

            _panel = panel;
            _canvas = ResolveCanvas(panel);

            UiFactory.CaptureFont(panel);
            UiFactory.CaptureGameDropdown(panel);

            _context?.Logger.Info("SearchAnything ready (use the shortcut to open the search window).");
        }

        /// <summary>
        /// Resolve the root canvas the window should live under. The City Map
        /// panel is usually inactive (its Start ends with SetActive(false)), and
        /// <see cref="Component.GetComponentInParent{T}()"/> skips inactive
        /// objects, so search with includeInactive and fall back to any canvas.
        /// </summary>
        private static Canvas ResolveCanvas(Component panel)
        {
            var canvas = panel.GetComponentInParent<Canvas>(true);
            if (canvas == null)
            {
                foreach (var c in FindObjectsOfType<Canvas>(true))
                {
                    if (c != null && c.isRootCanvas)
                    {
                        canvas = c;
                        break;
                    }
                }
            }

            return canvas != null ? canvas.rootCanvas : null;
        }

        // -- Open / close ---------------------------------------------------

        private void SetActive(bool active)
        {
            if (_tornDown)
                return;

            if (active)
            {
                if (_panel == null)
                    TryAttach();
                if (_canvas == null && _panel != null)
                    _canvas = ResolveCanvas(_panel);
                if (_panel == null || _canvas == null)
                {
                    _context?.Logger.Info("SearchAnything: not ready yet, cannot open window.");
                    return;
                }

                _active = true;
                ScanCatalog();
                _window ??= new SearchAnythingPanel(this, _canvas);
                _window.Show();
                _focusPending = true;
            }
            else
            {
                _active = false;
                _focusPending = false;
                _window?.Hide();
                ClearHighlight();
                _selectedId = null;
                _whereToFind.Clear();
            }
        }

        // -- Catalogue ------------------------------------------------------

        /// <summary>
        /// Walks every building in the city and records (a) every place — with its
        /// name, type and neighbourhood so it is searchable — and (b) for each
        /// product offered for sale, the shops that sell it.
        /// </summary>
        private void ScanCatalog()
        {
            _products.Clear();
            _sellersByProduct.Clear();
            _vehicleNameByItem.Clear();
            _vehiclePriceByItem.Clear();
            _vehicleSearchByItem.Clear();
            _vehicleDetailByItem.Clear();
            _locations.Clear();
            _selectedId = null;
            _whereToFind.Clear();

            CityBuildingController[] controllers = null;
            try
            {
                var manager = InstanceBehavior<CityManager>.Instance;
                controllers = manager != null ? manager.cityBuildingControllers : null;
            }
            catch (System.Exception e)
            {
                _context?.Logger.Info($"SearchAnything: catalogue scan failed ({e.Message}).");
            }

            if (controllers == null)
                return;

            foreach (var controller in controllers)
            {
                if (controller == null)
                    continue;

                var reg = controller.buildingRegistration;
                if (reg == null)
                    continue;

                var building = controller.building;
                string name = SafeSellerName(reg);
                string hood = LabelText.Prettify(SafeNeighbourhood(reg));

                // (a) Searchable place entry for every registered building.
                string buildingTypeKey = string.Empty;
                try { buildingTypeKey = building != null ? (building.BuildingType ?? string.Empty) : string.Empty; }
                catch { /* ignore */ }
                string buildingTypeLabel = LabelText.Prettify(buildingTypeKey);

                string businessKey = string.Empty;
                try { businessKey = reg.businessTypeName ?? string.Empty; }
                catch { /* ignore */ }
                bool isBusiness = !string.IsNullOrEmpty(businessKey) && businessKey != "ba:businesstype_empty";
                string businessLabel = isBusiness ? LabelText.Prettify(businessKey) : string.Empty;

                // Building size code as shown in game, e.g. "C1" = size letter + version.
                string sizeKey = string.Empty;
                int sizeVersion = 0;
                try
                {
                    if (building != null)
                    {
                        sizeKey = building.BuildingSize ?? string.Empty;
                        sizeVersion = building.BuildingVersion;
                    }
                }
                catch { /* ignore */ }
                string sizeShort = ShortSizeCode(sizeKey);
                string sizeCode = string.IsNullOrEmpty(sizeShort) ? string.Empty : sizeShort + sizeVersion;

                // Who owns / rents the building (player or a named rival).
                string ownershipText = BuildOwnershipSearchText(reg);
                string ownershipLabel = BuildOwnershipLabel(reg);

                string searchText = (name + " " +
                    buildingTypeLabel + " " + buildingTypeKey + " " +
                    businessLabel + " " + businessKey + " " +
                    sizeCode + " " + sizeShort + " " + sizeKey + " " +
                    ownershipText + " " + ownershipLabel + " " + hood).ToLowerInvariant();

                _locations.Add(new LocationEntry
                {
                    Controller = controller,
                    DisplayName = name,
                    TypeLabel = isBusiness ? businessLabel : buildingTypeLabel,
                    Neighbourhood = hood,
                    SizeCode = sizeCode,
                    Ownership = ownershipLabel,
                    SearchText = searchText,
                });

                // (b) Wholesale / import-export stores expose their stock via a
                // special service, not cachedAvailableProducts. Add those boxes too.
                try
                {
                    if (building != null && building.SpecialService != null
                        && building.SpecialService.settings is Buildings.ImportExportSettings ies)
                    {
                        var available = ies.GetItemsAvailable(null, true);
                        if (available != null)
                        {
                            foreach (var itemName in available)
                            {
                                if (string.IsNullOrEmpty(itemName))
                                    continue;
                                if (!_sellersByProduct.TryGetValue(itemName, out var wlist))
                                {
                                    wlist = new List<SellerInfo>();
                                    _sellersByProduct[itemName] = wlist;
                                }
                                wlist.Add(new SellerInfo
                                {
                                    Controller = controller,
                                    DisplayName = name,
                                    Neighbourhood = hood,
                                    Price = WholesaleBoxPrice(itemName),
                                    IsWholesale = true,
                                });
                            }
                        }
                    }
                }
                catch { /* not a wholesaler / no special service */ }

                // (b2) Wholesale stores stock via GetListOfItemsForSale (their
                // cachedAvailableProducts can be empty), sold by the box.
                if (businessKey == "ba:businesstype_wholesalestore")
                {
                    try
                    {
                        var forSale = reg.GetListOfItemsForSale();
                        if (forSale != null)
                        {
                            foreach (var itemName in forSale)
                            {
                                if (string.IsNullOrEmpty(itemName))
                                    continue;
                                if (!_sellersByProduct.TryGetValue(itemName, out var wlist))
                                {
                                    wlist = new List<SellerInfo>();
                                    _sellersByProduct[itemName] = wlist;
                                }
                                wlist.Add(new SellerInfo
                                {
                                    Controller = controller,
                                    DisplayName = name,
                                    Neighbourhood = hood,
                                    Price = WholesaleBoxPrice(itemName),
                                    IsWholesale = true,
                                });
                            }
                        }
                    }
                    catch { /* not stocked / no layout */ }
                    continue;
                }

                // (b3) Car dealerships sell vehicles (e.g. the Mersaidi Dash).
                // Their stock is not in cachedAvailableProducts and is filtered
                // out by GetListOfItemsForSale (dealerships have no walk-in
                // customer type), so read the business layout set directly and
                // keep the "showcase" items that map to a VehicleType — mirroring
                // the game's own PurchaseVehicle logic.
                if (businessKey == "ba:businesstype_cardealership")
                {
                    IndexDealershipVehicles(controller, reg, building, name, hood);
                    continue;
                }

                // (c) Product sellers for buildings that offer products.
                var products = reg.cachedAvailableProducts;
                if (products == null || products.Count == 0)
                    continue;

                foreach (var itemName in products)
                {
                    if (string.IsNullOrEmpty(itemName))
                        continue;

                    if (!_sellersByProduct.TryGetValue(itemName, out var list))
                    {
                        list = new List<SellerInfo>();
                        _sellersByProduct[itemName] = list;
                    }

                    list.Add(new SellerInfo
                    {
                        Controller = controller,
                        DisplayName = name,
                        Neighbourhood = hood,
                        Price = LookupPrice(reg, itemName),
                    });
                }
            }

            _locations.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, System.StringComparison.OrdinalIgnoreCase));

            // Build the searchable product list and sort everything by name.
            foreach (var kv in _sellersByProduct)
            {
                kv.Value.Sort((a, b) =>
                {
                    int n = string.Compare(a.Neighbourhood, b.Neighbourhood, System.StringComparison.OrdinalIgnoreCase);
                    return n != 0 ? n : string.Compare(a.DisplayName, b.DisplayName, System.StringComparison.OrdinalIgnoreCase);
                });

                bool isVehicle = _vehicleNameByItem.TryGetValue(kv.Key, out var vehicleName);
                _products.Add(new ProductInfo
                {
                    ItemName = kv.Key,
                    DisplayName = isVehicle ? vehicleName : LabelText.Prettify(kv.Key),
                    SellerCount = kv.Value.Count,
                    Price = isVehicle ? _vehiclePriceByItem[kv.Key] : SafeMarketPrice(kv.Key),
                });
            }

            _products.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, System.StringComparison.OrdinalIgnoreCase));

            _context?.Logger.Info(
                $"SearchAnything: indexed {_products.Count} products ({_vehicleNameByItem.Count} vehicles) and {_locations.Count} locations.");
        }

        /// <summary>
        /// Indexes the vehicles a car dealership offers. Dealership stock is not
        /// held in <c>cachedAvailableProducts</c> (and GetListOfItemsForSale
        /// discards it because dealerships have no walk-in customer type), so the
        /// business layout set is loaded directly and every enabled "player item
        /// purchaser" that resolves to a VehicleType is added as a sellable
        /// vehicle — the same source the game's PurchaseVehicle dialog reads.
        /// </summary>
        private void IndexDealershipVehicles(
            CityBuildingController controller, BuildingRegistration reg, Buildings.Building building,
            string name, string hood)
        {
            try
            {
                if (building == null || string.IsNullOrEmpty(reg.Layout))
                    return;

                var layoutSet = BusinessLayoutSets.BusinessLayoutSetHelper.GetOrLoadBusinessLayoutSet(
                    reg.businessTypeName, new Blueprints.BuildingSizeInfo(building), reg.Layout.ToLower(), false);
                if (layoutSet?.Items == null)
                    return;

                foreach (var layoutItem in layoutSet.Items)
                {
                    var purchaser = layoutItem?.playerItemPurchaserSettings;
                    if (purchaser == null || !purchaser.enabled || string.IsNullOrEmpty(purchaser.itemName))
                        continue;

                    var item = BigAmbitions.Items.ItemsGetter.GetByName(purchaser.itemName);
                    if (item == null || string.IsNullOrEmpty(item.vehicleType))
                        continue;

                    var vehicleType = Vehicles.VehicleTypes.VehicleTypeHelper.GetVehicleType(item.vehicleType);

                    string itemName = purchaser.itemName;
                    if (!_sellersByProduct.TryGetValue(itemName, out var sellers))
                    {
                        sellers = new List<SellerInfo>();
                        _sellersByProduct[itemName] = sellers;
                    }
                    sellers.Add(new SellerInfo
                    {
                        Controller = controller,
                        DisplayName = name,
                        Neighbourhood = hood,
                        Price = VehicleTypePrice(vehicleType),
                    });

                    _vehicleNameByItem[itemName] = LabelText.Prettify(item.vehicleType);
                    _vehiclePriceByItem[itemName] = VehicleTypePrice(vehicleType);
                    _vehicleSearchByItem[itemName] = BuildVehicleSearchTerms(vehicleType);
                    _vehicleDetailByItem[itemName] = BuildVehicleDetail(vehicleType);
                }
            }
            catch (System.Exception e)
            {
                _context?.Logger.Info($"SearchAnything: dealership scan failed ({e.Message}).");
            }
        }

        private static string SafeSellerName(BuildingRegistration reg)
        {
            try
            {
                string name = reg.GetDisplayName();
                if (!string.IsNullOrEmpty(name))
                    return name;
            }
            catch { /* fall through */ }
            return "Building";
        }

        private static string SafeNeighbourhood(BuildingRegistration reg)
        {
            try { return reg.Neighborhood ?? string.Empty; }
            catch { return string.Empty; }
        }

        /// <summary>
        /// Builds a searchable blob describing who owns and who rents the building
        /// so terms like "owned by player", "rented by you" or a rival's name match.
        /// </summary>
        private static string BuildOwnershipSearchText(BuildingRegistration reg)
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                if (reg.BuildingOwnedByPlayer)
                {
                    sb.Append(" owned by player you me mine");
                }
                else
                {
                    string ownerRival = ResolveRivalName(reg.buildingOwnerRivalId);
                    if (!string.IsNullOrEmpty(ownerRival))
                        sb.Append(" owned by ").Append(ownerRival);
                }

                if (reg.RentedByPlayer)
                {
                    sb.Append(" rented by player you me mine tenant");
                }
                else
                {
                    string tenantRival = ResolveRivalName(reg.businessOwnerRivalId);
                    if (!string.IsNullOrEmpty(tenantRival))
                        sb.Append(" rented by ").Append(tenantRival);
                    else if (reg.AvailableForRent)
                        sb.Append(" available to rent rentable vacant empty");
                }
            }
            catch { /* ownership unknown */ }

            return sb.ToString();
        }

        /// <summary>
        /// Builds a short, human-readable ownership label for a building, e.g.
        /// "Owned by You", "Rented by Rival", or both joined — empty when the
        /// building has no owner or tenant.
        /// </summary>
        private static string BuildOwnershipLabel(BuildingRegistration reg)
        {
            try
            {
                string owner = reg.BuildingOwnedByPlayer ? "You" : ResolveRivalName(reg.buildingOwnerRivalId);
                string tenant = reg.RentedByPlayer ? "You" : ResolveRivalName(reg.businessOwnerRivalId);

                string result = string.Empty;
                if (!string.IsNullOrEmpty(owner))
                    result = "Owned by " + owner;
                if (!string.IsNullOrEmpty(tenant))
                    result = string.IsNullOrEmpty(result) ? "Rented by " + tenant : result + " \u00b7 Rented by " + tenant;
                else if (reg.AvailableForRent)
                    result = string.IsNullOrEmpty(result) ? "Available to rent" : result + " \u00b7 Available to rent";
                return result;
            }
            catch { return string.Empty; }
        }

        /// <summary>
        /// Turns a building-size key (e.g. "ba:buildingsize_c") into the short
        /// code the game shows ("C"); combined with the version it reads "C1".
        /// </summary>
        private static string ShortSizeCode(string sizeKey)
        {
            if (string.IsNullOrEmpty(sizeKey))
                return string.Empty;

            int underscore = sizeKey.LastIndexOf('_');
            string s = underscore >= 0 && underscore < sizeKey.Length - 1
                ? sizeKey.Substring(underscore + 1)
                : sizeKey;
            if (s.Length == 0)
                return string.Empty;

            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        /// <summary>Resolves a rival id to its display name, or empty when there is no rival.</summary>
        private static string ResolveRivalName(string rivalId)
        {
            if (string.IsNullOrEmpty(rivalId))
                return string.Empty;

            try
            {
                var data = BigAmbitions.Rivals.RivalsHelper.GetRivalData(rivalId);
                if (data != null && !string.IsNullOrEmpty(data.rivalName))
                    return data.rivalName;
            }
            catch { /* fall through */ }

            return string.Empty;
        }

        /// <summary>The product's default market price, or a negative value when unknown.</summary>
        private static float SafeMarketPrice(string itemName)
        {
            try
            {
                float price = ItemHelper.GetDefaultMarketPrice(itemName);
                return price > 0f ? price : -1f;
            }
            catch { return -1f; }
        }

        /// <summary>The purchase price of a vehicle type, or negative when unknown.</summary>
        private static float VehicleTypePrice(Vehicles.VehicleTypes.VehicleType vehicleType)
        {
            try
            {
                return vehicleType != null && vehicleType.price > 0f ? vehicleType.price : -1f;
            }
            catch { return -1f; }
        }

        /// <summary>
        /// Builds a blob of extra search keywords for a vehicle so it can be found
        /// by more than its name: the word "vehicle", its category (car, truck,
        /// scooter, hand truck) and a handful of features (motorised, cargo, tax
        /// deductible, auto-park, radio, enclosed, tow-fit).
        /// </summary>
        private static string BuildVehicleSearchTerms(Vehicles.VehicleTypes.VehicleType vehicleType)
        {
            var sb = new System.Text.StringBuilder(" vehicle");
            if (vehicleType == null)
                return sb.ToString();

            // Category, taken from the vehicle's tags. "Car" is omitted because
            // it is the default kind shared by almost every vehicle, so it does
            // not help narrow a search.
            try
            {
                if (vehicleType.HasTag(BigAmbitions.Tags.TagRef.Vehicletag.istruck))
                    sb.Append(" truck lorry");
                else if (vehicleType.HasTag(BigAmbitions.Tags.TagRef.Vehicletag.isscooter))
                    sb.Append(" scooter moped bike");
                else if (vehicleType.HasTag(BigAmbitions.Tags.TagRef.Vehicletag.ishandvehicle))
                    sb.Append(" hand truck handtruck cart trolley");
            }
            catch { /* tag database unavailable */ }

            // Distinguishing features only (radio and enclosed are shared by
            // essentially every vehicle, so they are left out).
            try
            {
                sb.Append(vehicleType.IsMotorVehicle ? " motorised motorized fuel" : " manual non-motorised");
                if (vehicleType.maxCargoCapacity > 0)
                    sb.Append(" cargo storage slots ").Append(vehicleType.maxCargoCapacity);
                if (vehicleType.taxDeductible)
                    sb.Append(" tax deductible business");
                if (vehicleType.autoParkSupported)
                    sb.Append(" auto park autopark self-parking");
                if (vehicleType.fitsHandTruck)
                    sb.Append(" fits hand truck");
                if (vehicleType.fitsFlatbed)
                    sb.Append(" fits flatbed");
            }
            catch { /* stats unavailable */ }

            return sb.ToString();
        }

        /// <summary>
        /// Builds the human-readable descriptor lines for a vehicle — its category
        /// and distinguishing features — shown under the result so the player can
        /// see why it matched. Traits shared by every vehicle (car, radio,
        /// enclosed) are omitted. A few tags are packed onto each line (separated
        /// by newlines) rather than one tag per line.
        /// </summary>
        private static string BuildVehicleDetail(Vehicles.VehicleTypes.VehicleType vehicleType)
        {
            var parts = new List<string> { "Vehicle" };
            if (vehicleType == null)
                return parts[0];

            try
            {
                if (vehicleType.HasTag(BigAmbitions.Tags.TagRef.Vehicletag.istruck))
                    parts.Add("Truck");
                else if (vehicleType.HasTag(BigAmbitions.Tags.TagRef.Vehicletag.isscooter))
                    parts.Add("Scooter");
                else if (vehicleType.HasTag(BigAmbitions.Tags.TagRef.Vehicletag.ishandvehicle))
                    parts.Add("Hand truck");
            }
            catch { /* tag database unavailable */ }

            try
            {
                if (vehicleType.maxCargoCapacity > 0)
                    parts.Add(vehicleType.maxCargoCapacity == 1 ? "1 storage slot" : $"{vehicleType.maxCargoCapacity} storage slots");
                if (vehicleType.taxDeductible)
                    parts.Add("Tax deductible");
                if (vehicleType.autoParkSupported)
                    parts.Add("Auto-park");
                if (vehicleType.fitsFlatbed)
                    parts.Add("Fits flatbed");
                else if (vehicleType.fitsHandTruck)
                    parts.Add("Fits hand truck");
            }
            catch { /* stats unavailable */ }

            return JoinIntoLines(parts, 3);
        }

        /// <summary>
        /// Joins tags into lines, packing up to <paramref name="perLine"/> tags on
        /// each line (separated by a middle dot) and starting a new line after
        /// that. Lines are separated by newlines.
        /// </summary>
        private static string JoinIntoLines(List<string> parts, int perLine)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < parts.Count; i++)
            {
                if (i > 0)
                    sb.Append(i % perLine == 0 ? "\n" : "  \u00b7  ");
                sb.Append(parts[i]);
            }
            return sb.ToString();
        }

        /// <summary>The wholesale price for a full box of the product, or negative when unknown.</summary>
        private static float WholesaleBoxPrice(string itemName)
        {
            try
            {
                var item = BigAmbitions.Items.ItemsGetter.GetByName(itemName);
                if (item == null)
                    return -1f;
                float perUnit = item.GetWholesalePrice();
                if (perUnit <= 0f)
                    return -1f;
                int box = item.boxSize > 0 ? item.boxSize : 1;
                return perUnit * box;
            }
            catch { return -1f; }
        }

        private static float LookupPrice(BuildingRegistration reg, string itemName)
        {
            try
            {
                var prices = reg.retailPrices;
                if (prices != null)
                {
                    foreach (var rp in prices)
                    {
                        if (rp != null && rp.itemName == itemName)
                            return rp.price;
                    }
                }
            }
            catch { /* no retail prices */ }
            return -1f;
        }

        // -- Highlighting (mirrors the vanilla map filter visuals) ----------

        /// <summary>True while the city map screen is actually open.</summary>
        private static bool IsMapOpen()
        {
            try { return CityMap.IsOpen; }
            catch { return false; }
        }

        private void ApplyHighlight()
        {
            // Only paint the map while it is actually open. Selecting a product
            // should just populate the results list; we must not push outlines and
            // pins onto a closed map. The 0.3s re-assert in LateUpdate picks the
            // highlight up the moment the map opens — whether the player opened it
            // themselves or by clicking a location result.
            if (!IsMapOpen())
            {
                if (_highlighted.Count > 0)
                    ClearHighlight();
                return;
            }

            _desired.Clear();
            foreach (var seller in _whereToFind)
            {
                if (seller.Controller != null)
                    _desired.Add(seller.Controller);
            }

            // Turn off buildings that no longer match.
            foreach (var controller in _highlighted)
            {
                if (controller == null || _desired.Contains(controller))
                    continue;
                try { Unhighlight(controller); }
                catch { /* a building may have been destroyed; skip it */ }
            }

            // Apply / re-assert the highlight on matching buildings.
            var hovered = GetHoveredBuilding();
            foreach (var controller in _desired)
            {
                if (controller == hovered && _highlighted.Contains(controller))
                    continue;
                try { Highlight(controller); }
                catch { /* a building may have been destroyed; skip it */ }
            }

            _highlighted.Clear();
            foreach (var controller in _desired)
                _highlighted.Add(controller);
        }

        private static CityBuildingController GetHoveredBuilding()
        {
            try
            {
                _hoveredField ??= typeof(CityBuildingController).GetField(
                    "CurrentBuildingHighlighted",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                return _hoveredField?.GetValue(null) as CityBuildingController;
            }
            catch { return null; }
        }

        private void Highlight(CityBuildingController controller)
        {
            GetPinVisual(controller, out var pinColor, out var pinIcon);

            if (controller.poi == null)
                controller.CreatePOI();
            else if (controller.poi.Permanent)
                controller.poi.SetHidden(false);

            controller.SetHighlight(true, pinColor);

            if (controller.poi != null)
                controller.poi.SetIcon(pinIcon, pinColor);
        }

        private static void Unhighlight(CityBuildingController controller)
        {
            controller.SetHighlight(false);
            if (controller.poi != null && controller.poi.Permanent)
                controller.poi.SetHidden(true);
        }

        private static void GetPinVisual(CityBuildingController controller, out Color color, out Sprite icon)
        {
            var typeData = Buildings.BuildingTypeHelper.GetData(controller.building);
            color = typeData.mapFilterColor;
            icon = typeData.poiIcon;

            var registration = controller.buildingRegistration;
            if (registration != null && registration.businessTypeName != "ba:businesstype_empty")
            {
                var businessData = Helpers.BusinessTypeHelper.GetData(registration);
                if (businessData != null)
                {
                    color = businessData.cityMapFilterColor;
                    icon = businessData.icon;
                }
            }
        }

        private void ClearHighlight()
        {
            foreach (var controller in _highlighted)
            {
                if (controller == null)
                    continue;
                try { Unhighlight(controller); }
                catch { /* ignore */ }
            }
            _highlighted.Clear();
        }

        public void Teardown()
        {
            _tornDown = true;
            try
            {
                ClearHighlight();
                _window?.Destroy();
            }
            catch (System.Exception e)
            {
                _context?.Logger.Info($"SearchAnything: ignored teardown error ({e.Message}).");
            }
        }
    }
}
