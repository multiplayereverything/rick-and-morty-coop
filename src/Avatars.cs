using System;
using System.Collections.Generic;
using OwlchemyVR;
using UnityEngine;

namespace RickAndMortyTogether
{
    // A remote player drawn as Morty: the game's own Morty-clone head (HeadControllerRAM's CloneHeadDynamic, the
    // model the game puts on the player for mirrors and the spectator camera) and two Morty hands (copies of the player's own MortyHand) that open and close with
    // the remote grip. Copies are made inside an inactive parent and stripped of every game script, collider and
    // rigidbody before they are switched on, so no game code ever runs on them.
    public sealed class Avatar
    {
        public readonly GameObject Root;
        public readonly Transform Head, LeftHand, RightHand;
        // This player's spectator camera, when it is out in the world (made on first use).
        public Transform Camera;
        bool headHidden;
        readonly Animator leftAnimator, rightAnimator;
        bool leftClosed, rightClosed;
        public float LeftGrip, RightGrip;
        public bool HeadIsMorty, HandsAreMorty;
        TextMesh label;

        public Avatar(int id, string name)
        {
            Root = new GameObject("RickAndMortyTogether Avatar " + id);
            UnityEngine.Object.DontDestroyOnLoad(Root);
            MasterHMDAndInputController m = TogetherRuntime.Master;
            Head = MakeHead(m);
            Head.SetParent(Root.transform, false);
            LeftHand = MakeHand(m, Handedness.Left, out leftAnimator);
            RightHand = MakeHand(m, Handedness.Right, out rightAnimator);
            LeftHand.SetParent(Root.transform, false);
            RightHand.SetParent(Root.transform, false);
            GameObject tag = new GameObject("Name");
            tag.transform.SetParent(Head, false);
            tag.transform.localPosition = new Vector3(0, 0.32f, 0);
            label = tag.AddComponent<TextMesh>();
            label.text = name; label.characterSize = 0.02f; label.fontSize = 64; label.anchor = TextAnchor.MiddleCenter; label.color = new Color(1f, 0.93f, 0.55f);
        }

        public void SetName(string name) { if (label != null && label.text != name) label.text = name; }

        // Copies a live object without letting its scripts wake: instantiate under an inactive parent, strip, then move out.
        public static GameObject Inert(GameObject source)
        {
            GameObject holder = new GameObject("RickAndMortyTogether Copy");
            holder.SetActive(false);
            GameObject copy = (GameObject)UnityEngine.Object.Instantiate(source, holder.transform);
            // Unity refuses to remove a script another one still there requires (TextMeshPro needs its TextContainer,
            // PickupableItem its InteractableItem), and component order does not follow those needs: remove in passes,
            // each taking the scripts nothing left requires.
            List<MonoBehaviour> scripts = new List<MonoBehaviour>(copy.GetComponentsInChildren<MonoBehaviour>(true));
            if (TogetherRuntime.Mutant("noinertorder")) scripts.Reverse();
            while (scripts.Count > 0)
            {
                List<MonoBehaviour> free = TogetherRuntime.Mutant("noinertorder") ? scripts : scripts.FindAll(s => !Required(s, scripts));
                if (free.Count == 0) free = scripts;   // a cycle: remove them anyway
                foreach (MonoBehaviour script in free) UnityEngine.Object.DestroyImmediate(script);
                scripts.RemoveAll(s => s == null);
                if (free == scripts) break;
            }
            foreach (Joint j in copy.GetComponentsInChildren<Joint>(true)) UnityEngine.Object.DestroyImmediate(j);
            foreach (Collider c in copy.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
            foreach (Rigidbody r in copy.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.DestroyImmediate(r);
            foreach (AudioSource a in copy.GetComponentsInChildren<AudioSource>(true)) UnityEngine.Object.DestroyImmediate(a);
            foreach (Transform t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
            copy.transform.SetParent(null, false);
            UnityEngine.Object.DestroyImmediate(holder);
            return copy;
        }

        // Whether another script on the same object still needs this one (a RequireComponent of its class or a base class).
        static bool Required(MonoBehaviour script, List<MonoBehaviour> left)
        {
            foreach (MonoBehaviour other in left)
            {
                if (other == script || other == null || other.gameObject != script.gameObject) continue;
                foreach (RequireComponent r in other.GetType().GetCustomAttributes(typeof(RequireComponent), true))
                    foreach (Type t in new[] { r.m_Type0, r.m_Type1, r.m_Type2 })
                        if (t != null && t.IsAssignableFrom(script.GetType())) return true;
            }
            return false;
        }

        Transform MakeHead(MasterHMDAndInputController m)
        {
            Transform face = m != null && m.Head != null ? m.Head.transform.Find("CloneHeadDynamic") : null;
            if (face != null)
            {
                GameObject copy = Inert(face.gameObject);
                // The player's own head is hidden from its own eyes; this copy is for everyone else's.
                foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true)) { r.enabled = true; r.gameObject.SetActive(true); }
                copy.SetActive(true);
                GameObject pivot = new GameObject("Head");
                copy.transform.SetParent(pivot.transform, false);
                copy.transform.localPosition = face.localPosition; copy.transform.localRotation = face.localRotation; copy.transform.localScale = face.lossyScale;
                HeadIsMorty = true;
                return pivot.transform;
            }
            GameObject ball = Shape(PrimitiveType.Sphere, new Color(0.98f, 0.85f, 0.7f));
            ball.transform.localScale = new Vector3(0.26f, 0.24f, 0.26f);
            GameObject p = new GameObject("Head");
            ball.transform.SetParent(p.transform, false);
            return p.transform;
        }

        Transform MakeHand(MasterHMDAndInputController m, Handedness side, out Animator animator)
        {
            animator = null;
            HandController hand = null;
            if (m != null) foreach (HandController h in m.Hands) if (h != null && h.Handedness == side) hand = h;
            AnimatedHandController visual = hand != null ? hand.GetComponentInChildren<AnimatedHandController>(true) : null;
            if (visual != null)
            {
                GameObject copy = Inert(visual.gameObject);
                foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
                copy.transform.localScale = visual.transform.lossyScale;
                copy.SetActive(true);
                animator = copy.GetComponentInChildren<Animator>(true);
                HandsAreMorty = true;
                copy.name = "Hand" + side;
                return copy.transform;
            }
            GameObject mitt = Shape(PrimitiveType.Cube, new Color(0.98f, 0.85f, 0.7f));
            mitt.transform.localScale = new Vector3(0.08f, 0.04f, 0.12f);
            mitt.name = "Hand" + side;
            return mitt.transform;
        }

        static GameObject Shape(PrimitiveType type, Color color)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            Renderer r = go.GetComponent<Renderer>();
            Shader s = Shader.Find("Standard") ?? Shader.Find("Diffuse");
            r.material = new Material(s) { color = color };
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        // Smoothly toward the latest pose (20 Hz stream); snaps after a teleport.
        bool placed, cameraPlaced;

        // Hidden while a spectator watches through this player's eyes (the head would fill the view).
        public void SetHeadHidden(bool hidden)
        {
            if (headHidden == hidden) return;
            headHidden = hidden;
            foreach (Renderer r in Head.GetComponentsInChildren<Renderer>(true)) r.enabled = !hidden;
        }
        public bool HeadHidden { get { return headHidden; } }

        // Shown again after being in another scene: the next pose places it outright.
        public void Unplace() { placed = false; cameraPlaced = false; }

        public void Follow(PlayerPose pose, float dt)
        {
            // A jump of more than half a metre is a teleport or a scene change, not movement: snap, don't glide.
            bool jump = (Head.position - pose.HeadPosition).sqrMagnitude > 0.25f;
            float k = placed && !jump ? 1f - Mathf.Exp(-dt * 18f) : 1f;
            placed = true;
            Move(Head, pose.HeadPosition, pose.HeadRotation, k);
            Move(LeftHand, pose.Left.Position, pose.Left.Rotation, k);
            Move(RightHand, pose.Right.Position, pose.Right.Rotation, k);
            if (pose.CameraShown && Camera == null) Camera = TogetherRuntime.MakeSpectatorCameraCopy(Root.transform);
            if (Camera != null)
            {
                if (Camera.gameObject.activeSelf != pose.CameraShown) { Camera.gameObject.SetActive(pose.CameraShown); cameraPlaced = false; }
                if (pose.CameraShown) { Move(Camera, pose.CameraPosition, pose.CameraRotation, cameraPlaced ? k : 1f); cameraPlaced = true; }
            }
            // The name reads the right way round from wherever this player looks at it (it would follow the head's turn
            // otherwise, and read mirrored from the front).
            MasterHMDAndInputController m = TogetherRuntime.Master;
            if (label != null && m != null && m.HMDCamera != null)
            {
                Vector3 away = label.transform.position - m.HMDCamera.transform.position;
                if (away.sqrMagnitude > 0.0001f) label.transform.rotation = Quaternion.LookRotation(away, Vector3.up);
            }
            LeftGrip = pose.Left.Grip; RightGrip = pose.Right.Grip;
            SetClosed(leftAnimator, ref leftClosed, pose.Left.Grip > 0.5f);
            SetClosed(rightAnimator, ref rightClosed, pose.Right.Grip > 0.5f);
        }

        static void Move(Transform t, Vector3 position, Quaternion rotation, float k)
        {
            // More than half a metre in one pose (a teleport, a flat-screen hand snapping back): snap, don't glide.
            if ((t.position - position).sqrMagnitude > 0.25f) { t.position = position; t.rotation = rotation; return; }
            t.position = Vector3.Lerp(t.position, position, k);
            t.rotation = Quaternion.Slerp(t.rotation, rotation, k);
        }

        static void SetClosed(Animator animator, ref bool closed, bool want)
        {
            if (animator == null || closed == want) return;
            closed = want;
            try { animator.CrossFade(want ? "HandClose" : "HandOpen", 0.08f, 0); }
            catch (Exception) { }   // a hand rig without these states still shows the right place
        }

        public bool AnimatorClosed(Handedness side) { return side == Handedness.Left ? leftClosed : rightClosed; }

        public void Destroy() { if (Root != null) UnityEngine.Object.Destroy(Root); }
    }

    public sealed partial class TogetherRuntime
    {
        void AvatarsUpdate()
        {
            float dt = Time.deltaTime;
            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            foreach (Peer p in peers.Values)
            {
                if (!p.HasPose || Master == null) continue;
                if (p.Avatar != null && Hosting && !Mutant("noenemytarget")) RemoteHead.Attach(p.Avatar, p.Id);
                if (p.Avatar == null) { p.Avatar = new Avatar(p.Id, p.Name); Log("Avatar for " + p.Name + " (Morty head " + p.Avatar.HeadIsMorty + ", Morty hands " + p.Avatar.HandsAreMorty + ")"); }
                p.Avatar.SetName(p.Name);
                // Someone in another scene of the game is not drawn here.
                bool here = p.Pose.Scene == scene;
                if (p.Avatar.Root.activeSelf != here) { p.Avatar.Root.SetActive(here); if (here) p.Avatar.Unplace(); }
                if (here) p.Avatar.Follow(p.Pose, dt);
            }
        }
    }
}
