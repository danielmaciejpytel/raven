using Raven.Config;
using Raven.Input;
using UnityEngine;
using UnityEngine.Animations.Rigging;

namespace Raven.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    public sealed class RavenAimTorso : MonoBehaviour
    {
        [SerializeField, Range(0f, 20f)] private float _maxUp = 8f;
        [SerializeField, Range(0f, 20f)] private float _maxDown = 10f;
        [SerializeField, Range(0f, 1f)] private float _chestShare = 0.65f;
        [SerializeField, Range(0.04f, 0.5f)] private float _smoothTime = 0.16f;

        [SerializeField] private OverrideTransform _spineBend;
        [SerializeField] private OverrideTransform _chestBend;

        private Animator _animator;
        private Transform _view, _body, _spine, _chest;
        private InputManager _input;
        private MovementConfig _config;
        private float _pitch, _pitchVelocity;
        private static readonly int DashHash = Animator.StringToHash("Dash");

        public void Initialize(Transform view, Transform body, InputManager input, MovementConfig config)
        {
            _animator = GetComponent<Animator>();
            _view = view;
            _body = body;
            _input = input;
            _config = config;
            if (!_animator.isHuman) return;
            _spine = _animator.GetBoneTransform(HumanBodyBones.Spine);
            _chest = _animator.GetBoneTransform(HumanBodyBones.Chest);
        }

        private void OnDisable()
        {
            _pitch = _pitchVelocity = 0f;
            SetBend(_spineBend, _spine, 0f);
            SetBend(_chestBend, _chest, 0f);
        }

        private void Update()
        {
            if (_view == null || _body == null || _input == null) return;
            float target = 0f;
            if (_input.AimButtonHold() && !_animator.GetBool(DashHash))
            {
                float viewPitch = Mathf.Asin(Mathf.Clamp(_body.InverseTransformDirection(_view.forward).y, -1f, 1f)) * Mathf.Rad2Deg;
                float limit = viewPitch >= 0f ? (_config != null ? _config.AimMaxUpAngle : 25f)
                    : (_config != null ? _config.AimMaxDownAngle : 20f);
                target = Mathf.Clamp(viewPitch / Mathf.Max(1f, limit), -1f, 1f)
                    * (viewPitch >= 0f ? _maxUp : _maxDown);
            }
            _pitch = Mathf.SmoothDamp(_pitch, target, ref _pitchVelocity, _smoothTime, Mathf.Infinity, Time.deltaTime);
            // The torso rig is first in RigBuilder.layers, ahead of both hand rigs.
            // Pivot offsets preserve the controller pose and leave hips/legs untouched.
            SetBend(_spineBend, _spine, _pitch * (1f - _chestShare));
            SetBend(_chestBend, _chest, _pitch * _chestShare);
        }

        private void SetBend(OverrideTransform constraint, Transform bone, float pitch)
        {
            if (constraint == null) return;
            var data = constraint.data;
            data.rotation = bone != null && _body != null
                ? Quaternion.AngleAxis(-pitch, bone.InverseTransformDirection(_body.right)).eulerAngles
                : Vector3.zero;
            constraint.data = data;
        }
    }
}
