using Raven.Input;
using UnityEngine;

namespace Raven.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    public sealed class RavenHeadLook : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] private float _weight = 1f;
        [SerializeField, Range(10f, 85f)] private float _maxYaw = 70f;
        [SerializeField, Range(5f, 50f)] private float _maxUp = 35f;
        [SerializeField, Range(5f, 50f)] private float _maxDown = 30f;
        [SerializeField, Range(0.04f, 0.5f)] private float _smoothTime = 0.12f;
        [Tooltip("Fade to the animation pose when the camera looks behind the body.")]
        [SerializeField, Range(90f, 160f)] private float _behindFadeStart = 110f;

        [Tooltip("Seconds without player camera input in TPP before head tracking fades back to animation. Aim always tracks.")]
        [SerializeField, Min(0.1f)] private float _cameraIdleTimeout = 3f;

        [Tooltip("Duration in seconds of the smooth transition between idle animation and camera tracking.")]
        [SerializeField, Min(0.05f)] private float _idleBlendTime = 0.8f;

        private float _idleBlendProgress = 1f;
        private float _trackingBlend;
        private float _cameraIdleTime;
        private Animator _animator;
        private Transform _head;
        private Transform _view;
        private Transform _body;
        private Transform _aimTarget;
        private Quaternion _neutralHeadRotation;
        private InputManager _input;
        private float _yaw, _pitch, _blend;
        private float _yawVelocity, _pitchVelocity;
        private Quaternion _animatedHeadRotation;
        private bool _hasOffset;
        private static readonly int DashHash = Animator.StringToHash("Dash");

        public void Initialize(Transform view, Transform body, InputManager input = null, Transform aimTarget = null)
        {
            _animator = GetComponent<Animator>();
            _head = _animator.isHuman ? _animator.GetBoneTransform(HumanBodyBones.Head) : null;
            _view = view;
            _body = body;
            _input = input;
            _aimTarget = aimTarget;
            // Read the model's bind pose, never a potentially asymmetric animation frame.
            // The correction also supports skeletons whose head axes differ from the body.
            _neutralHeadRotation = Quaternion.identity;
            if (_head != null && _body != null)
            {
                foreach (var renderer in _animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (renderer.sharedMesh == null) continue;
                    int index = System.Array.IndexOf(renderer.bones, _head);
                    var bindPoses = renderer.sharedMesh.bindposes;
                    if (index < 0 || index >= bindPoses.Length) continue;
                    Quaternion neutralWorld = (renderer.transform.localToWorldMatrix * bindPoses[index].inverse).rotation;
                    _neutralHeadRotation = Quaternion.Inverse(_body.rotation) * neutralWorld;
                    break;
                }
            }
            ResetLook();
        }

        private void Update() => RestoreAnimatedPose();

        private void OnDisable()
        {
            RestoreAnimatedPose();
            ResetLook();
        }

        private void RestoreAnimatedPose()
        {
            // Remove last frame's offset even when Animator culls this character.
            if (_hasOffset && _head != null) _head.localRotation = _animatedHeadRotation;
            _hasOffset = false;
        }

        private void ResetLook()
        {
            _yaw = _pitch = _blend = _yawVelocity = _pitchVelocity = 0f;
            _cameraIdleTime = 0f;
            _trackingBlend = 0f;
            _idleBlendProgress = 1f;
        }

        private void LateUpdate()
        {
            // Solve an absolute look orientation after animation/IK; do not add the clip's yaw.
            if (_head == null || _view == null || _body == null) return;
            float dt = Time.deltaTime;
            bool gameplayActive = _input == null || _input.GameplayInputEnabled;
            // Use player input, not camera transforms: camera blends, movement and
            // automatic camera corrections must not keep the head tracking awake.
            if (gameplayActive)
            {
                if (_input != null && _input.GetMouseDelta().sqrMagnitude > 0.0001f)
                    _cameraIdleTime = 0f;
                else
                    _cameraIdleTime += dt;
            }
            Vector3 direction = _view.forward;
            if (_aimTarget != null && _input != null && _input.AimButtonHold())
            {
                Vector3 toTarget = _aimTarget.position - _head.position;
                if (toTarget.sqrMagnitude > 0.0001f) direction = toTarget.normalized;
            }
            Vector3 local = _body.InverseTransformDirection(direction).normalized;
            float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg;
            float behind = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(_behindFadeStart, 175f, Mathf.Abs(yaw)));
            bool wantsTracking = (_input != null && _input.AimButtonHold())
                || _cameraIdleTime < Mathf.Max(0.1f, _cameraIdleTimeout);
            _idleBlendProgress = Mathf.MoveTowards(_idleBlendProgress, wantsTracking ? 1f : 0f,
                dt / Mathf.Max(0.05f, _idleBlendTime));
            bool active = gameplayActive && !_animator.GetBool(DashHash);
            float weight = active ? _weight * behind : 0f;
            _trackingBlend = Mathf.Lerp(_trackingBlend, weight, 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, _smoothTime)));
            if (weight == 0f && _trackingBlend < 0.0001f) _trackingBlend = 0f;
            // Separate, timed ease-in/ease-out: fast camera following no longer
            // dictates how quickly the head hands control back to the idle clip.
            _blend = _trackingBlend * Mathf.SmoothStep(0f, 1f, _idleBlendProgress);
            // Linear angular coordinates cross through neutral rather than wrapping the neck.
            _yaw = Mathf.SmoothDamp(_yaw, Mathf.Clamp(yaw, -_maxYaw, _maxYaw), ref _yawVelocity, _smoothTime, Mathf.Infinity, dt);
            _pitch = Mathf.SmoothDamp(_pitch, Mathf.Clamp(pitch, -_maxDown, _maxUp), ref _pitchVelocity, _smoothTime, Mathf.Infinity, dt);
            _animatedHeadRotation = _head.localRotation;
            _hasOffset = true;
            Quaternion target = _body.rotation * Quaternion.Euler(-_pitch, _yaw, 0f) * _neutralHeadRotation;
            _head.rotation = Quaternion.Slerp(_head.rotation, target, _blend);
        }
    }
}
