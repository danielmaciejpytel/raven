using Raven.Input;
using Raven.Manager;
using Raven.Player;
using UnityEngine;
using Zenject;

public class ResetPoint : MonoBehaviour
{
    private InputManager _inputManager;
    private PlayerDataManager _playerDataManager;
    private PlayerMovementManager _playerMovementManager;
    private AudioSource _audioSource;

    public Vector3 ResetPosition { get; set; }
    public Quaternion ResetRotation { get; set; } = Quaternion.identity;
    public Transform PlayerTransform { get; set; }
    public Animator ResetPanel { get; set; }

    [Inject]
    public void Construct(InputManager inputManager, PlayerDataManager playerDataManager, PlayerMovementManager playerMovementManager)
    {
        _inputManager = inputManager;
        _playerDataManager = playerDataManager;
        _playerMovementManager = playerMovementManager;
    }

    private void Start()
    {
        _audioSource = GetComponent<AudioSource>();
        if (PlayerTransform == null)
        {
            PlayerTransform = _playerMovementManager.PlayerTransform;
            ResetPosition = PlayerTransform.position;
            ResetRotation = PlayerTransform.rotation;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!_inputManager.GameplayInputEnabled) return;
        Transform player = _playerMovementManager.PlayerTransform;
        if (other.transform != player && !other.transform.IsChildOf(player)) return;
        if (_audioSource != null && _audioSource.isActiveAndEnabled) _audioSource.Play();
        _playerDataManager.SetCheckpoint(ResetPosition, ResetRotation);
        _playerDataManager.LoseLife();
    }
}
