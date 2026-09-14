using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PlayerHudReferences : MonoBehaviour
{
    public  Slider EnergySlider;
    public TextMeshProUGUI EnergyCounterText;
    [Space(10)]
    public Slider HealthSlider;
    public TextMeshProUGUI HealthCounterText;
    [Space(10)]
    public Image ViewFinder;
    [Space(10)]
    public Image DashImage;
    public GameObject DashLocked;
    public Image StateImage;
    public GameObject StateLocked;
    public Image Weapon2Image;
    [Space(10)]
    public Sprite DashSprite;
    public Sprite FireSprite;
    public Sprite NormalStateSprite;
    [Space(10)]
    public GameObject PopUp;
    public TextMeshProUGUI PopUpText;

    private TextMeshProUGUI _livesText;
    private CanvasGroup _deathOverlay;
    private TextMeshProUGUI _gameOverText;

    private void Awake()
    {
        if (ViewFinder != null) ViewFinder.gameObject.SetActive(false);
    }

    public void SetLives(int remaining, int total)
    {
        if (_livesText == null)
        {
            _livesText = CreateText("LivesCounter", transform, 32f);
            var rect = _livesText.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(32f, 110f);
            rect.sizeDelta = new Vector2(280f, 48f);
            _livesText.alignment = TextAlignmentOptions.Left;
        }
        _livesText.SetText("Lives: {0} / {1}", remaining, total);
    }

    public IEnumerator ShowDeath(bool gameOver)
    {
        if (_deathOverlay == null)
        {
            var root = GetComponentInParent<Canvas>().rootCanvas.transform;
            var overlay = new GameObject("DeathOverlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup), typeof(UnityEngine.UI.Image));
            overlay.transform.SetParent(root, false);
            var rect = (RectTransform)overlay.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var canvas = overlay.GetComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 100;
            overlay.GetComponent<UnityEngine.UI.Image>().color = Color.black;
            overlay.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
            _deathOverlay = overlay.GetComponent<CanvasGroup>();
            _deathOverlay.ignoreParentGroups = true;
            _gameOverText = CreateText("GameOverText", overlay.transform, 90f);
            _gameOverText.text = "Game Over";
            _gameOverText.alignment = TextAlignmentOptions.Center;
            _gameOverText.rectTransform.anchorMin = Vector2.zero;
            _gameOverText.rectTransform.anchorMax = Vector2.one;
            _gameOverText.rectTransform.offsetMin = _gameOverText.rectTransform.offsetMax = Vector2.zero;
        }
        _gameOverText.gameObject.SetActive(gameOver);
        _deathOverlay.gameObject.SetActive(true);
        _deathOverlay.alpha = 0f;
        for (float elapsed = 0f; elapsed < 0.65f; elapsed += Time.unscaledDeltaTime)
        {
            _deathOverlay.alpha = Mathf.Clamp01(elapsed / 0.65f);
            yield return null;
        }
        _deathOverlay.alpha = 1f;
        yield return new WaitForSecondsRealtime(gameOver ? 2f : 0.25f);
    }

    public IEnumerator HideDeath()
    {
        for (float elapsed = 0f; elapsed < 0.4f; elapsed += Time.unscaledDeltaTime)
        {
            _deathOverlay.alpha = 1f - Mathf.Clamp01(elapsed / 0.4f);
            yield return null;
        }
        _deathOverlay.gameObject.SetActive(false);
    }

    private TextMeshProUGUI CreateText(string objectName, Transform parent, float size)
    {
        var go = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<TextMeshProUGUI>();
        text.font = HealthCounterText.font;
        text.fontSharedMaterial = HealthCounterText.fontSharedMaterial;
        text.fontSize = size;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }
}
