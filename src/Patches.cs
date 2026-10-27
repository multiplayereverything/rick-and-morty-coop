using HarmonyLib;

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
}
