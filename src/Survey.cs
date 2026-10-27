using System;
using System.Collections;
using System.Reflection;
using System.Text;
using OwlchemyVR;
using UnityEngine;

namespace RickAndMortyTogether
{
    // Lab-only: lists every lever, door, slider, button and pull cord in the scene with who listens to it, so tours
    // pick controls whose effects are safe to press in a test.
    public sealed partial class TogetherRuntime
    {
        static string Listeners(object target, string eventName)
        {
            for (Type t = target.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (f == null) continue;
                Delegate d = f.GetValue(target) as Delegate;
                if (d == null) return "";
                StringBuilder s = new StringBuilder();
                foreach (Delegate x in d.GetInvocationList()) s.Append(" " + (x.Target != null ? x.Target.GetType().Name : "static") + "." + x.Method.Name);
                return s.ToString();
            }
            return "";
        }

        // Tour "portrait": the two players face each other a metre apart and each copy saves what its camera sees
        // (portrait-host.png / portrait-c1.png in the lab folder), as a picture of the other player's Morty avatar.
        IEnumerator Portrait()
        {
            bool host = Hosting || autoRole == "host" || autoRole == "steamhost";
            RigHead = host ? new Vector3(0.2f, 1.35f, -0.6f) : new Vector3(1.3f, 1.35f, -0.6f);
            RigYaw = host ? 90 : -90;
            float x = host ? 0.45f : 1.05f;
            RigSetHand(Handedness.Right, new Vector3(x, 1.2f, host ? -0.4f : -0.8f));
            RigSetHand(Handedness.Left, new Vector3(x, 1.2f, host ? -0.8f : -0.4f));
            yield return StartCoroutine(Wait(() => FirstPeer() != null && FirstPeer().Avatar != null, 120, "portrait.seen", "no remote player pose arrived"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(Pause(5));
            Peer p = FirstPeer();
            Check("avatar.morty", p.Avatar.HeadIsMorty && p.Avatar.HandsAreMorty, "Morty head " + p.Avatar.HeadIsMorty + ", Morty hands " + p.Avatar.HandsAreMorty);
            string file = System.IO.Path.Combine(Root, "portrait-" + (host ? "host" : "c1") + ".png");
            Application.CaptureScreenshot(file, 3);
            yield return StartCoroutine(Wait(() => System.IO.File.Exists(file), 10, "portrait.saved", "no screenshot written"));
            if (tourFailure != null) yield break;
            Check("portrait.saved", true, file);
            yield return StartCoroutine(Pause(3));
            Check("tour.done", tourFailure == null, "portrait");
        }

        // Tour "full": three copies start; the host keeps the first joiner and refuses the second (two players at most).
        IEnumerator FullGame()
        {
            if (!(Hosting || autoRole == "host")) yield break;
            yield return StartCoroutine(Wait(() => AcceptedCount >= 1, 120, "full.host.one", "no client joined"));
            if (tourFailure != null) yield break;
            yield return StartCoroutine(Pause(25));
            Check("full.host.one", AcceptedCount == 1, AcceptedCount + " other player(s) in the game");
            Check("tour.done", tourFailure == null, "full");
        }

        IEnumerator Survey()
        {
            yield return StartCoroutine(Pause(8));
            foreach (LimitedIndirectGrabbableController c in UnityEngine.Object.FindObjectsOfType<LimitedIndirectGrabbableController>())
            {
                string ev = "";
                foreach (string e in new[] { "OnUpperLocked", "OnLowerLocked", "OnUpperUnlocked", "OnLowerUnlocked", "OnSnapIndexSelected", "OnHingeActivated", "OnHingeReset", "OnReleasedAtSnapIndex" })
                {
                    string l = Listeners(c, e);
                    if (l.Length > 0) ev += " " + e + ":" + l;
                }
                Log("RT-SURVEY control " + c.GetType().Name + " " + KeyOf(c.transform) + " at " + V(c.transform.position) + " physics=" + c.UsePhysics + " axis=" + c.Axis + " limits=" + c.LowerLimit + ".." + c.UpperLimit + " norm=" + c.NormalizedAxisValue.ToString("0.00") + " upper=" + c.IsUpperLocked + " lower=" + c.IsLowerLocked + " input=" + c.HandInputType + " rb=" + (c.GetComponent<Rigidbody>() != null) + " |" + ev);
            }
            foreach (PushableButton b in UnityEngine.Object.FindObjectsOfType<PushableButton>())
                Log("RT-SURVEY button " + KeyOf(b.transform) + " at " + V(b.transform.position) + " |" + Listeners(b, "OnButtonPushed"));
            foreach (PullCordController p in UnityEngine.Object.FindObjectsOfType<PullCordController>())
                Log("RT-SURVEY cord " + KeyOf(p.transform) + " at " + V(p.transform.position) + " |" + Listeners(p, "OnPullCordPulled"));
            Check("tour.done", true, "survey");
        }
    }
}
