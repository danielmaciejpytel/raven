using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class RavenLocalization : MonoBehaviour
{
    public const string StringTable = "RavenUI";
    public const string LogoTable = "RavenLogos";
    private const string PreferenceKey = "Raven.Language";
    private static readonly Dictionary<string, string> FallbackEnglish = new Dictionary<string, string>
    {
        ["Ustawienia"] = "Settings", ["Język"] = "Language", ["Rozdzielczość"] = "Resolution", ["Dźwięk"] = "Sound",
        ["Jakość grafiki"] = "Graphics Quality", ["Pełny ekran"] = "Fullscreen", ["Wstecz"] = "Back", ["Twórcy"] = "Credits",
        ["Grafik Pomocniczy"] = "Additional Artist", ["Grafik Główny"] = "Lead Artist", ["Opiekun Projektu"] = "Project Supervisor",
        ["Programista"] = "Programmer", ["Kontynuuj"] = "Continue", ["Nowa gra"] = "New Game", ["Rozpocznij grę"] = "Start Game",
        ["Wznów"] = "Resume", ["Wznów grę"] = "Resume Game", ["Menu główne"] = "Main Menu", ["Wyjście"] = "Exit",
        ["Wyjdź"] = "Exit", ["Wyjdź z gry"] = "Quit Game", ["Wysoka"] = "High", ["Niska"] = "Low", ["Średnia"] = "Medium",
        ["Bardzo wysoka"] = "Very High", ["Normalna"] = "Normal", ["Zdolności"] = "Abilities", ["Koniec gry"] = "Game Over",
        ["Pauza"] = "Pause", ["Zablokowane"] = "Locked", ["Mignięcie"] = "Dash", ["Cel           Strzał"] = "Aim           Shoot",
        ["Cel         Strzał"] = "Aim         Shoot", ["Pomiń"] = "Skip",
        ["Nowy tekst"] = "New Text", ["ala ma kota"] = "Ala has a cat", ["[ SPACJA ]"] = "[ SPACE ]",
        ["[ PPM ] + [ LPM ]"] = "[ RMB ] + [ LMB ]",
        ["Odblokowano unik"] = "Dash unlocked", ["Odblokowano stan ognia"] = "Fire state unlocked",
        ["Podniesiono drugi pistolet"] = "Second pistol picked up", ["locked"] = "locked", ["Design"] = "Design",
        ["Ultra"] = "Ultra", ["aim           shot"] = "aim           shot"
    };
    private static int _logoRequestVersion;
    private readonly Dictionary<TMP_Text, LocalizedString> _runtimeLabels = new Dictionary<TMP_Text, LocalizedString>();
    private readonly Dictionary<TMP_Text, LocalizedString.ChangeHandler> _runtimeLabelCallbacks = new Dictionary<TMP_Text, LocalizedString.ChangeHandler>();
    private readonly Dictionary<TMP_Text, string> _runtimeFallbackSources = new Dictionary<TMP_Text, string>();
    private readonly Dictionary<TMP_Text, List<string>> _runtimeLabelSources = new Dictionary<TMP_Text, List<string>>();

    public static event Action LanguageChanged;
    public static bool IsEnglish { get; private set; }
    private static RavenLocalization _instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateRuntime()
    {
        if (_instance != null) return;
        var host = new GameObject("RavenLocalization");
        DontDestroyOnLoad(host);
        _instance = host.AddComponent<RavenLocalization>();
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        SceneManager.sceneLoaded += OnSceneLoaded;
        LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;
    }

    private IEnumerator Start()
    {
        yield return LocalizationSettings.InitializationOperation;
        var savedCode = PlayerPrefs.GetString(PreferenceKey, "en");
        var locale = LocalizationSettings.AvailableLocales.GetLocale(savedCode);
        if (locale == null) locale = LocalizationSettings.AvailableLocales.GetLocale("en");
        if (locale != null) LocalizationSettings.SelectedLocale = locale;
        IsEnglish = LocalizationSettings.SelectedLocale != null && LocalizationSettings.SelectedLocale.Identifier.Code == "en";
        RefreshScene();
    }

    private void OnDestroy()
    {
        if (_instance != this) return;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;
        ++_logoRequestVersion;
        _instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => RefreshScene();

    private void OnSelectedLocaleChanged(Locale locale)
    {
        IsEnglish = locale != null && locale.Identifier.Code == "en";
        foreach (var label in _runtimeLabels.Keys.ToArray())
            if (label != null) _runtimeLabels[label].RefreshString();
        foreach (var item in _runtimeFallbackSources)
            if (item.Key != null && !_runtimeLabels.ContainsKey(item.Key)) item.Key.text = Get(item.Value);
        foreach (var dropdown in FindObjectsByType<TMP_Dropdown>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!_runtimeLabelSources.TryGetValue(dropdown.captionText, out var options)) continue;
            for (var i = 0; i < options.Count && i < dropdown.options.Count; i++) dropdown.options[i].text = Get(options[i]);
            dropdown.RefreshShownValue();
        }
        RefreshLogo();
        LanguageChanged?.Invoke();
    }

    public static void SetLanguage(int languageIndex)
    {
        if (_instance == null) CreateRuntime();
        _instance.StartCoroutine(_instance.SelectLanguage(languageIndex == 1 ? "en" : "pl"));
    }

    private IEnumerator SelectLanguage(string code)
    {
        yield return LocalizationSettings.InitializationOperation;
        var locale = LocalizationSettings.AvailableLocales.GetLocale(code);
        if (locale == null)
        {
            Debug.LogError($"Localization locale '{code}' is missing. Generate the Raven localization tables from the Raven menu.");
            yield break;
        }
        PlayerPrefs.SetString(PreferenceKey, code);
        PlayerPrefs.Save();
        LocalizationSettings.SelectedLocale = locale;
    }

    public static string Get(string polishText)
    {
        if (string.IsNullOrEmpty(polishText)) return polishText;
        var key = GetKey(polishText);
        var table = LocalizationSettings.StringDatabase.GetTable(StringTable);
        if (table != null && table.GetEntry(key) != null)
        {
            try
            {
                var value = LocalizationSettings.StringDatabase.GetLocalizedString(StringTable, key);
                if (!string.IsNullOrEmpty(value) && value != key) return value;
            }
            catch (Exception) { }
        }
        if (IsEnglish && FallbackEnglish.TryGetValue(polishText, out var fallback)) return fallback;
        if (IsEnglish)
        {
            if (polishText.StartsWith("Wpadasz w panikę", StringComparison.Ordinal)) return "Panic takes hold, but soon gives way to calm. You are not suffocating. You are not breathing. You almost forget what air is. You search for the place where your heart should be, but feel no beat. A strong breeze lashes your wet body, yet you do not shiver from the cold. You are not dead, but neither are you alive. You look to the horizon, trying to work out where you are. A huge stone skull towering over the land catches your eye. You take your first uncertain step and head towards it.";
            if (polishText.StartsWith("Dryfujesz przez bezkresną ciemność", StringComparison.Ordinal)) return "You drift through endless darkness beyond time. The rebellion and betrayal that led to your death seem like distant, almost unreal memories. Suddenly you feel… pain. Startled, you open your eyes. You… can see. You can still taste seawater and blood in your mouth. You try to take a deep breath, but cannot draw in air. Instinctively, gasping for breath, you grab your throat, mouth, nose…";
            if (polishText.StartsWith("W jaskini panuje nieprzenikniony mrok", StringComparison.Ordinal)) return "The cave is pitch-black. You hesitate for a moment, wondering whether to enter. You grip your pistols tightly, take one last look at the misty island, and step into the skull. The sound of the wind fades into darkness, replaced by a steady, rhythmic murmur. It sounds a little like… breathing. You open your eyes again. You feel pain as salty seawater fills your fresh wounds. You cannot forget the smell of blood and gunpowder. You try to scream, but silence is all that leaves your lips as your lungs fill with salt water. Then only cold runs through you, and darkness covers your eyes. You find… peace.";
            if (polishText.StartsWith("Czujesz ból, kiedy słona, morska woda wypełnia twoje świeże rany", StringComparison.Ordinal)) return "You feel pain as salty seawater fills your fresh wounds. The smell of blood and gunpowder will not leave your memory. You try to scream, but silence is all that leaves your lips as your lungs fill with salt water. Then only cold runs through you, and darkness covers your eyes. You find… peace.";
            if (polishText.StartsWith("Teleportuje na krótką odległość", StringComparison.Ordinal)) return "Teleports a short distance";
        }
        return polishText;
    }

    public static void Bind(TMP_Text label, string source)
    {
        if (label == null || string.IsNullOrWhiteSpace(source)) return;
        if (_instance == null) CreateRuntime();
        _instance._runtimeFallbackSources[label] = source;
        var key = GetKey(source);
        var table = LocalizationSettings.StringDatabase.GetTable(StringTable);
        if (table == null || table.GetEntry(key) == null)
        {
            label.text = Get(source);
            return;
        }
        if (_instance._runtimeLabelCallbacks.TryGetValue(label, out var previous) && _instance._runtimeLabels.TryGetValue(label, out var previousString))
            previousString.StringChanged -= previous;
        var localized = new LocalizedString(StringTable, key);
        LocalizedString.ChangeHandler callback = value => { if (label != null) label.text = value; };
        localized.StringChanged += callback;
        _instance._runtimeLabels[label] = localized;
        _instance._runtimeLabelCallbacks[label] = callback;
        localized.RefreshString();
    }

    public static void Unbind(TMP_Text label)
    {
        if (_instance == null || label == null) return;
        if (_instance._runtimeLabelCallbacks.TryGetValue(label, out var callback) && _instance._runtimeLabels.TryGetValue(label, out var localized))
            localized.StringChanged -= callback;
        _instance._runtimeLabels.Remove(label);
        _instance._runtimeLabelCallbacks.Remove(label);
        _instance._runtimeFallbackSources.Remove(label);
    }

    public static string GetKey(string source)
    {
        using (var sha = SHA256.Create())
        {
            var hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Normalize(source)))).Replace("-", string.Empty);
            return "ui_" + hash.Substring(0, 16).ToLowerInvariant();
        }
    }
    public static string Normalize(string source) => (source ?? string.Empty).Trim();

    private static void RefreshScene()
    {
        var dropdowns = FindObjectsByType<TMP_Dropdown>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var dropdownLabels = new HashSet<TMP_Text>();
        foreach (var dropdown in dropdowns)
        {
            if (dropdown.captionText != null) dropdownLabels.Add(dropdown.captionText);
            if (dropdown.itemText != null) dropdownLabels.Add(dropdown.itemText);
        }
        foreach (var label in FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (label == null || dropdownLabels.Contains(label) || string.IsNullOrWhiteSpace(label.text)) continue;
            if (!_instance._runtimeFallbackSources.ContainsKey(label)) Bind(label, label.text);
        }
        foreach (var dropdown in dropdowns)
        {
            if (dropdown.captionText == null) continue;
            if (!_instance._runtimeLabelSources.ContainsKey(dropdown.captionText))
                _instance._runtimeLabelSources[dropdown.captionText] = dropdown.options.Select(option => option.text).ToList();
            var options = _instance._runtimeLabelSources[dropdown.captionText];
            for (var i = 0; i < options.Count && i < dropdown.options.Count; i++) dropdown.options[i].text = Get(options[i]);
            dropdown.RefreshShownValue();
        }
        RefreshLogo();
    }

    private static void RefreshLogo()
    {
        var requestVersion = ++_logoRequestVersion;
        var operation = LocalizationSettings.AssetDatabase.GetLocalizedAssetAsync<Texture>(LogoTable, "MenuLogo");
        operation.Completed += handle => SetLocalizedLogo(handle, requestVersion);
    }

    private static void SetLocalizedLogo(AsyncOperationHandle<Texture> operation, int requestVersion)
    {
        if (requestVersion != _logoRequestVersion) return;
        if (operation.Status != AsyncOperationStatus.Succeeded || operation.Result == null) return;
        foreach (var image in FindObjectsByType<RawImage>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (image.name == "TitleImage") image.texture = operation.Result;
    }
}
