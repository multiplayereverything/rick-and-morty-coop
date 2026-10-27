using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using OwlchemyVR;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RickAndMortyTogether
{
    // Lab-only instrumentation, enabled by -rt-lab. It writes a per-instance log with RT-CHECK, RT-METRIC and RT-STATE
    // lines, which tests/verify-lab.py reads to judge pass/fail, error growth, memory growth, frame cost and desync.
    public sealed partial class TogetherRuntime
    {
        public static bool LabMode;   // lab-only
        static StreamWriter labLog;   // lab-only
        static readonly object labLogGate = new object();   // lab-only
        static int errorCount, modErrorCount;   // lab-only
        // -rt-mutate a,b turns those fixes off, so scripts/mutants.ps1 can prove each tour fails without its fix.
        static readonly HashSet<string> mutants = new HashSet<string>();   // lab-only
        float nextMetric, nextState;
        float labJoinRetryAt;
        string labTour = "";
        readonly System.Diagnostics.Stopwatch labWatch = new System.Diagnostics.Stopwatch();
        long labModTicks; int labFrames; float labWindowStart;

        void InitLab(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-rt-lab") LabMode = true;
                if (args[i] == "-rt-tour" && i + 1 < args.Length) labTour = args[++i];
                if (args[i] == "-rt-mutate" && i + 1 < args.Length) foreach (string m in args[++i].Split(',')) if (m.Length > 0) mutants.Add(m);
                if (args[i] == "-rt-lag" && i + 1 < args.Length) { int lag; if (int.TryParse(args[++i], out lag)) { Transport.LagMs = Mathf.Clamp(lag, 0, 1000); Transport.JitterMs = Transport.LagMs / 2; } }
            }
            if (!LabMode) { Transport.LagMs = 0; Transport.JitterMs = 0; mutants.Clear(); return; }
            try
            {
                labLog = new StreamWriter(Path.Combine(Root, "rick-and-morty-together-lab.log"), false) { AutoFlush = true };
                Action<string> previous = Log;
                Log = message => { previous(message); WriteLab(message); };
                foreach (string failure in PatchFailures) { Interlocked.Increment(ref modErrorCount); WriteLab("RT-MODERROR patch " + failure); }
                if (mutants.Count > 0) Log("Lab: fixes turned off: " + string.Join(",", new List<string>(mutants).ToArray()));
            }
            catch (Exception ex) { Log("Lab log unavailable: " + ex.Message); }
            Application.logMessageReceivedThreaded += CountErrors;
        }

        static void WriteLab(string line)
        {
            lock (labLogGate) { if (labLog != null) labLog.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " " + line); }
        }

        static void CountErrors(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            int count = Interlocked.Increment(ref errorCount);
            if (count <= 200)
            {
                string at = "";
                if (stackTrace != null) foreach (string line in stackTrace.Split('\n')) if (line.Trim().Length > 0) { at = line.Trim(); break; }
                WriteLab("RT-ERROR " + (condition ?? "").Replace('\n', ' ') + " | " + at);
            }
            if ((stackTrace != null && stackTrace.Contains("RickAndMortyTogether")) || (condition != null && condition.Contains("RickAndMortyTogether")))
            {
                Interlocked.Increment(ref modErrorCount);
                WriteLab("RT-MODERROR " + condition + " | " + (stackTrace ?? "").Replace('\n', ' '));
            }
        }

        // True when the lab run turned this fix off (never outside -rt-lab).
        public static bool Mutant(string name) { return LabMode && mutants.Contains(name); }

        public static void Check(string name, bool pass, string detail)
        {
            if (LabMode) Log("RT-CHECK " + name + " " + (pass ? "PASS" : "FAIL") + " " + detail);
        }

        static string V(Vector3 v) { return v.x.ToString("0.000") + "," + v.y.ToString("0.000") + "," + v.z.ToString("0.000"); }
        static string Q(Quaternion q) { return q.x.ToString("0.0000") + "," + q.y.ToString("0.0000") + "," + q.z.ToString("0.0000") + "," + q.w.ToString("0.0000"); }

        void LabUpdate()
        {
            if (!LabMode) return;
            float now = Time.realtimeSinceStartup;
            // Lab clients may start before the host listens; keep retrying instead of giving up.
            if (autoRole == "join" && transport == null && now >= labJoinRetryAt) { labJoinRetryAt = now + 3; StartJoin(); }
            if (now >= nextMetric)
            {
                nextMetric = now + 2;
                long heap = GC.GetTotalMemory(false);
                long unity = Profiler.GetTotalAllocatedMemory();
                GameObject[] all = Resources.FindObjectsOfTypeAll<GameObject>();
                int modObjects = 0;
                string modNames = "";
                foreach (GameObject go in all) if (go.name.StartsWith("RickAndMortyTogether", StringComparison.Ordinal) && go.transform.parent == null) { modObjects++; if (modObjects > 1 + peers.Count) modNames += " " + go.name; }
                if (modNames.Length > 0) Log("Mod objects over the expected count:" + modNames);
                int avatars = 0; foreach (Peer peer in peers.Values) if (peer.Avatar != null) avatars++;
                float window = Mathf.Max(0.001f, now - labWindowStart);
                double modMs = labFrames > 0 ? labModTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / labFrames : 0;
                float fps = labFrames / window;
                labModTicks = 0; labFrames = 0; labWindowStart = now;
                Log(string.Format("RT-METRIC t={0:0} role={1} scene={2} peers={3} avatars={4} items={5} heapMB={6:0.0} unityMB={7:0.0} objects={8} errors={9} moderrors={10} modms={11:0.000} fps={12:0.0} modobjects={13}",
                    now, Hosting ? "host" : "client", SceneManager.GetActiveScene().name, AcceptedCount, avatars, items.Count, heap / 1048576.0, unity / 1048576.0, all.Length, errorCount, modErrorCount, modMs, fps, modObjects));
            }
            // State samples on a shared host-time grid, so the verifier can pair the same moment across copies.
            float ht = HostTime;
            // A client's host clock is only right after its first ping round-trip; samples before that are skipped.
            if (Active && (Hosting || lastRtt >= 0) && ht >= nextState)
            {
                nextState = (Mathf.Floor(ht / 2f) + 1) * 2f;
                StringBuilder s = new StringBuilder("RT-STATE ht=" + ht.ToString("0.000") + " id=" + LocalId + " scene=" + SceneManager.GetActiveScene().name);
                PlayerPose me;
                if (CaptureLocalPose(out me)) s.Append(" self.head=" + V(me.HeadPosition) + " self.L=" + V(me.Left.Position) + " self.R=" + V(me.Right.Position) + " self.Rgrip=" + me.Right.Grip.ToString("0.00"));
                // Only avatars drawn here (same scene): one in another scene is hidden and not a desync.
                foreach (Peer p in peers.Values) if (p.Avatar != null && p.Avatar.Root.activeSelf) s.Append(" av" + p.Id + ".head=" + V(p.Avatar.Head.position) + " av" + p.Id + ".R=" + V(p.Avatar.RightHand.position) + " av" + p.Id + ".Rclosed=" + (p.Avatar.AnimatorClosed(Handedness.Right) ? 1 : 0));
                foreach (NetItem item in items.Values)
                {
                    if (item.LastMoved <= 0 && !labWatched.Contains(item.Id)) continue;
                    if (item.Control != null)
                    {
                        s.Append(" ctl" + item.Id.ToString("x8") + "=" + Mathf.Clamp01(item.Control.NormalizedAxisValue).ToString("0.0000") + "|" + LockBits(item.Control) + "|o" + item.Owner + "|e" + item.Epoch + "|" + ItemMode(item));
                        continue;
                    }
                    if (item.Body == null) continue;
                    s.Append(" item" + item.Id.ToString("x8") + "=" + V(item.Body.transform.position) + "|" + Q(item.Body.transform.rotation) + "|o" + item.Owner + "|e" + item.Epoch + "|" + ItemMode(item));
                }
                Log(s.ToString());
            }
            TourUpdate();
        }

        readonly HashSet<uint> labWatched = new HashSet<uint>();

        string ItemMode(NetItem item)
        {
            if (item.Grab.IsCurrInHand) return "held";
            if (item.Owner != LocalId) return "follow";
            bool settled = item.LastMoved <= 0 || (item.SentRest && Time.realtimeSinceStartup - item.LastMoved > 1f);
            return settled ? "rest" : "moving";
        }
    }
}
