using System;
using System.IO;
using UnityEngine;

namespace RickAndMortyTogether
{
    public enum Op : byte { Hello = 1, Welcome, Reject, Roster, Leave, Ping, Pong, Pose, Scene, ItemClaim, ItemOwner, ItemState, ControlState, TriggerFired, Cause, Stage, Area, Areas, Spawn, Despawn, Enemies, EnemyHit, PlayerHit, BossState }

    public static class Wire
    {
        // Bump on every protocol change; a joiner with another version is refused with both versions in the message.
        public const int Version = 7;

        public static byte[] Packet(Op op, Action<BinaryWriter> write)
        {
            using (MemoryStream memory = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(memory))
            {
                writer.Write((byte)op); if (write != null) write(writer);
                writer.Flush();
                if (memory.Length > Transport.MaxFrame) throw new IOException("Packet too large.");
                return memory.ToArray();
            }
        }

        public static string Text(BinaryReader reader, int max)
        {
            // Explicit byte lengths avoid BinaryReader.ReadString allocating from a peer supplied prefix.
            int count = reader.ReadUInt16();
            if (count > max * 4 || count > reader.BaseStream.Length - reader.BaseStream.Position) throw new IOException("Invalid text length.");
            string value = System.Text.Encoding.UTF8.GetString(Transport.ReadExactly(reader.BaseStream, count));
            if (value.Length > max) throw new IOException("Text too long.");
            return value;
        }

        public static void Text(BinaryWriter writer, string value)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value ?? "");
            if (bytes.Length > 4096) throw new IOException("Text too long.");
            writer.Write((ushort)bytes.Length); writer.Write(bytes);
        }

        public static float Number(BinaryReader reader)
        {
            float value = reader.ReadSingle();
            if (float.IsNaN(value) || float.IsInfinity(value) || Math.Abs(value) > 100000) throw new IOException("Invalid number.");
            return value;
        }

        public static void Vec(BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
        public static Vector3 Vec(BinaryReader r) { return new Vector3(Number(r), Number(r), Number(r)); }

        public static void Rot(BinaryWriter w, Quaternion q) { w.Write(q.x); w.Write(q.y); w.Write(q.z); w.Write(q.w); }
        public static Quaternion Rot(BinaryReader r)
        {
            Quaternion q = new Quaternion(Number(r), Number(r), Number(r), Number(r));
            float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (m < 0.5f || m > 1.5f) throw new IOException("Invalid rotation.");
            return new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m);
        }
    }

    // One tracked hand as sent on the wire: world pose, grip (0 open .. 1 closed), trigger, and the item it holds (0 = none).
    public struct HandPose
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public float Grip, Trigger;
        public uint Held;

        public void Write(BinaryWriter w) { Wire.Vec(w, Position); Wire.Rot(w, Rotation); w.Write((byte)Mathf.RoundToInt(Mathf.Clamp01(Grip) * 255)); w.Write((byte)Mathf.RoundToInt(Mathf.Clamp01(Trigger) * 255)); w.Write(Held); }
        public static HandPose Read(BinaryReader r) { HandPose h; h.Position = Wire.Vec(r); h.Rotation = Wire.Rot(r); h.Grip = r.ReadByte() / 255f; h.Trigger = r.ReadByte() / 255f; h.Held = r.ReadUInt32(); return h; }
    }

    public struct PlayerPose
    {
        public string Scene;
        public Vector3 HeadPosition;
        public Quaternion HeadRotation;
        public HandPose Left, Right;
        // Spectator mode: whether this player is spectating, and where their spectator camera floats when it is out in
        // the world (not riding on their head).
        public bool Spectating, CameraShown;
        public Vector3 CameraPosition;
        public Quaternion CameraRotation;

        public void Write(BinaryWriter w)
        {
            Wire.Text(w, Scene); Wire.Vec(w, HeadPosition); Wire.Rot(w, HeadRotation); Left.Write(w); Right.Write(w);
            w.Write((byte)((Spectating ? 1 : 0) | (CameraShown ? 2 : 0)));
            if (CameraShown) { Wire.Vec(w, CameraPosition); Wire.Rot(w, CameraRotation); }
        }
        public static PlayerPose Read(BinaryReader r)
        {
            PlayerPose p = new PlayerPose();
            p.Scene = Wire.Text(r, 64); p.HeadPosition = Wire.Vec(r); p.HeadRotation = Wire.Rot(r); p.Left = HandPose.Read(r); p.Right = HandPose.Read(r);
            byte flags = r.ReadByte();
            p.Spectating = (flags & 1) != 0; p.CameraShown = (flags & 2) != 0;
            if (p.CameraShown) { p.CameraPosition = Wire.Vec(r); p.CameraRotation = Wire.Rot(r); }
            return p;
        }
    }
}
