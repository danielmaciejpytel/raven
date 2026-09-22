using System;
using Raven.Input;
using UnityEngine;

namespace Raven.Player
{
    // Restore before animation and reconnect weapons before gameplay can fire.
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class RavenWeaponHolster : MonoBehaviour
    {
        [Serializable]
        public sealed class WeaponSlot
        {
            public Transform weapon;
            public Transform holster;
            public RavenHolsterMotion motion;
            [NonSerialized] public Transform socket, upper, lower, hand;
            [NonSerialized] public Vector3 localPosition, handOffset, startPosition;
            [NonSerialized] public Quaternion localRotation, handRotation, startRotation;
            [NonSerialized] public Quaternion[] animated = new Quaternion[3], release = new Quaternion[3];
            [NonSerialized] public bool applied, stored, reaching, returning, crossBody;
            [NonSerialized] public float timer;
            [NonSerialized] public float attachmentPositionError, attachmentRotationError;
            [NonSerialized] public Vector3 releasePosition;
            [NonSerialized] public Quaternion releaseRotation, carryWristLocal;
            [NonSerialized] public Pose startWeapon;
            [NonSerialized] public Vector3 startElbow;
        }

        [SerializeField] private WeaponSlot _right = new WeaponSlot();
        [SerializeField] private WeaponSlot _left = new WeaponSlot();
        [SerializeField, Min(0f)] private float _holsterDelay = 3f;
        [SerializeField, Range(0.2f, 2f)] private float _holsterDuration = 0.7f;
        [SerializeField, Range(0.1f, 1f)] private float _handReleaseTime = 0.3f;
        [SerializeField, Range(0f, 0.2f)] private float _reachArc = 0.06f;
        [Header("Right hand cross-body holster")]
        [SerializeField, Range(1f, 2f)] private float _rightCrossBodyTimeScale = 1.25f;
        [SerializeField, Range(0.05f, 0.3f)] private float _rightApproachClearance = 0.16f;
        [SerializeField, Range(0.02f, 0.25f)] private float _rightInsertHeight = 0.05f;
        private InputManager _input;
        private Animator _animator;
        private Transform _hips, _body;
        private bool _wasAiming, _pending;
        private float _delay;

        public void Initialize(InputManager input, Transform body)
        {
            _input = input;
            _body = body;
        }

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            _hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
            Setup(_right, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);
            Setup(_left, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand);
        }

        private void Setup(WeaponSlot slot, HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones hand)
        {
            slot.upper = _animator.GetBoneTransform(upper);
            slot.lower = _animator.GetBoneTransform(lower);
            slot.hand = _animator.GetBoneTransform(hand);
            if (slot.weapon == null || slot.hand == null) return;
            slot.socket = slot.weapon.parent;
            slot.localPosition = slot.weapon.localPosition;
            slot.localRotation = slot.weapon.localRotation;
            slot.handOffset = Quaternion.Inverse(slot.hand.rotation) * (slot.weapon.position - slot.hand.position);
            slot.handRotation = Quaternion.Inverse(slot.hand.rotation) * slot.weapon.rotation;
        }

        private static void Restore(WeaponSlot s)
        {
            if (!s.applied) return;
            s.upper.localRotation = s.animated[0];
            s.lower.localRotation = s.animated[1];
            s.hand.localRotation = s.animated[2];
            s.applied = false;
        }

        private void Update()
        {
            Restore(_right); Restore(_left);
            if (_input == null || !_input.GameplayInputEnabled) return;
            bool aim = _input.AimButtonHold();
            if (aim)
            {
                Draw(_right); Draw(_left);
                _pending = false;
                _delay = 0f;
            }
            else if (_wasAiming) { _pending = true; _delay = 0f; }
            _wasAiming = aim;
            if (_pending && !aim) _delay += Time.deltaTime;
        }

        private static void Draw(WeaponSlot s)
        {
            if (s.weapon == null || s.socket == null) return;
            if (s.stored)
            {
                s.weapon.SetParent(s.socket, false);
                s.weapon.SetLocalPositionAndRotation(s.localPosition, s.localRotation);
            }
            if (s.reaching) { s.returning = true; s.timer = 0f; }
            s.stored = s.reaching = false;
        }

        // Called after RavenPistolGrip: the final wrist pose must carry the gun to the slot.
        public void EvaluateAfterGrip()
        {
            if (_input == null || _hips == null || _body == null) return;
            bool gameplay = _input.GameplayInputEnabled;
            bool canStart = gameplay && _pending && _delay >= _holsterDelay && !_animator.GetBool("Dash") && _animator.GetBool("Grounded");
            Evaluate(_right, 1f, canStart, gameplay ? Time.deltaTime : 0f);
            Evaluate(_left, -1f, canStart, gameplay ? Time.deltaTime : 0f);
        }

        private void Evaluate(WeaponSlot s, float side, bool canStart, float deltaTime)
        {
            if (s.weapon == null || s.holster == null || !s.weapon.gameObject.activeInHierarchy) return;
            if (canStart && !s.stored && !s.reaching && !s.returning)
            {
                s.reaching = true;
                s.timer = 0f;
                s.startPosition = _hips.InverseTransformPoint(s.hand.position);
                s.startRotation = Quaternion.Inverse(_hips.rotation) * s.hand.rotation;
                s.carryWristLocal = s.hand.localRotation;
                s.startWeapon = new Pose(_hips.InverseTransformPoint(s.weapon.position), Quaternion.Inverse(_hips.rotation) * s.weapon.rotation);
                s.startElbow = _hips.InverseTransformPoint(s.lower.position);
                Quaternion destinationWrist = s.holster.rotation * Quaternion.Inverse(s.handRotation);
                Vector3 destinationHand = s.holster.position - destinationWrist * s.handOffset;
                // The weapon origin is offset from its grip and can lie across the
                // centreline. Classify the reach by the hand, never the gun pivot.
                s.crossBody = Vector3.Dot(destinationHand - _hips.position, _body.right) * side < 0f;
            }
            if (!s.reaching && !s.returning) return;
            s.animated[0] = s.upper.localRotation;
            s.animated[1] = s.lower.localRotation;
            s.animated[2] = s.hand.localRotation;
            s.applied = true;
            s.timer += deltaTime;
            if (s.reaching)
            {
                bool crossBody = s.crossBody;
                float duration = _holsterDuration * (side > 0f && crossBody ? _rightCrossBodyTimeScale : 1f);
                bool authored = s.motion != null && s.motion.IsValid;
                if (authored) duration = s.motion.duration;
                float t = Mathf.Clamp01(s.timer / Mathf.Max(0.01f, duration));
                float blend = Mathf.SmoothStep(0f, 1f, t);
                Quaternion rotation = s.holster.rotation * Quaternion.Inverse(s.handRotation);
                Vector3 target = s.holster.position - rotation * s.handOffset;
                Vector3 position = Vector3.Lerp(_hips.TransformPoint(s.startPosition), target, blend)
                    + (_body.right * side + _body.up + (crossBody ? _body.forward * 2f : Vector3.zero))
                    * (_reachArc * Mathf.Sin(Mathf.PI * t));
                Quaternion handRotation = Quaternion.Slerp(_hips.rotation * s.startRotation, rotation, blend);
                if (side > 0f && crossBody)
                {
                    // A low, continuous sweep: reach in front of the belt before
                    // crossing to the opposite grip. Do not lift to the end pose first.
                    Vector3 start = _hips.TransformPoint(s.startPosition);
                    Vector3 approach = target + _body.forward * _rightApproachClearance
                        + _body.up * _rightInsertHeight;
                    Vector3 belt = _hips.position + _body.forward * (_rightApproachClearance + 0.16f);
                    belt += _body.up * Vector3.Dot(start - _hips.position, _body.up);
                    float u = blend;
                    float v = 1f - u;
                    position = v*v*v*start + 3f*v*v*u*belt + 3f*v*u*u*approach + u*u*u*target;
                }
                Vector3 elbowPole = Vector3.Lerp(s.lower.position,
                    s.upper.position + _body.right * side * (crossBody ? 0.15f : 0.4f)
                    + _body.forward * (crossBody ? 0.4f : 0.15f)
                    - _body.up * (side > 0f && crossBody ? 0.25f : 0f), blend);
                if (side > 0f && crossBody)
                {
                    Quaternion forearmRotation = handRotation * Quaternion.Inverse(s.carryWristLocal);
                    Vector3 forearmDirection = forearmRotation * s.hand.localPosition.normalized;
                    elbowPole = position - forearmDirection * Vector3.Distance(s.lower.position, s.hand.position);
                }
                if (authored)
                {
                    Pose end = new Pose(_hips.InverseTransformPoint(s.holster.position), Quaternion.Inverse(_hips.rotation) * s.holster.rotation);
                    var pose = s.motion.Sample(t, s.startWeapon, end, s.startElbow);
                    handRotation = _hips.rotation * pose.Pose.rotation * Quaternion.Inverse(s.handRotation);
                    position = _hips.TransformPoint(pose.position) - handRotation * s.handOffset;
                    elbowPole = _hips.TransformPoint(pose.elbow);
                }
                SolveArm(s, position, elbowPole);
                if (authored || side > 0f && crossBody)
                {
                    // Share the grip's axial rotation with the forearm rather than
                    // forcing the entire difference into the wrist joint.
                    Vector3 axis = (s.hand.position - s.lower.position).normalized;
                    Quaternion delta = handRotation * Quaternion.Inverse(s.carryWristLocal) * Quaternion.Inverse(s.lower.rotation);
                    Vector3 vector = Vector3.Project(new Vector3(delta.x, delta.y, delta.z), axis);
                    Quaternion twist = new Quaternion(vector.x, vector.y, vector.z, delta.w);
                    if (Quaternion.Dot(twist, twist) > 0.00001f)
                        s.lower.rotation = twist.normalized * s.lower.rotation;
                }
                // Interpolate in the moving body frame, not the forearm frame:
                // a locked local wrist points the barrel up as the elbow bends.
                s.hand.rotation = handRotation;
                s.release[0] = s.upper.localRotation;
                s.release[1] = s.lower.localRotation;
                s.release[2] = s.hand.localRotation;
                if (t >= 1f)
                {
                    s.attachmentPositionError = Vector3.Distance(s.weapon.position, s.holster.position);
                    s.attachmentRotationError = Quaternion.Angle(s.weapon.rotation, s.holster.rotation);
                    s.releasePosition = _hips.InverseTransformPoint(s.hand.position);
                    s.releaseRotation = Quaternion.Inverse(_hips.rotation) * s.hand.rotation;
                    s.weapon.SetParent(s.holster, false);
                    s.weapon.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                    s.stored = true;
                    s.reaching = false;
                    s.returning = true;
                    s.timer = 0f;
                }
            }
            else
            {
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(s.timer / Mathf.Max(0.01f, _handReleaseTime)));
                if (side > 0f && s.stored && s.crossBody && (s.motion == null || !s.motion.IsValid))
                {
                    Quaternion wrist = s.hand.rotation;
                    Vector3 position = Vector3.Lerp(_hips.TransformPoint(s.releasePosition), s.hand.position, t)
                        + _body.forward * (_rightApproachClearance * Mathf.Sin(Mathf.PI * t));
                    // Release outwards before recovering to idle, rather than blending
                    // joint rotations through the belt and the stored pistol.
                    SolveArm(s, position, Vector3.Lerp(s.upper.position + _body.forward * 0.4f
                        + _body.right * 0.15f - _body.up * 0.25f, s.lower.position, t));
                    s.hand.rotation = Quaternion.Slerp(_hips.rotation * s.releaseRotation, wrist, t);
                    if (t >= 1f)
                    {
                        s.upper.localRotation = s.animated[0];
                        s.lower.localRotation = s.animated[1];
                        s.hand.localRotation = s.animated[2];
                        s.returning = false;
                    }
                    return;
                }
                s.upper.localRotation = Quaternion.Slerp(s.release[0], s.animated[0], t);
                s.lower.localRotation = Quaternion.Slerp(s.release[1], s.animated[1], t);
                s.hand.localRotation = Quaternion.Slerp(s.release[2], s.animated[2], t);
                if (t >= 1f) s.returning = false;
            }
        }

        private static void SolveArm(WeaponSlot s, Vector3 target, Vector3 pole)
        {
            Vector3 root = s.upper.position;
            float a = Vector3.Distance(root, s.lower.position), b = Vector3.Distance(s.lower.position, s.hand.position);
            Vector3 direction = (target - root).normalized;
            float distance = Mathf.Clamp(Vector3.Distance(root, target), Mathf.Abs(a - b) + 0.001f, a + b - 0.001f);
            float along = (a * a - b * b + distance * distance) / (2f * distance);
            Vector3 bend = Vector3.ProjectOnPlane(pole - root, direction).normalized;
            if (bend.sqrMagnitude < 0.001f) bend = Vector3.ProjectOnPlane(s.upper.up, direction).normalized;
            Vector3 elbow = root + direction * along + bend * Mathf.Sqrt(Mathf.Max(0f, a * a - along * along));
            s.upper.rotation = Quaternion.FromToRotation(s.lower.position - root, elbow - root) * s.upper.rotation;
            s.lower.rotation = Quaternion.FromToRotation(s.hand.position - s.lower.position, target - s.lower.position) * s.lower.rotation;
        }

        public static void PreviewWeaponPose(WeaponSlot slot, Pose weaponPose, Vector3 elbow)
        {
            Quaternion wrist = weaponPose.rotation * Quaternion.Inverse(slot.handRotation);
            SolveArm(slot, weaponPose.position - wrist * slot.handOffset, elbow);
            Vector3 axis = (slot.hand.position - slot.lower.position).normalized;
            Quaternion delta = wrist * Quaternion.Inverse(slot.carryWristLocal) * Quaternion.Inverse(slot.lower.rotation);
            Vector3 vector = Vector3.Project(new Vector3(delta.x, delta.y, delta.z), axis);
            Quaternion twist = new Quaternion(vector.x, vector.y, vector.z, delta.w);
            if (Quaternion.Dot(twist, twist) > 0.00001f) slot.lower.rotation = twist.normalized * slot.lower.rotation;
            slot.hand.rotation = wrist;
        }

        private void OnDisable()
        {
            Restore(_right); Restore(_left);
            Draw(_right); Draw(_left);
            _right.returning = _left.returning = false;
            _pending = _wasAiming = false;
        }
    }
}
