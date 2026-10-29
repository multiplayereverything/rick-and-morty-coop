using HarmonyLib;
using OwlchemyVR;

namespace RickAndMortyTogether
{
    // Saves: lab copies share the real install's save folder (persistentDataPath), so they never read or write it.
    // A client in someone else's game keeps its own single-player save untouched; only the host's game saves.
    // Both overloads: Save(data) and Save(path, data).
    [HarmonyPatch(typeof(SaveLoad), "Save", new[] { typeof(SaveStateData) })]
    static class SaveLoadSavePatch
    {
        static bool Prefix() { return Allowed(); }

        public static bool Allowed()
        {
            TogetherRuntime rt = TogetherRuntime.Instance;
            if (TogetherRuntime.LabMode) return false;
            return rt == null || !rt.Active || rt.Hosting;
        }
    }

    [HarmonyPatch(typeof(SaveLoad), "Save", new[] { typeof(string), typeof(SaveStateData) })]
    static class SaveLoadSavePathPatch
    {
        static bool Prefix() { return SaveLoadSavePatch.Allowed(); }
    }

    [HarmonyPatch(typeof(SaveLoad), "Load")]
    static class SaveLoadLoadPatch
    {
        static bool Prefix(ref SaveStateData __result)
        {
            if (!TogetherRuntime.LabMode) return true;
            __result = null;   // a fresh game in every lab copy
            return false;
        }
    }

    // The laundry zone puts an item that leaves it while still its child back under OwlchemyVRStorage.LevelController,
    // which nothing sets any more (deprecated for SceneController): a NullReferenceException that stops the zone's other
    // listeners. Put it under the scene's transient root instead, as the basket's own DropLaundry does.
    [HarmonyPatch(typeof(UnpartentOnExitItemCollectionZone), "Removed")]
    static class UnparentOnExitPatch
    {
        static bool Prefix(UnpartentOnExitItemCollectionZone __instance, PickupableItem item)
        {
            if (item == null || item.transform.parent != __instance.transform || OwlchemyVRStorage.Instance.LevelController != null) return true;
            TogetherRuntime rt = TogetherRuntime.Instance;
            TogetherRuntime.Log("Laundry: " + item.name + " left " + __instance.name + " while its child (synced id " + (rt != null ? rt.ItemIdOf(item) : 0u) + ")");
            if (TogetherRuntime.Mutant("nolaundryunparent")) return true;
            item.transform.parent = item.gameObject.GetTransientSceneRoot();
            return false;
        }
    }
}
