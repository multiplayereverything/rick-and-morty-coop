using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using OwlchemyVR;
using UnityEngine;

namespace RickAndMortyTogether
{
    // Rick's smartwatch calls: only the wearer's copy plays them (the game needs the watch on this player's own wrist to
    // ring and be answered). Each clip it plays (calls and quick lines, all through SmartWatchController.PlayCallClip)
    // goes to every other player by name (Op.WatchClip), and they hear it too, at full volume wherever they stand.
    public sealed partial class TogetherRuntime
    {
        AudioSource watchVoice;                                         // session-scoped: plays other players' watch calls here
        readonly Dictionary<string, AudioClip> clipsByName = new Dictionary<string, AudioClip>();   // session-scoped: clip lookup cache
        public int WatchClipsSent, WatchClipsHeard;                     // session-scoped: lab counters

        // Harmony postfix on PlayCallClip: this copy's wearer is hearing a call clip.
        public void OnWatchClip(SmartWatchController watch, AudioClip clip)
        {
            if (!Active || clip == null || watch == null || watch.Attachable == null || !RigPoint(watch.Attachable.CurrentlyAttachedTo) || Mutant("nowatchaudio")) return;
            string name = clip.name;
            byte[] packet = Wire.Packet(Op.WatchClip, w => Wire.Text(w, name));
            if (Hosting) Broadcast(packet, -1); else Send(0, packet);
            WatchClipsSent++;
        }

        void HostWatchClip(int from, BinaryReader r)
        {
            string name = Wire.Text(r, 128);
            PlayWatchClip(name);
            Broadcast(Wire.Packet(Op.WatchClip, w => Wire.Text(w, name)), from);
        }

        void ClientWatchClip(BinaryReader r) { PlayWatchClip(Wire.Text(r, 128)); }

        void PlayWatchClip(string name)
        {
            AudioClip clip;
            if (!clipsByName.TryGetValue(name, out clip) || clip == null)
            {
                clip = null;
                foreach (AudioClip c in Resources.FindObjectsOfTypeAll<AudioClip>()) if (c.name == name) { clip = c; break; }
                clipsByName[name] = clip;
            }
            if (clip == null) { Log("Watch: no clip " + name + " here"); return; }
            if (watchVoice == null)
            {
                GameObject go = new GameObject("RickAndMortyTogether.WatchVoice");
                Object.DontDestroyOnLoad(go);
                watchVoice = go.AddComponent<AudioSource>();
                watchVoice.spatialBlend = 0f;   // heard the same wherever this player stands
                watchVoice.playOnAwake = false;
            }
            watchVoice.Stop();
            watchVoice.clip = clip;
            watchVoice.Play();
            WatchClipsHeard++;
            Log("Watch: another player's call: " + name);
        }
    }

    [HarmonyPatch(typeof(SmartWatchController), "PlayCallClip")]
    static class WatchClipPatch
    {
        static void Postfix(SmartWatchController __instance, AudioClip clip) { if (TogetherRuntime.Instance != null) TogetherRuntime.Instance.OnWatchClip(__instance, clip); }
    }
}
