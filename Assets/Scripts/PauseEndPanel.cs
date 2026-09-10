using Raven.Input;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Zenject;
using TMPro;

public class PauseEndPanel : MonoBehaviour
{
    [SerializeField] private GameObject _coreText;
    [SerializeField] private GameObject _pauseText;
    [SerializeField] private GameObject _resumeButton;
    [SerializeField] private GameObject _menu;
    [SerializeField] private GameObject _HUD;
    [SerializeField] private GameObject _menuCamera;

    [SerializeField] private CanvasGroup _canvasGroup;
    private InputManager _inputManager;
    private Animator _animator;

    private bool _panelActive;

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
                if (_menuCamera == null || !_menuCamera.activeSelf)
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
        Time.timeScale = 1;

        // Ukrywamy i blokujemy kursor po wznowieniu gry
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;

        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = 0;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
        }
        if (_pauseText != null) _pauseText.SetActive(false);
        _panelActive = false;
        if (_HUD != null) _HUD.SetActive(true);
    }

    public void BUTTON_Exit()
    {
        Application.Quit();
    }

    public void SetCoreTextActive()
    {
        _animator.enabled = true;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        _panelActive = true;
        _resumeButton.SetActive(false);
        _coreText.SetActive(true);
        _animator.SetTrigger("FadeIn");
    }
}
