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
    private bool _aimRequested;
    private float _aimExitTimer;

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
        _playerMovementManager = p_playerMovementManager;
        _cameraManager = p_cameraManager;
        _playerStatesManager = p_playerStatesManager;
        _playerRigManager = p_playerRigManager;
        _playerMovementManager.OnDash += SetDashParameter;

        _cameraManager.OnAimChange += SetAimBool;
        _playerStatesManager.OnShoot += KickRecoil;
    }

    public void Tick()
    {
        UpdateAimParameter();
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
        _aimRequested = p_aim;
        _aimExitTimer = 0f;
        _playerRigManager.SetAimState(p_aim, _playerMovementManager.FppExitDelayTime);
        if (p_aim)
        {
            _animator.SetBool(AimHash, true);
        }
    }

    private void KickRecoil()
    {
        _playerRigManager.KickRecoil(_playerMovementManager.RecoilLocalKickDistance,
            _playerMovementManager.RecoilLocalKickAngle, _playerMovementManager.RecoilRecoverTime);
    }

    private void UpdateAimParameter()
    {
        if (_aimRequested || !_animator.GetBool(AimHash)) return;

        _aimExitTimer += Time.deltaTime;
        if (_aimExitTimer >= _playerMovementManager.FppExitDelayTime)
        {
            _animator.SetBool(AimHash, false);
            _aimExitTimer = 0f;
        }
    }

    private void UpdateLocomotionParameters()
    {
        float moveSpeed = Mathf.Max(_playerMovementManager.MoveSpeed, 0.001f);
        Vector3 localVelocity = _playerMovementManager.PlayerTransform.InverseTransformDirection(_playerMovementManager.PlanarVelocity);
        float normalizedSpeed = Mathf.Clamp01(_playerMovementManager.PlanarSpeed / moveSpeed);
        float directionX = Mathf.Clamp(localVelocity.x / moveSpeed, -1f, 1f);
        float directionY = Mathf.Clamp(localVelocity.z / moveSpeed, -1f, 1f);
        float turnAngle = _playerMovementManager.TurnAngle;
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
    }
}
