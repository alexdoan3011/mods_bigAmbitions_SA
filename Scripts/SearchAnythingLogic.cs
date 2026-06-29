using BAModAPI;
using BigAmbitions.Mods;
using UnityEngine;

namespace SearchAnything
{
    /// <summary>
    /// Core logic for Search Anything. Spawns a persistent driver
    /// (<see cref="SearchAnythingController"/>) and registers the mod's options
    /// (the configurable open shortcut, default Ctrl+F).
    /// </summary>
    public class SearchAnythingLogic
    {
        private const string ModifierOptionId = "sa_modifier";
        private const string KeyOptionId = "sa_key";
        private const int DefaultModifierIndex = 1; // Ctrl
        private const int DefaultKeyIndex = 0;      // F

        private ModContext _context = null!;
        private GameObject _driverObject;
        private SearchAnythingController _controller;

        public void Initialize(ModContext context)
        {
            _context = context;

            _driverObject = new GameObject("SearchAnything_Driver");
            Object.DontDestroyOnLoad(_driverObject);
            _controller = _driverObject.AddComponent<SearchAnythingController>();
            _controller.Configure(context);

            // Apply the saved shortcut up front. The option callbacks only fire
            // once the options menu is built, so read the persisted indices here
            // (ModOptionPrefs stores each option under "m:<modId>:<optionId>").
            int modifier = UnityEngine.PlayerPrefs.GetInt($"m:{context.ModId}:{ModifierOptionId}", DefaultModifierIndex);
            int key = UnityEngine.PlayerPrefs.GetInt($"m:{context.ModId}:{KeyOptionId}", DefaultKeyIndex);
            _controller.SetHotkey(modifier, key);

            RegisterOptions(context);

            _context.Logger.Info("SearchAnything loaded. Press the shortcut (Ctrl+F by default) to search.");
        }

        private void RegisterOptions(ModContext context)
        {
            var options = new ModOptions()
                .AddHeader("searchanything_options_header")
                .AddDropdown(ModifierOptionId, "searchanything_modifier_label",
                    SearchAnythingController.ModifierChoiceKeys, DefaultModifierIndex,
                    index => _controller.SetModifierIndex(index))
                .AddDropdown(KeyOptionId, "searchanything_key_label",
                    SearchAnythingController.HotkeyChoiceKeys, DefaultKeyIndex,
                    index => _controller.SetKeyIndex(index));

            OptionsService.Register(context.ModId, options);
        }

        public void Shutdown()
        {
            try
            {
                OptionsService.RemoveModOptions(_context.ModId);
            }
            catch (System.Exception e)
            {
                _context.Logger.Info($"SearchAnything: ignored options cleanup error ({e.Message}).");
            }

            try
            {
                if (_driverObject != null)
                {
                    if (_controller != null)
                        _controller.Teardown();

                    Object.Destroy(_driverObject);
                    _driverObject = null;
                }
            }
            catch (System.Exception e)
            {
                _context.Logger.Info($"SearchAnything: ignored shutdown error ({e.Message}).");
            }

            _context.Logger.Info("SearchAnything unloaded.");
        }
    }
}
