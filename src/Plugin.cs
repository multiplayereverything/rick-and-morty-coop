using System;
using System.IO;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace RickAndMortyTogether
{
    // BepInEx entry. The game is Unity 5.4, which loads its scripts into a child domain: BepInEx must start from
    // SceneManager.Internal_SceneLoaded (see BepInEx/config/BepInEx.cfg in the release), after the game's own scripts
    // are bound, or every scene loses its script references.
    [BepInPlugin("com.multiplayereverything.rickandmortytogether", "Rick and Morty Together", TogetherRuntime.ModVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        void Awake()
        {
            BepInEx.Logging.ManualLogSource log = Logger;
            TogetherRuntime.Log = message => log.LogInfo(message);
            // One class at a time: a patch that fails (say the game changed) must not take every later patch with it.
            Harmony harmony = new Harmony("com.multiplayereverything.rickandmortytogether");
            foreach (Type type in typeof(Plugin).Assembly.GetTypes())
            {
                if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
                try { harmony.CreateClassProcessor(type).Patch(); }
                catch (Exception ex) { log.LogError("Harmony patch " + type.Name + " failed: " + ex); TogetherRuntime.PatchFailures.Add(type.Name + ": " + ex.Message); }
            }
            TogetherRuntime.Create(Path.GetDirectoryName(Application.dataPath));
        }
    }
}
