using System;
using System.Threading.Tasks;
using BAModAPI;
using SearchAnything;

[assembly: RegisterModClass(typeof(SearchAnythingMod))]

namespace SearchAnything
{
    /// <summary>
    /// Entry point for the Search Anything mod. Loads while in-game so it can
    /// reach the live City Map UI and the player's save data.
    /// </summary>
    [ModEntryOnInitializationLoad]
    public class SearchAnythingMod : IModBigAmbitions
    {
        private readonly SearchAnythingLogic _logic = new();

        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            _logic.Initialize(context);
            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            _logic.Shutdown();
            return Task.CompletedTask;
        }
    }
}
