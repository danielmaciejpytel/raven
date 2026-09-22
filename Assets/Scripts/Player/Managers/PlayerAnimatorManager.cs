using System;
using Raven.Manager;
using Raven.Player;
using UnityEngine;
using Zenject;

public class PlayerAnimatorManager : IDisposable, ITickable
{
    private Animator _animator;
    private PlayerMovementManager _playerMovementManager;
    private CameraManager _cameraManager;
    private PlayerStatesManager _playerStatesManager;
    private PlayerRigManager _playerRigManager;
    private float _previousYaw;
    private float _turnRate;
    private int _teleportVersion;
    private readonly int _turnLayer;
    private float _turnLayerWeight;
    private static readonly int StartTurningHash = Animator.StringToHash("StartTurning");
    private static readonly int StationaryTurnProgressHash = Animator.StringToHash("StationaryTurnProgress");
    private static readonly int StationaryTurnAngleHash = Animator.StringToHash("StationaryTurnAngle");
    private static readonly int RunningPivotHash = Animator.StringToHash("RunningPivot");
    private static readonly int RunningPivotAngleHash = Animator.StringToHash("RunningPivotAngle");
    private static readonly int RunningPivotProgressHash = Animator.StringToHash("RunningPivotProgress");
    private static readonly int TurningHash = Animator.StringToHash("Turning");
    private static readonly int TurnDirectionHash = Animator.StringToHash("TurnDirection");
    private static readonly int TurnPlaybackRateHash = Animator.StringToHash("TurnPlaybackRate");

    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int DashHash = Animator.StringToHash("Dash");
    private static readonly int AimHash = Animator.StringToHash("Aim");
    private static readonly int DirectionXHash = Animator.StringToHash("DirectionX");
    private static readonly int DirectionYHash = Animator.StringToHash("DirectionY");
    private static readonly int LocomotionPlaybackRateHash = Animator.StringToHash("LocomotionPlaybackRate");
    private static readonly int TurnAngleHash = Animator.StringToHash("TurnAngle");
    private static readonly int GroundedHash = Animator.StringToHash("Grounded");
    private static readonly int VerticalSpeedHash = Animator.StringToHash("VerticalSpeed");

    [Inject]
    public PlayerAnimatorManager(Animator p_animator, PlayerMovementManager p_playerMovementManager, CameraManager p_cameraManager,
        PlayerStatesManager p_playerStatesManager, PlayerRigManager p_playerRigManager)
    {
        _animator = p_animator;
        _turnLayer = p_animator.GetLayerIndex("Turn In Place");
        _playerMovementManager = p_playerMovementManager;
        _cameraManager = p_cameraManager;
        _playerStatesManager = p_playerStatesManager;
        _playerRigManager = p_playerRigManager;
        _previousYaw = p_playerMovementManager.PlayerTransform.eulerAngles.y;
        _teleportVersion = p_playerMovementManager.TeleportVersion;
        _playerMovementManager.OnDash += SetDashParameter;

        _cameraManager.OnAimChange += SetAimBool;
        _playerStatesManager.OnShoot += KickRecoil;
    }

    public void Tick()
    {
        UpdateLocomotionParameters();
    }

    public void Dispose()
    {
        _playerMovementManager.OnDash -= SetDashParameter;

        _cameraManager.OnAimChange -= SetAimBool;
        _playerStatesManager.OnShoot -= KickRecoil;
    }

    private void SetDashParameter(bool p_value)
    {
        _animator.SetBool(DashHash, p_value);
    }

    private void SetAimBool(bool p_aim)
    {
        _playerRigManager.SetAimState(p_aim);
        _animator.SetBool(AimHash, p_aim);
    }

    private void KickRecoil()
    {
        _playerRigManager.KickRecoil(_playerMovementManager.RecoilLocalKickDistance,
            _playerMovementManager.RecoilLocalKickAngle, _playerMovementManager.RecoilRecoverTime);
    }

    private void UpdateLocomotionParameters()
    {
        float moveSpeed = Mathf.Max(_playerMovementManager.MoveSpeed, 0.001f);
        Vector3 localVelocity = _playerMovementManager.PlayerTransform.InverseTransformDirection(_playerMovementManager.PlanarVelocity);
        float normalizedSpeed = Mathf.Clamp01(_playerMovementManager.PlanarSpeed / moveSpeed);
        float directionX = Mathf.Clamp(localVelocity.x / moveSpeed, -1f, 1f);
        float directionY = Mathf.Clamp(localVelocity.z / moveSpeed, -1f, 1f);
        bool entry = _cameraManager.AimEntryActive && _playerMovementManager.Grounded && !_playerMovementManager.Dash;
        bool entryMoving = entry && _cameraManager.AimEntryMoving;
        bool entryStanding = entry && !entryMoving && Mathf.Abs(_cameraManager.AimEntryAngle) >= _cameraManager.AimEntryStepAngle;
        bool entryPivot = entryMoving && _cameraManager.AimEntryRunningPivot;
        // Start the crossfade during the recovery, while the source turn keeps advancing.
        bool entryRecovery = entry && _cameraManager.AimEntryProgress >= _cameraManager.AimEntryBlendStart &&
            Mathf.Abs(_cameraManager.AimYawError) <= 25f;
        _animator.SetBool(AimHash, _cameraManager.IsAiming && (!(entryStanding || entryMoving) || entryRecovery));
        float turnAngle = entry ? _cameraManager.AimYawError : _playerMovementManager.TurnAngle;
        float playbackRate = normalizedSpeed > 0.01f
            ? Mathf.Lerp(_playerMovementManager.LocomotionMinPlaybackRate, 1f, normalizedSpeed)
            : 1f;

        float deltaTime = Time.deltaTime;
        _animator.SetFloat(SpeedHash, normalizedSpeed, _playerMovementManager.AnimationSpeedDampTime, deltaTime);
        _animator.SetFloat(DirectionXHash, directionX, _playerMovementManager.AnimationDirectionDampTime, deltaTime);
        _animator.SetFloat(DirectionYHash, directionY, _playerMovementManager.AnimationDirectionDampTime, deltaTime);
        _animator.SetFloat(LocomotionPlaybackRateHash, playbackRate, _playerMovementManager.AnimationSpeedDampTime, deltaTime);
        _animator.SetFloat(TurnAngleHash, turnAngle, _playerMovementManager.AnimationTurnDampTime, deltaTime);
        _animator.SetBool(GroundedHash, _playerMovementManager.AnimationGrounded);
        _animator.SetFloat(VerticalSpeedHash, _playerMovementManager.VerticalSpeed);
        _animator.SetBool(StartTurningHash, _playerMovementManager.StartTurning || (entryStanding && !entryRecovery));
        // Do not reset a fading source to an unrelated movement turn's time/angle.
        if (entryStanding || _playerMovementManager.StartTurning)
            _animator.SetFloat(StationaryTurnProgressHash, entryStanding ? _cameraManager.AimEntryProgress : _playerMovementManager.StationaryTurnProgress);
        if (entryStanding) _animator.SetFloat(StationaryTurnAngleHash, _cameraManager.AimEntryAngle);
        _animator.SetBool(RunningPivotHash, _playerMovementManager.RunningPivot || (entryPivot && !entryRecovery));
        if (entryPivot || _playerMovementManager.RunningPivot)
        {
            _animator.SetFloat(RunningPivotAngleHash, entryPivot ? _cameraManager.AimEntryAngle : _playerMovementManager.RunningPivotAngle);
            _animator.SetFloat(RunningPivotProgressHash, entryPivot ? _cameraManager.AimEntryProgress : _playerMovementManager.RunningPivotProgress);
        }

        // CameraManager ticks first, so this includes aim yaw as well as physics turns.
        float yaw = _playerMovementManager.PlayerTransform.eulerAngles.y;
        bool teleported = _teleportVersion != _playerMovementManager.TeleportVersion;
        _teleportVersion = _playerMovementManager.TeleportVersion;
        float yawDelta = teleported ? 0f : Mathf.DeltaAngle(_previousYaw, yaw);
        _previousYaw = yaw;
        float rate = deltaTime > 0f ? yawDelta / deltaTime : 0f;
        bool canTurn = !_cameraManager.AimEntryActive && _playerMovementManager.Grounded && !_playerMovementManager.Dash &&
            normalizedSpeed < 0.03f && deltaTime > 0f && !teleported && !_playerMovementManager.RunningPivot;
        _turnRate = canTurn ? Mathf.Lerp(_turnRate, rate, 1f - Mathf.Exp(-25f * deltaTime)) : 0f;
        if (canTurn && _playerMovementManager.StartTurning)
            _turnRate = Mathf.Sign(turnAngle) * _playerMovementManager.StartTurnSpeed;
        bool turning = canTurn && Mathf.Abs(_turnRate) > 8f;
        _animator.SetBool(TurningHash, turning);
        if (_turnLayer >= 0)
        {
            bool turnLegsOnly = turning && !_playerMovementManager.StartTurning;
            if (turnLegsOnly && _turnLayerWeight <= 0f) _animator.Play("Turn In Place.Turn", _turnLayer, 0f);
            _turnLayerWeight = canTurn
                ? Mathf.MoveTowards(_turnLayerWeight, turnLegsOnly ? 1f : 0f, deltaTime / 0.10f)
                : Mathf.MoveTowards(_turnLayerWeight, 0f, deltaTime / 0.14f);
            if (teleported || _playerMovementManager.Dash || !_playerMovementManager.Grounded) _turnLayerWeight = 0f;
            _animator.SetLayerWeight(_turnLayer, Mathf.SmoothStep(0f, 1f, _turnLayerWeight));
        }
        if (turning)
        {
            _animator.SetFloat(TurnDirectionHash, Mathf.Sign(_turnRate));
            float turnAmount = _playerMovementManager.StartTurning
                ? _playerMovementManager.StationaryTurnAngle : Mathf.Sign(_turnRate) * 90f;
            _animator.SetFloat(StationaryTurnAngleHash, turnAmount);
            // Both stationary sources last 2/3 second; the 180 source performs twice the angle.
            float sourceAngularSpeed = Mathf.Lerp(135f, 270f, Mathf.InverseLerp(90f, 180f, Mathf.Abs(turnAmount)));
            _animator.SetFloat(TurnPlaybackRateHash, Mathf.Clamp(Mathf.Abs(_turnRate) / sourceAngularSpeed, 0.35f, 6f));
        }
    }
}
