using System.Collections;
using OwlchemyVR;
using UnityEngine;

namespace RickAndMortyTogether
{
    // Tour "boss": in the garage the host sets the BossBattle stage (the game then shows the final boss), the host
    // starts its entrance and the battle with the game's own calls (what the Brain does), the client must show the same
    // states; then the client tries a state change of its own, which must wait for the host.
    public sealed partial class TogetherRuntime
    {
        // The game shows the boss at the BossBattle stage: the host sets it (the client follows the host's stage).
        IEnumerator BringBossOn(string check)
        {
            if (Hosting)
            {
                ProgressionStageData stage = null;
                ProgressionListData list = progressionListField.GetValue(ProgressionManager.Instance) as ProgressionListData;
                foreach (ProgressionStageData s in list.ProgressionStages) if (s != null && s.name.StartsWith("Boss")) stage = s;
                if (stage == null) { tourFailure = check; Check(check, false, "no boss stage"); yield break; }
                Log("Boss stage: " + stage.name);
                ProgressionManager.Instance.SetStage(stage);
            }
            yield return StartCoroutine(Wait(() => Boss() != null, 60, check, () => "the boss never came on (stage " + CurrentStageName() + ")"));
        }

        IEnumerator BossHost()
        {
            RigRelative = true; RigHead = new Vector3(-0.3f, 1.35f, 0); RigYaw = 0;
            yield return StartCoroutine(Wait(() => AcceptedCount >= 1, 120, "tour.peers", "no client joined"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(Pause(4));
            yield return StartCoroutine(IntoGarage("boss.host.on"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(BringBossOn("boss.host.on"));
            if (tourFailure != null) yield break;
            Check("boss.host.on", true, "boss state " + BossState(Boss()));
            yield return StartCoroutine(Wait(() => { Peer p = FirstPeer(); return p != null && p.Pose.Scene == "RicksGarage"; }, 120, "boss.host.states", "the client never reached the garage"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(Pause(10));
            FinalBossController b = Boss();
            b.EnterScene();
            yield return StartCoroutine(Pause(8));
            b.StartBattle();
            yield return StartCoroutine(Pause(2));
            Check("boss.host.states", BossStatesSent >= 2 && BossState(b) == 2, "states sent " + BossStatesSent + ", now " + BossState(b));
            yield return StartCoroutine(Pause(20));
            Check("tour.done", tourFailure == null, "boss");
        }

        IEnumerator BossClient()
        {
            RigRelative = true; RigHead = new Vector3(0.6f, 1.35f, 0); RigYaw = 0;
            yield return StartCoroutine(Wait(() => Active && LocalId > 0, 120, "tour.joined", "never welcomed by the host"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(IntoGarage("boss.client.states"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(BringBossOn("boss.client.states"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(Wait(() => BossState(Boss()) == 2 && BossStatesShown >= 2, 120, "boss.client.states", () => "boss here in state " + BossState(Boss()) + ", shown " + BossStatesShown));
            if (tourFailure != null) yield break;
            Check("boss.client.states", true, "entered and fighting like the host's (" + BossStatesShown + " states from the host)");
            Boss().WaitForFood();
            yield return StartCoroutine(Pause(4));
            Check("boss.client.held", BossState(Boss()) == 2 && BossStatesHeld >= 1, "its own change held: state " + BossState(Boss()) + ", held " + BossStatesHeld);
            yield return StartCoroutine(Pause(6));
            Check("tour.done", tourFailure == null, "boss");
        }
    }
}
