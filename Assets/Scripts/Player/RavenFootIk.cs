using UnityEngine;

namespace Raven.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    public sealed class RavenFootIk : MonoBehaviour
    {
        [SerializeField] private LayerMask _groundMask = ~0;
        [SerializeField, Range(0.05f, 0.8f)] private float _rayStartHeight = 0.3f;
        [SerializeField, Range(0.1f, 1.5f)] private float _rayDistance = 0.75f;
        [SerializeField, Range(0f, 0.12f)] private float _soleOffset = 0.025f;
        [SerializeField, Range(1f, 30f)] private float _weightSpeed = 12f;
        [SerializeField, Range(0f, 1f)] private float _pelvisWeight = 0.55f;
        [SerializeField, Range(0f, 0.25f)] private float _maxPelvisOffset = 0.12f;
        [SerializeField, Range(0.02f, 0.3f)] private float _swingReleaseHeight = 0.16f;
        [SerializeField, Range(0.05f, 0.4f)] private float _maxFootCorrection = 0.2f;

        private RaycastHit[] _hits = new RaycastHit[8];
        private Animator _animator;
        private Transform _playerRoot;
        private CharacterController _characterController;
        private float _weight;
        private float _pelvisOffset;
        private static readonly int DashHash = Animator.StringToHash("Dash");

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            _characterController = GetComponentInParent<CharacterController>();
            _playerRoot = _characterController != null ? _characterController.transform : transform.root;
        }

        private void OnDisable()
        {
            _weight = 0f;
            _pelvisOffset = 0f;
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (layerIndex != 0 || !_animator.isHuman) return;

            // A dash can end in gameplay before its animation has blended out.
            bool blocked = _characterController == null || !_characterController.isGrounded ||
                _animator.GetBool(DashHash) || BlocksIk(_animator.GetCurrentAnimatorStateInfo(0)) ||
                (_animator.IsInTransition(0) && BlocksIk(_animator.GetNextAnimatorStateInfo(0)));
            _weight = blocked ? 0f : Mathf.MoveTowards(_weight, 1f, _weightSpeed * Time.deltaTime);
            if (blocked)
            {
                _pelvisOffset = 0f;
                ApplyFoot(AvatarIKGoal.LeftFoot, default, default, 0f);
                ApplyFoot(AvatarIKGoal.RightFoot, default, default, 0f);
                return;
            }

            // Read the animated IK goals before changing the body or either leg.
            Vector3 left = _animator.GetIKPosition(AvatarIKGoal.LeftFoot);
            Vector3 right = _animator.GetIKPosition(AvatarIKGoal.RightFoot);
            float leftContact = GetFootPose(AvatarIKGoal.LeftFoot, _animator.leftFeetBottomHeight, out Vector3 leftTarget, out Quaternion leftRotation);
            float rightContact = GetFootPose(AvatarIKGoal.RightFoot, _animator.rightFeetBottomHeight, out Vector3 rightTarget, out Quaternion rightRotation);
            float targetPelvis = Mathf.Min(0f, Mathf.Min((leftTarget.y - left.y) * leftContact, (rightTarget.y - right.y) * rightContact));
            targetPelvis = Mathf.Max(-_maxPelvisOffset, targetPelvis) * _pelvisWeight * _weight;
            _pelvisOffset = Mathf.Lerp(_pelvisOffset, targetPelvis, 1f - Mathf.Exp(-_weightSpeed * Time.deltaTime));
            _animator.bodyPosition += Vector3.up * _pelvisOffset;
            ApplyFoot(AvatarIKGoal.LeftFoot, leftTarget, leftRotation, leftContact * _weight);
            ApplyFoot(AvatarIKGoal.RightFoot, rightTarget, rightRotation, rightContact * _weight);
        }

        private static bool BlocksIk(AnimatorStateInfo state)
        {
            return state.IsName("Base Layer.Dash") || state.IsName("Base Layer.Falling");
        }

        private void ApplyFoot(AvatarIKGoal goal, Vector3 position, Quaternion rotation, float weight)
        {
            _animator.SetIKPositionWeight(goal, weight);
            _animator.SetIKRotationWeight(goal, weight);
            if (weight <= 0f) return;
            _animator.SetIKPosition(goal, position);
            _animator.SetIKRotation(goal, rotation);
        }

        private float GetFootPose(AvatarIKGoal goal, float bottomHeight, out Vector3 position, out Quaternion rotation)
        {
            position = _animator.GetIKPosition(goal);
            rotation = _animator.GetIKRotation(goal);
            Vector3 sole = position - rotation * Vector3.up * bottomHeight;
            Vector3 origin = sole + Vector3.up * _rayStartHeight;
            int count;
            // Never silently truncate the query in dense collider clusters.
            while ((count = Physics.RaycastNonAlloc(origin, Vector3.down, _hits, _rayStartHeight + _rayDistance,
                       _groundMask, QueryTriggerInteraction.Ignore)) == _hits.Length)
                System.Array.Resize(ref _hits, _hits.Length * 2);

            float bestDistance = float.PositiveInfinity;
            RaycastHit best = default;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _hits[i];
                if (hit.collider == null || hit.transform == _playerRoot || hit.transform.IsChildOf(_playerRoot) ||
                    hit.normal.y < Mathf.Cos(_characterController.slopeLimit * Mathf.Deg2Rad) || hit.distance >= bestDistance) continue;
                bestDistance = hit.distance;
                best = hit;
            }
            if (float.IsPositiveInfinity(bestDistance)) return 0f;

            // Release the airborne foot instead of pinning both feet throughout the stride.
            float clearance = sole.y - best.point.y;
            float contact = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(_soleOffset, _swingReleaseHeight, clearance));
            Quaternion targetRotation = Quaternion.FromToRotation(Vector3.up, best.normal) * rotation;
            Vector3 target = best.point + best.normal * _soleOffset + targetRotation * Vector3.up * bottomHeight;
            if (Mathf.Abs(target.y - position.y) > _maxFootCorrection) return 0f;
            position = target;
            rotation = targetRotation;
            return contact;
        }
    }
}
