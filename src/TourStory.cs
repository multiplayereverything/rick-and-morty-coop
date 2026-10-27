using System;
using System.Collections;
using System.Collections.Generic;
using OwlchemyVR;
using OwlchemyVR.Brain;
using OwlchemyVR.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RickAndMortyTogether
{
    // Tour "story": story events, the progression stage, and the trip from the living room (the DVD menu) into the
    // garage and back. The host plays the game's own calls where a player would need the real DVD menu (inserting a
    // disc and the headset's Menu button for the tutorial screen): a Brain world event, ProgressionManager.SetStage and
    // GlobalStorage.FadeAndLoadScene. Everything after that is the mod's sync.
    public sealed partial class TogetherRuntime
    {
        public int CausesFromClients;   // lab checks read it

        static List<string> CauseKeys()
        {
            List<string> keys = new List<string>();
            BrainManager brain = BrainManager.no_instantiate_instance;
            if (brain == null || brain.QuickLookupForCauses == null) return keys;
            foreach (string k in brain.QuickLookupForCauses.Keys) if (!LocalCause(k)) keys.Add(k);
            keys.Sort(StringComparer.Ordinal);
            return keys;
        }

        // A world event as the game would raise it here (the patch then decides who runs it).
        static void RaiseCause(string key)
        {
            BrainManager brain = BrainManager.no_instantiate_instance;
            if (brain != null) checkCause.Invoke(brain, new object[] { key });
        }

        IEnumerator StoryHost()
        {
            RigHead = new Vector3(0.2f, 1.35f, -0.6f); RigYaw = 0;
            yield return StartCoroutine(Wait(() => AcceptedCount >= Mathf.Max(1, expectPlayers - 1), 120, "tour.peers", "no client joined"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(Pause(6));
            // 1. A story event on the host reaches the client's Brain.
            List<string> keys = CauseKeys();
            Log("Story: " + keys.Count + " causes in the living room: " + string.Join(" ", keys.GetRange(0, Mathf.Min(40, keys.Count)).ToArray()));
            if (keys.Count < 2) { tourFailure = "story.host.cause"; Check("story.host.cause", false, "the living room's Brain has " + keys.Count + " causes"); yield break; }
            RaiseCause(keys[0]);
            Check("story.host.cause", CausesSent > 0, "raised " + keys[0] + ", sent " + CausesSent);
            // 2. The client raises one of its own: it arrives here as a request and runs on the host.
            yield return StartCoroutine(Wait(() => CausesFromClients > 0, 60, "story.host.request", "no story event request from the client"));
            if (tourFailure != null) yield break;
            Check("story.host.request", true, CausesFromClients + " requests, " + CausesSent + " causes sent");
            // 3. Next progression stage.
            yield return StartCoroutine(Pause(2));
            ProgressionManager pm = ProgressionManager.Instance;
            ProgressionListData list = progressionListField.GetValue(pm) as ProgressionListData;
            int next = Mathf.Clamp(pm.CurrentProgressionIndex + 1, 0, list.ProgressionStages.Count - 1);
            pm.SetStage(list.ProgressionStages[next]);
            Check("story.host.stage", pm.CurrentProgressionIndex == next, "stage " + CurrentStageName() + " (" + next + ")");
            yield return StartCoroutine(Pause(8));
            // 4. Into the garage.
            GlobalStorage.Instance.FadeAndLoadScene("RicksGarage", 1f);
            float loadAt = Time.realtimeSinceStartup;
            yield return StartCoroutine(Wait(() => { if (SceneManager.GetActiveScene().name != "RicksGarage" && Time.realtimeSinceStartup - loadAt > 20 && loadAt > 0) { loadAt = -1; Log("Fade load stalled on the host; loading directly"); SceneManager.LoadScene("RicksGarage"); } return SceneManager.GetActiveScene().name == "RicksGarage"; }, 60, "story.host.garage", () => "still in " + SceneManager.GetActiveScene().name));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(Wait(() => ActiveAreas().Count > 0, 60, "story.host.garage", "no garage area switched on"));
            if (tourFailure != null) yield break;
            Check("story.host.garage", true, "in the garage, areas on: " + string.Join(",", ActiveAreas().ToArray()));
            // Switch on one more mission area, as the blast-shield portal would: the client must switch it on too.
            yield return StartCoroutine(Pause(3));
            MultiSceneController areas = Areas();
            string extra = null;
            foreach (string n in areas.SceneNames) if (n != "Death" && !ActiveAreas().Contains(n) && areas.GetSceneController(n) != null) { extra = n; break; }
            if (extra == null) { tourFailure = "story.host.area"; Check("story.host.area", false, "no switched-off area to switch on"); yield break; }
            areas.GetSceneController(extra).BeginDelayedActivation();
            yield return StartCoroutine(Wait(() => ActiveAreas().Contains(extra), 30, "story.host.area", "area " + extra + " did not switch on"));
            if (tourFailure != null) yield break;
            Check("story.host.area", true, "switched on " + extra);
            // 5. Stay while the client follows and tries to leave on its own; then back to the living room.
            yield return StartCoroutine(Wait(() => { Peer p = FirstPeer(); return p != null && p.Pose.Scene == "RicksGarage"; }, 90, "story.host.sees.client", "the client never reached the garage"));
            if (tourFailure != null) yield break;
            Check("story.host.sees.client", true, "client is in the garage");
            yield return StartCoroutine(Pause(20));
            GlobalStorage.Instance.FadeAndLoadScene("LivingRoom", 1f);
            loadAt = Time.realtimeSinceStartup;
            yield return StartCoroutine(Wait(() => { if (SceneManager.GetActiveScene().name != "LivingRoom" && Time.realtimeSinceStartup - loadAt > 20 && loadAt > 0) { loadAt = -1; Log("Fade load stalled on the host; loading directly"); SceneManager.LoadScene("LivingRoom"); } return SceneManager.GetActiveScene().name == "LivingRoom"; }, 60, "story.host.back", () => "still in " + SceneManager.GetActiveScene().name));
            if (tourFailure != null) yield break;
            Check("story.host.back", true, "back in the living room");
            yield return StartCoroutine(Wait(() => { Peer p = FirstPeer(); return p != null && p.Pose.Scene == "LivingRoom"; }, 90, "story.host.sees.back", "the client never came back"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(Pause(5));
            Check("tour.done", tourFailure == null, "story");
        }

        IEnumerator StoryClient()
        {
            RigHead = new Vector3(1.0f, 1.35f, -0.6f); RigYaw = 0;
            yield return StartCoroutine(Wait(() => Active && LocalId > 0, 120, "tour.joined", "never welcomed by the host"));
            if (tourFailure != null) yield break;
            // 1. The host's story event runs here.
            yield return StartCoroutine(Wait(() => CausesReplayed > 0, 60, "story.client.cause", "no story event from the host"));
            if (tourFailure != null) yield break;
            Check("story.client.cause", true, "replayed " + LastReplayedCause);
            // 2. Raise one here: it must not run here on its own, only when the host sends it back.
            List<string> keys = CauseKeys();
            string mine = keys.Count > 1 ? keys[1] : "";
            int replayed = CausesReplayed;
            RaiseCause(mine);
            Check("story.client.held", CausesRequested > 0 && CausesReplayed == replayed, "raised " + mine + ": sent to the host, not run here");
            yield return StartCoroutine(Wait(() => LastReplayedCause == mine && CausesReplayed > replayed, 30, "story.client.request", () => "the host never sent " + mine + " back (last " + LastReplayedCause + ")"));
            if (tourFailure != null) yield break;
            Check("story.client.request", true, mine + " ran here after the host decided");
            // 3. The host's next stage.
            string before = CurrentStageName();
            yield return StartCoroutine(Wait(() => CurrentStageName() != before && CurrentStageName() == hostStage, 60, "story.client.stage", () => "stage here " + CurrentStageName() + ", host " + hostStage));
            if (tourFailure != null) yield break;
            Check("story.client.stage", true, "stage " + before + " -> " + CurrentStageName());
            // 4. Follow the host into the garage, with the same areas switched on.
            yield return StartCoroutine(Wait(() => SceneManager.GetActiveScene().name == "RicksGarage", 120, "story.client.garage", () => "still in " + SceneManager.GetActiveScene().name + ", host in " + HostScene));
            if (tourFailure != null) yield break;
            Check("story.client.garage", true, "followed the host into the garage");
            // The host switches on a second area after arriving; wait for it, then for the same set here.
            yield return StartCoroutine(Wait(() => { List<string> mineOn = ActiveAreas(); if (hostAreas.Count < 2 || mineOn.Count != hostAreas.Count) return false; foreach (string a in hostAreas) if (!mineOn.Contains(a)) return false; return true; }, 60, "story.client.areas", () => "areas here " + string.Join(",", ActiveAreas().ToArray()) + ", host " + string.Join(",", hostAreas.ToArray())));
            if (tourFailure != null) yield break;
            Check("story.client.areas", true, "areas on: " + string.Join(",", ActiveAreas().ToArray()));
            // 5. Try to go back to the living room alone: held back while the host stays.
            GlobalStorage.Instance.FadeAndLoadScene("LivingRoom", 1f);
            yield return StartCoroutine(Pause(4));
            Check("story.client.blocked", BlockedLoads > 0 && SceneManager.GetActiveScene().name == "RicksGarage", "own load held back " + BlockedLoads + "x, still in " + SceneManager.GetActiveScene().name);
            // 6. The host goes back to the living room (the DVD menu): follow.
            yield return StartCoroutine(Wait(() => SceneManager.GetActiveScene().name == "LivingRoom", 150, "story.client.back", () => "still in " + SceneManager.GetActiveScene().name + ", host in " + HostScene));
            if (tourFailure != null) yield break;
            Check("story.client.back", true, "followed the host back to the living room");
            yield return StartCoroutine(Pause(5));
            Check("tour.done", tourFailure == null, "story");
        }
    }
}
