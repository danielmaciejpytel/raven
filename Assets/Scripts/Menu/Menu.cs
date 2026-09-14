using Raven.Input;
using Raven.Manager;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

namespace Raven.Core
{
    public class Menu : MonoBehaviour
    {
        [SerializeField] private Animator _playerHud;
        [SerializeField] private Animator _storyPanel;
        [SerializeField] private GameObject _menuCam;
        [SerializeField] private Button _nextButton;
        [Header("Przejscie do gry")]
        [SerializeField, Min(0f)] private float _gameplayFadeDuration = 1f;

        private InputManager _inputManager;
        private CameraManager _cameraManager;
        private CanvasGroup _blackout;
        private CanvasFadeIn _canvasFade;
        private bool _started;
        private bool _playRequested;
        private bool _revealing;
        private Animator _animator;
        private int _currentPage = 1;

        [Inject]
        public void Construct(InputManager p_inputManager, CameraManager cameraManager)
        {
            _inputManager = p_inputManager;
            _cameraManager = cameraManager;
        }

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            _canvasFade = GetComponentInParent<Canvas>().rootCanvas.GetComponent<CanvasFadeIn>();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _inputManager.CanInput = false;
        }

        private void Start()
        {
            _cameraManager.PrepareGameplayCamera();
            // Keep the blackout independent of the animated menu/story CanvasGroups.
            var canvas = GetComponentInParent<Canvas>().rootCanvas;
            var background = new GameObject("GameplayBlackout", typeof(RectTransform));
            // Configure transparency before adding any renderable UI or joining the canvas.
            background.SetActive(false);
            _blackout = background.AddComponent<CanvasGroup>();
            _blackout.alpha = 0f;
            _blackout.blocksRaycasts = false;
            var image = background.AddComponent<UnityEngine.UI.Image>();
            image.color = Color.black;
            image.raycastTarget = false;
            background.transform.SetParent(canvas.transform, false);
            background.transform.SetAsLastSibling();
            var rect = (RectTransform)background.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            // Story remains visible and clickable above the fully black world and HUD.
            var storyRoot = _storyPanel.transform;
            while (storyRoot.parent != canvas.transform && storyRoot.parent != null)
                storyRoot = storyRoot.parent;
            if (storyRoot.parent == canvas.transform) storyRoot.SetAsLastSibling();
        }

        private void Update()
        {
            if (_playRequested && !_revealing && _playerHud.GetCurrentAnimatorStateInfo(0).IsTag("End"))
                StartCoroutine(RevealGameplay());
        }

        public void Button_StartGame()
        {
            if (_started || (_canvasFade != null && !_canvasFade.IsComplete)) return;
            _started = true;
            _inputManager.CanInput = false;
            _blackout.alpha = 1f;
            _blackout.gameObject.SetActive(true);
            _cameraManager.PrepareGameplayCamera();
            if (_menuCam != null) _menuCam.SetActive(false);
            _animator.enabled = true;
            _animator.SetTrigger("FadeOut");
            _storyPanel.SetTrigger("FadeIn");
        }

        public void Button_NextPage()
        {
            if (!_started || _playRequested) return;
            _storyPanel.SetTrigger("NextPage");
            _currentPage++;
            _nextButton.interactable = false;

            if (_currentPage == 4)
            {
                RequestGameplay();
            }
        }

        public void Button_SkipStory()
        {
                if (!_started || _playRequested) return;
                _storyPanel.SetTrigger("SkipStory");
                RequestGameplay();
        }

        private void RequestGameplay()
        {
            _playRequested = true;
            _playerHud.SetTrigger("FadeIn");
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private IEnumerator RevealGameplay()
        {
            _revealing = true;
            // Let Cinemachine evaluate the switch even when story is skipped immediately.
            yield return null;
            while (_cameraManager.IsCameraBlending) yield return null;
            if (_storyPanel != null) _storyPanel.gameObject.SetActive(false);
            float elapsed = 0f;
            while (elapsed < _gameplayFadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                _blackout.alpha = 1f - Mathf.SmoothStep(0f, 1f,
                    Mathf.Clamp01(elapsed / _gameplayFadeDuration));
                yield return null;
            }
            _blackout.alpha = 0f;
            Destroy(_blackout.gameObject);
            _inputManager.CanInput = true;
            enabled = false;
        }

        private void OnDestroy()
        {
            if (_blackout != null) Destroy(_blackout.gameObject);
        }

        public void Button_ExitGame()
        {
            Application.Quit();
        }
    }
}

