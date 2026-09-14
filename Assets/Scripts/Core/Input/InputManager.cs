using UnityEngine;
using UnityEngine.InputSystem;

namespace Raven.Input
{
    public class InputManager : MonoBehaviour
    {
        private Controls _controls;

        public bool CanInput;

        public bool GameplayInputEnabled => CanInput && isActiveAndEnabled && Time.timeScale > 0f;
        public bool IsPointerLook => _controls?.Player.CameraLook.activeControl?.device is Pointer;

        private void Awake()
        {
            if (_controls == null) _controls = new Controls();
        }

        private void OnEnable()
        {
            _controls.Enable();
        }

        private void OnDisable()
        {
            _controls?.Disable();
        }

        private void OnDestroy()
        {
            _controls?.Dispose();
            _controls = null;
        }

        public bool EscTrigerred()
        {
            return _controls.Player.ESC.triggered;
        }

        public Vector2 GetMovementAxis()
        {
            if (!GameplayInputEnabled)
            {
                return Vector2.zero;
            }

            return _controls.Player.Movement.ReadValue<Vector2>();
        }

        public Vector2 GetMouseDelta()
        {
            if (!GameplayInputEnabled)
            {
                return Vector2.zero;
            }

            return _controls.Player.CameraLook.ReadValue<Vector2>();
        }

        public bool DashButtonPressed()
        {
            if (!GameplayInputEnabled)
            {
                return false;
            }

            return _controls.Player.Dash.triggered;
        }

        public bool AimButtonHold()
        {
            if (!GameplayInputEnabled)
            {
                return false;
            }

            return _controls.Player.Aim.IsPressed();
        }

        public bool ActiveStateButtonPressed()
        {
            if (!GameplayInputEnabled)
            {
                return false;
            }

            return _controls.Player.ActiveState.triggered;
        }

        public bool DashButtonHold()
        {
            if (!GameplayInputEnabled)
            {
                return false;
            }

            return _controls.Player.DashHold.IsPressed();
        }

        public bool ShootButtonPressed()
        {
            if (!GameplayInputEnabled)
            {
                return false;
            }

            return _controls.Player.Shoot.triggered;
        }

        public bool TakeButtonPressed()
        {
            if (!GameplayInputEnabled)
            {
                return false;
            }

            return _controls.Player.Take.triggered;
        }
    }
}

