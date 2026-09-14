using UnityEngine;

public class Checpoint : MonoBehaviour
{
    private Raven.Player.PlayerDataManager _playerDataManager;

    [Zenject.Inject]
    public void Construct(Raven.Player.PlayerDataManager playerDataManager)
    {
        _playerDataManager = playerDataManager;
    }
    [SerializeField] private ResetPoint[] _resetPoints;
    [SerializeField] private Animator _resetPanel;

    private void Awake()
    {
        if (_resetPoints == null) return;
        for (int i = 0; i < _resetPoints.Length; i++)
        {
            if (_resetPoints[i] != null) _resetPoints[i].ResetPanel = _resetPanel;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        CharacterController controller = other.GetComponentInParent<CharacterController>();
        Transform player = controller != null && controller.CompareTag("Player") ? controller.transform : other.transform;
        if (player.CompareTag("Player") && _resetPoints != null)
        {
            _playerDataManager.SetCheckpoint(player.position, player.rotation);
            for (int i = 0; i < _resetPoints.Length; i++)
            {
                if (_resetPoints[i] == null) continue;
                _resetPoints[i].ResetPosition = player.position;
                _resetPoints[i].ResetRotation = player.rotation;
                _resetPoints[i].PlayerTransform = player;
            }
        }
    }
}
