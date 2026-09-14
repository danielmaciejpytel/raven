using System.Collections;
using UnityEngine;

namespace Raven.Core
{
    [RequireComponent(typeof(CanvasGroup))]
    [DisallowMultipleComponent]
    public class CanvasFadeIn : MonoBehaviour
    {
        [Tooltip("Czas pojawiania sie calego interfejsu w sekundach. 0 = natychmiast.")]
        [SerializeField, Min(0f)] private float _fadeDuration = 1f;

        private CanvasGroup _group;
        private bool _interactable;
        private bool _blocksRaycasts;
        public bool IsComplete { get; private set; }

        private void Awake()
        {
            _group = GetComponent<CanvasGroup>();
            _interactable = _group.interactable;
            _blocksRaycasts = _group.blocksRaycasts;
            _group.alpha = 0f;
            _group.interactable = false;
            _group.blocksRaycasts = false;
        }

        private IEnumerator Start()
        {
            // Do not charge scene initialization time against the visible fade.
            yield return null;
            float elapsed = 0f;
            while (elapsed < _fadeDuration)
            {
                _group.alpha = Mathf.SmoothStep(0f, 1f, elapsed / _fadeDuration);
                yield return null;
                // Loading stalls must not jump straight to an opaque interface.
                elapsed += Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
            }
            _group.alpha = 1f;
            _group.interactable = _interactable;
            _group.blocksRaycasts = _blocksRaycasts;
            IsComplete = true;
        }
    }
}
