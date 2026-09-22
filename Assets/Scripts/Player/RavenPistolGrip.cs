using System;
using UnityEngine;

namespace Raven.Player
{
    // Apply after the arm pose return and rig evaluation; never rotate the forearm.
    [DefaultExecutionOrder(10000)]
    [DisallowMultipleComponent]
    public sealed class RavenPistolGrip : MonoBehaviour
    {
        [Serializable]
        public sealed class HandGrip
        {
            public Transform weapon;
            [Range(0f, 1f)] public float weight = 1f;
            public Vector3 fingersCurl = new Vector3(45f, 65f, 45f);
            public Vector3 indexCurl = new Vector3(15f, 35f, 25f);
            public Vector3 thumbCurl = new Vector3(20f, 30f, 20f);
            // Wrist, followed by thumb/index/middle/ring/little finger joints.
            public Transform[] bones = new Transform[16];
            public Quaternion[] neutralRotations = new Quaternion[16];
            [NonSerialized] public Quaternion[] animatedRotations;
            [NonSerialized] public bool applied;
            [NonSerialized] public float blend;
        }

        [SerializeField] private HandGrip _right = new HandGrip();
        [SerializeField] private HandGrip _left = new HandGrip();
        [SerializeField, Range(0.03f, 0.5f)] private float _blendTime = 0.12f;

        private void Update() { Restore(_right); Restore(_left); }
        private void LateUpdate() { Apply(_right); Apply(_left); }
        private void OnDisable()
        {
            Restore(_right); Restore(_left);
            _right.blend = _left.blend = 0f;
        }

        private static void Restore(HandGrip grip)
        {
            if (!grip.applied) return;
            for (int i = 0; i < grip.bones.Length; i++)
                if (grip.bones[i] != null) grip.bones[i].localRotation = grip.animatedRotations[i];
            grip.applied = false;
        }

        private void Apply(HandGrip grip)
        {
            if (grip.bones == null || grip.neutralRotations == null || grip.bones.Length != grip.neutralRotations.Length) return;
            if (grip.animatedRotations == null || grip.animatedRotations.Length != grip.bones.Length)
                grip.animatedRotations = new Quaternion[grip.bones.Length];
            float target = grip.weapon != null && grip.weapon.gameObject.activeInHierarchy ? grip.weight : 0f;
            grip.blend = Mathf.MoveTowards(grip.blend, target, Time.deltaTime / Mathf.Max(0.01f, _blendTime));
            if (grip.blend <= 0f) return;
            for (int i = 0; i < grip.bones.Length; i++)
            {
                var bone = grip.bones[i];
                if (bone == null) continue;
                grip.animatedRotations[i] = bone.localRotation;
                Quaternion pose = grip.neutralRotations[i];
                if (i > 0)
                {
                    int finger = (i - 1) / 3;
                    int joint = (i - 1) % 3;
                    Vector3 curl = finger == 0 ? grip.thumbCurl : finger == 1 ? grip.indexCurl : grip.fingersCurl;
                    pose *= Quaternion.AngleAxis(curl[joint], Vector3.right);
                }
                bone.localRotation = Quaternion.Slerp(bone.localRotation, pose, Mathf.SmoothStep(0f, 1f, grip.blend));
            }
            grip.applied = true;
        }
    }
}
