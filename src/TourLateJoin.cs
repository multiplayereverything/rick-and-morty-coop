using System.Collections;
using OwlchemyVR;
using UnityEngine;

namespace RickAndMortyTogether
{
    // Tour "look": both players in the garage; the client stands next to Rick and the host steps away: Rick must look at
    // the client in both games (on the host at its drawn head).
    // Tour "latejoin": the host is in the garage a minute before the client tries to join (-rt-join-after): the game is
    // under way, so the host refuses it.
    public sealed partial class TogetherRuntime
    {
        static BrainControlledModule_CharacterHeadLook Looker(string name)
        {
            foreach (BrainControlledModule_CharacterHeadLook m in Object.FindObjectsOfType<BrainControlledModule_CharacterHeadLook>()) if (m.name.Contains(name) && LookAim(m) != null) return m;
            return null;
        }

        IEnumerator LateHost()
        {
            yield return StartCoroutine(IntoGarage("latejoin.host.refused"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(Wait(() => JoinsRefusedMidGame > 0, 300, "latejoin.host.refused", "no join was refused"));
            if (tourFailure != null) yield break;
            Check("latejoin.host.refused", AcceptedCount == 0, JoinsRefusedMidGame + " join(s) refused in " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + ", " + AcceptedCount + " accepted");
            Check("tour.done", tourFailure == null, "latejoin");
        }

        // A refused client: the host's message reaches it (net.rejected, checked by the transport code).
        IEnumerator LateClient() { yield break; }

        IEnumerator LookHost()
        {
            RigHead = new Vector3(0.2f, 1.35f, -0.6f); RigYaw = 0;
            yield return StartCoroutine(Wait(() => AcceptedCount >= 1, 120, "look.host.rick", "no client joined"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(IntoGarage("look.host.rick"));
            if (tourFailure != null) yield break;
            Peer client = null;
            yield return StartCoroutine(Wait(() => (client = FirstPeer()) != null && client.Pose.Scene == "RicksGarage" && client.Avatar != null && client.Avatar.Root.activeSelf, 300, "look.host.rick", "the client never arrived in the garage"));
            if (tourFailure != null) yield break;
            BrainControlledModule_CharacterHeadLook rick = null;
            yield return StartCoroutine(Wait(() => (rick = Looker("Rick")) != null, 30, "look.host.rick", "no Rick head-look here"));
            if (tourFailure != null) yield break;
            // Rick looks at "the player" (what LookAtPlayer does); this player steps 4 m away from him.
            AimSystem aim = LookAim(rick);
            Vector3 away = aim.transform.position + (Master.HMDCamera.transform.position - aim.transform.position).normalized * 4f;
            RigHead = new Vector3(away.x, 1.35f, away.z);
            // Rick's story keeps re-aiming him (at Morty, at "the player"); aim him at the player every second, as its
            // LookAtPlayer does, and the mod must turn that to the nearest player.
            float nextAim = 0;
            yield return StartCoroutine(Wait(() => { if (Time.realtimeSinceStartup > nextAim) { nextAim = Time.realtimeSinceStartup + 1; aim.SetTarget(Master.Head.transform); } return aim.Target == client.Avatar.Head; }, 90, "look.host.rick", () => "Rick looks at " + (aim.Target != null ? aim.Target.name : "nothing") + ", client " + (client.Avatar.Head.position - aim.transform.position).magnitude.ToString("0.0") + " m from him, host " + (Master.Head.transform.position - aim.transform.position).magnitude.ToString("0.0") + " m"));
            if (tourFailure != null) yield break;
            Check("look.host.rick", true, "Rick looks at the client's head here (" + (client.Avatar.Head.position - aim.transform.position).magnitude.ToString("0.0") + " m from him)");
            yield return StartCoroutine(Pause(15));
            Check("tour.done", tourFailure == null, "look");
        }

        IEnumerator LookClient()
        {
            yield return StartCoroutine(Wait(() => Active && LocalId > 0, 300, "tour.joined", "never welcomed by the host"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(Wait(() => UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "RicksGarage", 300, "look.client.rick", "never followed the host into the garage"));
            if (tourFailure != null) yield break;
            BrainControlledModule_CharacterHeadLook rick = null;
            yield return StartCoroutine(Wait(() => (rick = Looker("Rick")) != null, 30, "look.client.rick", "no Rick head-look here"));
            if (tourFailure != null) yield break;
            AimSystem aim = LookAim(rick);
            // Stand a metre in front of Rick; his look at "the player" must stay on this player (the nearest).
            Vector3 near = aim.transform.position + aim.transform.forward * 1f;
            RigHead = new Vector3(near.x, 1.35f, near.z);
            yield return StartCoroutine(Pause(5));
            // Rick's story also points him at Morty (not a player, left alone); he must never pick the host, the farther one.
            bool wrong = false; int here = 0;
            Peer host = FirstPeer();
            for (int n = 0; n < 15; n++)
            {
                aim.SetTarget(Master.Head.transform);
                yield return StartCoroutine(Pause(1));
                if (aim.Target == Master.Head.transform) here++;
                if (host != null && host.Avatar != null && aim.Target == host.Avatar.Head) wrong = true;
            }
            Check("look.client.rick", here > 0 && !wrong, "Rick looked at this player " + here + "/15 s, at the farther host " + wrong);
            Check("tour.done", tourFailure == null, "look");
        }
    }
}
