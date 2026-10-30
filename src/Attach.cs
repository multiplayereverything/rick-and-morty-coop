using HarmonyLib;
using OwlchemyVR;
using UnityEngine;

namespace RickAndMortyTogether
{
    // Parts in attach points (the computer's RAM stick, crystal and fuse, and every other socket): the owner's item state
    // carries the point the part is in (its scene path, "" for none), and every other copy puts its copy there or takes
    // it out. The game's own attach and detach run on each copy, so its OnAttach listeners (the computer module, the
    // story's ATTACHED_TO events, echoed once through the host like a replayed button press) see it everywhere.
    public sealed partial class TogetherRuntime
    {
        bool applyingAttach;               // session-scoped: an attach or detach made here from another copy's state
        public int AttachesApplied;        // session-scoped: lab counter

        static string PointKey(AttachablePoint p) { return p != null ? KeyOf(p.transform) : ""; }

        static string AttachKeyOf(NetItem item) { return item.Part != null ? PointKey(item.Part.CurrentlyAttachedTo) : ""; }

        static bool InPoint(NetItem item) { return item.Part != null && item.Part.CurrentlyAttachedTo != null; }

        static AttachablePoint FindPoint(string key)
        {
            foreach (AttachablePoint p in Object.FindObjectsOfType<AttachablePoint>()) if (KeyOf(p.transform) == key) return p;
            return null;
        }

        // A part the game made already in a point (the computer's parts, a refilled holder): every copy's game made the same
        // one there, so the point and the part's name give it the same id everywhere. The host's copy is in charge.
        void RegisterPointPart(GrabbableItem grab)
        {
            AttachableItem part = grab.GetComponent<AttachableItem>();
            string key = "part:" + PointKey(part.CurrentlyAttachedTo) + ":" + BaseName(grab.name);
            uint id = Hash(key);
            NetItem old;
            if (items.TryGetValue(id, out old) && old.Grab != null) { passedOver.Add(grab); return; }   // two alike in one point stay local
            Rigidbody body = grab.GetComponent<Rigidbody>();
            NetItem item = new NetItem { Id = id, Key = key, Grab = grab, Body = body, Part = part, Owner = 0, OriginalKinematic = body != null && body.isKinematic, PointPart = true, WasInPoint = true };
            items[id] = item; itemOf[grab] = item;
            if (!Hosting && body != null) StartFollowing(item);
            Log("Point part " + grab.name + " in " + part.CurrentlyAttachedTo.name + " -> " + id.ToString("x8"));
        }

        void ApplyAttach(NetItem item, string key)
        {
            if (item.Part == null || Mutant("noattachsync") || AttachKeyOf(item) == key) return;
            AttachablePoint point = key.Length > 0 ? FindPoint(key) : null;
            if (key.Length > 0 && point == null) { Log("Attach: no point " + key + " here for " + item.Grab.name); return; }
            applyingAttach = true;
            try
            {
                if (item.Part.CurrentlyAttachedTo != null) item.Part.Detach();
                if (point != null)
                {
                    // The game's points hold one part: whatever this copy still has there came out on the owner's.
                    foreach (AttachableItem other in point.AttachedItems.ToArray())
                    {
                        if (other == null || other == item.Part) continue;
                        other.Detach();
                        NetItem was;
                        GrabbableItem otherGrab = other.GetComponent<GrabbableItem>();
                        if (otherGrab != null && itemOf.TryGetValue(otherGrab, out was) && was.Owner == LocalId) was.AttachDirty = true;
                    }
                    item.Part.AttachTo(point);
                }
            }
            finally { applyingAttach = false; }
            AttachesApplied++;
            RefreshBody(item);
            Log("Attach: " + item.Grab.name + " -> " + (point != null ? point.name : "out") + " (from the owner)");
        }

        // The game removes a part's rigidbody in a point and makes a new one when it comes out.
        void RefreshBody(NetItem item)
        {
            if (item.Body != null) return;
            item.Body = item.Grab.GetComponent<Rigidbody>();
            if (item.Body == null || item.Owner == LocalId) return;
            item.Body.isKinematic = true; item.Following = true;
        }

        // Harmony postfixes on AttachableItem.AttachTo and Detach: this copy's own player (or game) moved a part it owns.
        public void OnLocalAttach(AttachableItem part)
        {
            NetItem item;
            GrabbableItem grab = part != null ? part.GetComponent<GrabbableItem>() : null;
            if (!Active || applyingAttach || grab == null || !itemOf.TryGetValue(grab, out item)) return;
            if (item.Owner == LocalId) item.AttachDirty = true;
        }
    }

    [HarmonyPatch(typeof(AttachableItem), "AttachTo")]
    static class AttachToPatch { static void Postfix(AttachableItem __instance) { if (TogetherRuntime.Instance != null) TogetherRuntime.Instance.OnLocalAttach(__instance); } }

    [HarmonyPatch(typeof(AttachableItem), "Detach")]
    static class DetachPatch { static void Postfix(AttachableItem __instance) { if (TogetherRuntime.Instance != null) TogetherRuntime.Instance.OnLocalAttach(__instance); } }
}
