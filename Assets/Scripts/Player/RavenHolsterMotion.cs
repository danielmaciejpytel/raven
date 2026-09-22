using System;
using UnityEngine;

namespace Raven.Player
{
    [CreateAssetMenu(menuName = "Raven/Holster Motion")]
    public sealed class RavenHolsterMotion : ScriptableObject
    {
        [Serializable]
        public struct Key
        {
            public Vector3 position;
            public Vector3 rotation;
            public Vector3 elbow;
            public Pose Pose => new Pose(position, Quaternion.Euler(rotation));
        }

        [Range(0.2f, 3f)] public float duration = 1f;
        public AnimationClip previewClip;
        public float previewTime;
        public Key[] keys = Array.Empty<Key>();
        public bool IsValid => keys != null && keys.Length >= 3;

        public Key Sample(float time, Pose start, Pose end, Vector3 startElbow)
        {
            if (!IsValid) return new Key { position = start.position, rotation = start.rotation.eulerAngles, elbow = startElbow };
            float cursor = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(time)) * (keys.Length - 1);
            int segment = Mathf.Min(Mathf.FloorToInt(cursor), keys.Length - 2);
            float t = cursor - segment;
            Key a = Resolve(segment - 1, start, end, startElbow);
            Key b = Resolve(segment, start, end, startElbow);
            Key c = Resolve(segment + 1, start, end, startElbow);
            Key d = Resolve(segment + 2, start, end, startElbow);
            return new Key
            {
                position = Cubic(a.position, b.position, c.position, d.position, t),
                rotation = Rotation(a.Pose.rotation, b.Pose.rotation, c.Pose.rotation, d.Pose.rotation, t).eulerAngles,
                elbow = Cubic(a.elbow, b.elbow, c.elbow, d.elbow, t)
            };
        }

        public float KeyTime(int index)
        {
            float target = index / (float)(keys.Length - 1);
            float lo = 0f, hi = 1f;
            for (int i = 0; i < 20; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (Mathf.SmoothStep(0f, 1f, mid) < target) lo = mid; else hi = mid;
            }
            return (lo + hi) * 0.5f;
        }

        private Key Resolve(int index, Pose start, Pose end, Vector3 startElbow)
        {
            index = Mathf.Clamp(index, 0, keys.Length - 1);
            Key key = keys[index];
            if (index == 0) { key.position = start.position; key.rotation = start.rotation.eulerAngles; key.elbow = startElbow; }
            if (index == keys.Length - 1) { key.position = end.position; key.rotation = end.rotation.eulerAngles; }
            return key;
        }

        private static Vector3 Cubic(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t) =>
            0.5f * ((2f*b) + (-a+c)*t + (2f*a-5f*b+4f*c-d)*t*t + (-a+3f*b-3f*c+d)*t*t*t);

        private static Quaternion Nearest(Quaternion reference, Quaternion q) => Quaternion.Dot(reference, q) < 0f
            ? new Quaternion(-q.x, -q.y, -q.z, -q.w) : q;

        private static Vector3 Log(Quaternion q)
        {
            var v = new Vector3(q.x, q.y, q.z);
            float length = v.magnitude;
            return length < 0.00001f ? Vector3.zero : v * (Mathf.Atan2(length, q.w) / length);
        }

        private static Quaternion Exp(Vector3 v)
        {
            float angle = v.magnitude;
            if (angle < 0.00001f) return Quaternion.identity;
            Vector3 axis = v * (Mathf.Sin(angle) / angle);
            return new Quaternion(axis.x, axis.y, axis.z, Mathf.Cos(angle));
        }

        private static Quaternion Tangent(Quaternion previous, Quaternion current, Quaternion next) =>
            current * Exp(-0.25f * (Log(Quaternion.Inverse(current) * previous) + Log(Quaternion.Inverse(current) * next)));

        private static Quaternion Rotation(Quaternion a, Quaternion b, Quaternion c, Quaternion d, float t)
        {
            a = Nearest(b, a); c = Nearest(b, c); d = Nearest(c, d);
            return Quaternion.Slerp(Quaternion.Slerp(b, c, t),
                Quaternion.Slerp(Tangent(a, b, c), Tangent(b, c, d), t), 2f*t*(1f-t));
        }
    }
}
