using System;
using OwlchemyVR;
using UnityEngine;

namespace RickAndMortyTogether
{
    // A remote player drawn as Morty: the game's own Morty-clone head (HeadControllerRAM's CloneHeadDynamic, the
    // model the game puts on the player for mirrors and the spectator camera), a Morty body under it that follows the
    // head's position and yaw, and two Morty hands (copies of the player's own MortyHand) that open and close with
    // the remote grip. Copies are made inside an inactive parent and stripped of every game script, collider and
    // rigidbody before they are switched on, so no game code ever runs on them.
    public sealed class Avatar
    {
        public readonly GameObject Root;
        public readonly Transform Head, Body, LeftHand, RightHand;
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
            Body = MakeBody();
            Body.SetParent(Root.transform, false);
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
        static GameObject Inert(GameObject source)
        {
            GameObject holder = new GameObject("RickAndMortyTogether Copy");
            holder.SetActive(false);
            GameObject copy = (GameObject)UnityEngine.Object.Instantiate(source, holder.transform);
            foreach (MonoBehaviour b in copy.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.DestroyImmediate(b);
            foreach (Joint j in copy.GetComponentsInChildren<Joint>(true)) UnityEngine.Object.DestroyImmediate(j);
            foreach (Collider c in copy.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
            foreach (Rigidbody r in copy.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.DestroyImmediate(r);
            foreach (AudioSource a in copy.GetComponentsInChildren<AudioSource>(true)) UnityEngine.Object.DestroyImmediate(a);
            foreach (Transform t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
            copy.transform.SetParent(null, false);
            UnityEngine.Object.DestroyImmediate(holder);
            return copy;
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

        // Morty's yellow shirt and blue trousers, hanging from the neck; the shoulders turn with the head's yaw.
        static Transform MakeBody()
        {
            GameObject body = new GameObject("Body");
            GameObject shirt = Shape(PrimitiveType.Capsule, new Color(0.98f, 0.86f, 0.18f));
            shirt.transform.SetParent(body.transform, false);
            shirt.transform.localPosition = new Vector3(0, -0.45f, 0); shirt.transform.localScale = new Vector3(0.36f, 0.3f, 0.24f);
            GameObject trousers = Shape(PrimitiveType.Capsule, new Color(0.17f, 0.36f, 0.72f));
            trousers.transform.SetParent(body.transform, false);
            trousers.transform.localPosition = new Vector3(0, -1.0f, 0); trousers.transform.localScale = new Vector3(0.3f, 0.36f, 0.22f);
            return body.transform;
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
        bool placed;

        public void Follow(PlayerPose pose, float dt)
        {
            float k = placed ? 1f - Mathf.Exp(-dt * 18f) : 1f;
            placed = true;
            Move(Head, pose.HeadPosition, pose.HeadRotation, k);
            Vector3 forward = pose.HeadRotation * Vector3.forward; forward.y = 0;
            Quaternion yaw = forward.sqrMagnitude > 0.001f ? Quaternion.LookRotation(forward) : Body.rotation;
            Move(Body, pose.HeadPosition, yaw, k * 0.6f);
            Move(LeftHand, pose.Left.Position, pose.Left.Rotation, k);
            Move(RightHand, pose.Right.Position, pose.Right.Rotation, k);
            LeftGrip = pose.Left.Grip; RightGrip = pose.Right.Grip;
            SetClosed(leftAnimator, ref leftClosed, pose.Left.Grip > 0.5f);
            SetClosed(rightAnimator, ref rightClosed, pose.Right.Grip > 0.5f);
        }

        static void Move(Transform t, Vector3 position, Quaternion rotation, float k)
        {
            if ((t.position - position).sqrMagnitude > 4f) { t.position = position; t.rotation = rotation; return; }
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
                if (p.Avatar == null) { p.Avatar = new Avatar(p.Id, p.Name); Log("Avatar for " + p.Name + " (Morty head " + p.Avatar.HeadIsMorty + ", Morty hands " + p.Avatar.HandsAreMorty + ")"); }
                p.Avatar.SetName(p.Name);
                // Someone in another scene of the game is not drawn here.
                bool here = p.Pose.Scene == scene;
                if (p.Avatar.Root.activeSelf != here) p.Avatar.Root.SetActive(here);
                if (here) p.Avatar.Follow(p.Pose, dt);
            }
        }
    }
}
