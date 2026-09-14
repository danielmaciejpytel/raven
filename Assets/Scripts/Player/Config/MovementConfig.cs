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

        public float FppMouseSensitivity => _fppMouseSensitivity;
        public float MoveSpeed => _moveSpeed;
        public float TurnSmoothTime => _turnSmoothTime;
        public float GravityValue => _gravityValue;
        public float FppToTppDelayTime => _fppToTppDelayTime;
    }
}

