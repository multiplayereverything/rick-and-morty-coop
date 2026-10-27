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
            try { new Harmony("com.multiplayereverything.rickandmortytogether").PatchAll(typeof(Plugin).Assembly); }
            catch (Exception ex) { log.LogError("Harmony patches failed: " + ex); }
            TogetherRuntime.Create(Path.GetDirectoryName(Application.dataPath));
        }
    }
}
