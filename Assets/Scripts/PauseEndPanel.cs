using Raven.Input;
using UnityEngine;
using Zenject;

public class PauseEndPanel : MonoBehaviour
{
    [SerializeField] private GameObject _coreText;
    [SerializeField] private GameObject _pauseText;
    [UnityEngine.Serialization.FormerlySerializedAs("_reastartButton")]
    [SerializeField] private GameObject _resumeButton;
    [SerializeField] private GameObject _menu;
    [SerializeField] private GameObject _HUD;
    [SerializeField] private GameObject _menuCamera;

    [SerializeField] private CanvasGroup _canvasGroup;
    private InputManager _inputManager;
    private Animator _animator;

    private bool _panelActive;
    private bool _ownsPause;
    private bool _hudWasActive;
    private bool _cursorWasVisible;
    private CursorLockMode _previousCursorLock;
    private float _previousTimeScale = 1f;

    [Inject]
    public void Construct(InputManager p_inputManager)
    {
        _inputManager = p_inputManager;

        _animator = GetComponent<Animator>();
        if (_animator != null) _animator.enabled = false;
        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = 0;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
        }
        if (_pauseText != null) _pauseText.SetActive(false);
        if (_coreText != null) _coreText.SetActive(false);
        if (_resumeButton != null) _resumeButton.SetActive(true);
    }

    private void Update()
    {
        if (_inputManager == null) return;

        if (_inputManager.EscTrigerred())
        {
            if (!_panelActive)
            {
                if (_inputManager.GameplayInputEnabled && (_menuCamera == null || !_menuCamera.activeSelf))
                {
                    PauseGame();
                }
            }
            else if (_pauseText != null && _pauseText.activeSelf)
            {
                BUTTON_Resume();
            }
        }
    }

    public void PauseGame()
    {
        if (_panelActive) return;

        ResetSubmenus();
        _previousTimeScale = Time.timeScale;
        _ownsPause = true;
        _hudWasActive = _HUD != null && _HUD.activeSelf;
        _cursorWasVisible = Cursor.visible;
        _previousCursorLock = Cursor.lockState;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        _panelActive = true;
        if (_pauseText != null) _pauseText.SetActive(true);
        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = 1;
            _canvasGroup.interactable = true;
            _canvasGroup.blocksRaycasts = true;
        }
        if (_HUD != null) _HUD.SetActive(false);
        Time.timeScale = 0;
    }

    public void BUTTON_Resume() // Wznowienie gry
    {
        RestorePause();
    }

    private void ResetSubmenus()
    {
        foreach (var settings in GetComponentsInChildren<SettingsMenu>(true))
        {
            foreach (var dropdown in settings.GetComponentsInChildren<TMPro.TMP_Dropdown>(true))
                dropdown.Hide();
            settings.gameObject.SetActive(false);
        }
        if (UnityEngine.EventSystems.EventSystem.current != null)
            UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
    }

    private void OnDisable()
    {
        RestorePause();
    }

    private void RestorePause()
    {
        if (!_ownsPause) return;
        _ownsPause = false;
        Time.timeScale = _previousTimeScale;
        Cursor.lockState = _previousCursorLock;
        Cursor.visible = _cursorWasVisible;

        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = 0;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
        }
        if (_pauseText != null) _pauseText.SetActive(false);
        _panelActive = false;
        if (_HUD != null) _HUD.SetActive(_hudWasActive);
    }

    public void BUTTON_Exit()
    {
        Application.Quit();
    }

    public void SetCoreTextActive()
    {
        if (_panelActive) return;
        if (_inputManager != null) _inputManager.CanInput = false;
        _animator.enabled = true;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        _panelActive = true;
        _resumeButton.SetActive(false);
        _coreText.SetActive(true);
        _animator.SetTrigger("FadeIn");
    }
}
