using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Raven.Config
{
    [CreateAssetMenu(fileName = "Movement Config", menuName = "Configs/Player/Movement Config", order = 0)]
    public class MovementConfig : ScriptableObject
    {
        [SerializeField, Range(1, 50)] private float _moveSpeed = 10f;
        [Space]
        [SerializeField, Range(0.1f, 0.5f)] private float _turnSmoothTime = 0.1f;
        [Space]
        [SerializeField, Range(-30, -1)] private float _gravityValue = -9.81f;
        [Space]
        [SerializeField, Range(0.1f, 1f)] private float _fppToTppDelayTime;
        [Space]
        [SerializeField, Range(0.1f, 20f)] private float _fppMouseSensitivity;

        [Header("Ground Contact")]
        [Tooltip("Maximum downward correction in metres while following walkable ground. Does not apply during dash or free fall.")]
        [SerializeField, Range(0f, 0.5f)] private float _groundSnapDistance = 0.3f;
        [Tooltip("Continuous time without ground contact before the Animator can enter Falling.")]
        [SerializeField, Range(0f, 0.3f)] private float _fallingDelay = 0.12f;

        [Header("Animation")]
        [SerializeField, Range(0.01f, 0.5f)] private float _animationSpeedDampTime = 0.08f;
        [SerializeField, Range(0.01f, 0.5f)] private float _animationDirectionDampTime = 0.1f;
        [SerializeField, Range(0.01f, 0.5f)] private float _animationTurnDampTime = 0.08f;
        [SerializeField, Range(0.5f, 1f)] private float _locomotionMinPlaybackRate = 0.75f;
        [SerializeField, Range(0f, 0.1f)] private float _recoilLocalKickDistance = 0.035f;
        [SerializeField, Range(0f, 10f)] private float _recoilLocalKickAngle = 2.5f;
        [SerializeField, Range(0.03f, 0.4f)] private float _recoilRecoverTime = 0.12f;

        [Header("ShootCamera - Aim Limits")]
        [Tooltip("Maximum aiming angle above the horizon, in degrees.")]
        [SerializeField, Range(0f, 89f)] private float _aimMaxUpAngle = 25f;
        [Tooltip("Maximum aiming angle below the horizon, in degrees.")]
        [SerializeField, Range(0f, 89f)] private float _aimMaxDownAngle = 20f;

        [Header("Start kamery za Raven")]
        [Tooltip("Kat wzgledem plecow Raven. 0 = dokladnie za postacia.")]
        [SerializeField, Range(-180f, 180f)] private float _startCameraYawOffset = 0f;
        [Tooltip("Wysokosc na orbicie: 0 = dol, 0.5 = srodek, 1 = gora.")]
        [SerializeField, Range(0f, 1f)] private float _startCameraHeight = 0.75f;

        public float StartCameraYawOffset => _startCameraYawOffset;
        public float StartCameraHeight => Mathf.Clamp01(_startCameraHeight);

        public float AimMaxUpAngle => Mathf.Clamp(_aimMaxUpAngle, 0f, 89f);
        public float AimMaxDownAngle => Mathf.Clamp(_aimMaxDownAngle, 0f, 89f);

        public float GroundSnapDistance => _groundSnapDistance;
        public float FallingDelay => _fallingDelay;
        public float FppMouseSensitivity => _fppMouseSensitivity;
        public float AnimationSpeedDampTime => _animationSpeedDampTime;
        public float AnimationDirectionDampTime => _animationDirectionDampTime;
        public float AnimationTurnDampTime => _animationTurnDampTime;
        public float LocomotionMinPlaybackRate => _locomotionMinPlaybackRate;
        public float RecoilLocalKickDistance => _recoilLocalKickDistance;
        public float RecoilLocalKickAngle => _recoilLocalKickAngle;
        public float RecoilRecoverTime => _recoilRecoverTime;
        public float MoveSpeed => _moveSpeed;
        public float TurnSmoothTime => _turnSmoothTime;
        public float GravityValue => _gravityValue;
        public float FppToTppDelayTime => _fppToTppDelayTime;
    }
}

