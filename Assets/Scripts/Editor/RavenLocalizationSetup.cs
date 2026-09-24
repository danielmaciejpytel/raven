using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Localization;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public static class RavenLocalizationSetup
{
    private static readonly Dictionary<string, string> English = new Dictionary<string, string>
    {
        ["Ustawienia"] = "Settings", ["Język"] = "Language", ["Rozdzielczość"] = "Resolution", ["Dźwięk"] = "Sound",
        ["Jakość grafiki"] = "Graphics Quality", ["Pełny ekran"] = "Fullscreen", ["Wstecz"] = "Back", ["Twórcy"] = "Credits",
        ["Grafik Pomocniczy"] = "Additional Artist", ["Grafik Główny"] = "Lead Artist", ["Opiekun Projektu"] = "Project Supervisor",
        ["Programista"] = "Programmer", ["Kontynuuj"] = "Continue", ["Nowa gra"] = "New Game", ["Rozpocznij grę"] = "Start Game",
        ["Wznów"] = "Resume", ["Wznów grę"] = "Resume Game", ["Menu główne"] = "Main Menu", ["Wyjście"] = "Exit",
        ["Wyjdź"] = "Exit", ["Wyjdź z gry"] = "Quit Game", ["Wysoka"] = "High", ["Niska"] = "Low", ["Średnia"] = "Medium",
        ["Bardzo wysoka"] = "Very High", ["Normalna"] = "Normal", ["Zdolności"] = "Abilities", ["Koniec gry"] = "Game Over",
        ["Odblokowano unik"] = "Dash unlocked", ["Odblokowano stan ognia"] = "Fire state unlocked",
        ["Podniesiono drugi pistolet"] = "Second pistol picked up", ["locked"] = "locked", ["Design"] = "Design",
        ["Ultra"] = "Ultra", ["aim           shot"] = "aim           shot", ["WORK IN PROGRESS"] = "WORK IN PROGRESS",
        ["Pauza"] = "Pause", ["Zablokowane"] = "Locked", ["Mignięcie"] = "Dash", ["Cel           Strzał"] = "Aim           Shoot",
        ["Cel         Strzał"] = "Aim         Shoot", ["Pomiń"] = "Skip",
        ["Nowy tekst"] = "New Text", ["ala ma kota"] = "Ala has a cat", ["[ SPACJA ]"] = "[ SPACE ]",
        ["[ PPM ] + [ LPM ]"] = "[ RMB ] + [ LMB ]",
        ["Lives: {0} / {1}"] = "Lives: {0} / {1}", ["Życia: {0} / {1}"] = "Lives: {0} / {1}"
    };
    private static readonly Dictionary<string, string> PolishOverrides = new Dictionary<string, string>
    {
        ["locked"] = "zablokowane", ["Design"] = "Projekt", ["High"] = "Wysoka", ["Low"] = "Niska",
        ["Medium"] = "Średnia", ["New Text"] = "Nowy tekst"
    };

    [MenuItem("Raven/Localization/Configure Language Controls")]
    public static void ConfigureLanguageControls()
    {
        const string path = "Assets/Prefabs/Core/MainCanvans.prefab";
        var stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage != null && stage.assetPath == path && stage.scene.isDirty)
            throw new InvalidOperationException("Save the open MainCanvans prefab before configuring language controls.");

        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var configured = 0;
            foreach (var menu in root.GetComponentsInChildren<SettingsMenu>(true))
            {
                if (menu.languageDropdown != null) continue;
                if (menu.resolutionDropdown == null)
                    throw new InvalidOperationException($"{menu.name} has no resolution dropdown to match.");

                TMP_Text resolutionLabel = null;
                foreach (var label in menu.GetComponentsInChildren<TMP_Text>(true))
                    if (label.name == "Rozdzielczość") { resolutionLabel = label; break; }
                if (resolutionLabel == null)
                    throw new InvalidOperationException($"{menu.name} has no resolution label to match.");

                var labelObject = UnityEngine.Object.Instantiate(resolutionLabel.gameObject, resolutionLabel.transform.parent, false);
                labelObject.name = "LanguageLabel";
                ((RectTransform)labelObject.transform).anchoredPosition += new Vector2(0f, -420f);
                labelObject.GetComponent<TMP_Text>().text = "Język";

                var dropdown = UnityEngine.Object.Instantiate(menu.resolutionDropdown, menu.resolutionDropdown.transform.parent, false);
                dropdown.gameObject.name = "LanguageDropdown";
                ((RectTransform)dropdown.transform).anchoredPosition += new Vector2(0f, -420f);
                if (dropdown.captionText == null || !dropdown.captionText.transform.IsChildOf(dropdown.transform))
                    throw new InvalidOperationException($"{menu.name} language dropdown caption did not clone with the dropdown.");
                dropdown.onValueChanged = new TMP_Dropdown.DropdownEvent();
                dropdown.ClearOptions();
                dropdown.AddOptions(new List<string> { "Polski", "English" });
                dropdown.SetValueWithoutNotify(1);
                dropdown.RefreshShownValue();
                UnityEditor.Events.UnityEventTools.AddPersistentListener(dropdown.onValueChanged, menu.SetLanguage);
                menu.languageDropdown = dropdown;
                EditorUtility.SetDirty(menu);
                configured++;
            }

            if (configured > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log($"Configured language controls in {configured} settings panels.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    [MenuItem("Raven/Localization/Generate Polish and English Tables")]
    public static void Generate()
    {
        EnsureFolder("Assets/Localization");
        EnsureFolder("Assets/Localization/Locales");
        EnsureFolder("Assets/Localization/Tables");

        var localizationSettings = LocalizationEditorSettings.ActiveLocalizationSettings;
        if (localizationSettings == null)
        {
            localizationSettings = ScriptableObject.CreateInstance<LocalizationSettings>();
            AssetDatabase.CreateAsset(localizationSettings, "Assets/Localization/LocalizationSettings.asset");
            LocalizationEditorSettings.ActiveLocalizationSettings = localizationSettings;
        }

        var polish = EnsureLocale("pl");
        var english = EnsureLocale("en");
        if (!LocalizationEditorSettings.GetLocales().Contains(polish)) LocalizationEditorSettings.AddLocale(polish);
        if (!LocalizationEditorSettings.GetLocales().Contains(english)) LocalizationEditorSettings.AddLocale(english);
        LocalizationSettings.ProjectLocale = english;
        foreach (var selector in LocalizationSettings.StartupLocaleSelectors)
            if (selector is SpecificLocaleSelector specificSelector) specificSelector.LocaleId = english.Identifier;
        EditorUtility.SetDirty(localizationSettings);

        var texts = CollectPrefabTexts();
        texts.UnionWith(CollectSceneTexts());
        texts.UnionWith(new[]
        {
            "Język", "Polski", "English", "Koniec gry", "Odblokowano unik", "Odblokowano stan ognia",
            "Podniesiono drugi pistolet", "Życia: {0} / {1}", "Lives: {0} / {1}",
            "Czujesz ból, kiedy słona, morska woda wypełnia twoje świeże rany.",
            "Wpadasz w panikę, która jednak po chwili ustępuje poczuciu spokoju.",
            "Dryfujesz przez bezkresną ciemność poza czasem. Bunt i zdrada, które doprowadziły do twojej śmierci, wydają się odległymi wspomnieniami.",
            "W jaskini panuje nieprzenikniony mrok. Wahasz się przez chwilę, zastanawiając się, czy wejść do środka.",
            "Teleportuje na krótką odległość", "[ Q ]", "[ E ]", "Press [e] to pickup", "Życia: {0:0}/{1:0}",
            "Zdrowie: {0}/{1}", "Energia: {0}/{1}", "Press [E] to pick up", "New Text", "100/100", "locked", "Zablokowane", "Odblokowano unik"
        });

        var collection = LocalizationEditorSettings.GetStringTableCollection(RavenLocalization.StringTable)
                         ?? LocalizationEditorSettings.CreateStringTableCollection(RavenLocalization.StringTable, "Assets/Localization/Tables");
        var polishTable = collection.GetTable(polish.Identifier) as StringTable;
        var englishTable = collection.GetTable(english.Identifier) as StringTable;
        foreach (var text in texts)
        {
            var source = RavenLocalization.Normalize(text);
            if (string.IsNullOrWhiteSpace(source)) continue;
            var key = RavenLocalization.GetKey(source);
            var shared = collection.SharedData.GetEntry(key);
            if (shared == null)
            {
                shared = collection.SharedData.AddKey(key);
                SetString(polishTable, shared.Id, PolishOverrides.TryGetValue(source, out var polishValue) ? polishValue : source);
                SetString(englishTable, shared.Id, Translate(source));
            }
            else if (PolishOverrides.TryGetValue(source, out var correctedPolish))
            {
                SetString(polishTable, shared.Id, correctedPolish);
            }
            if (English.TryGetValue(source, out var correctedEnglish))
                SetString(englishTable, shared.Id, correctedEnglish);
        }
        SaveCollection(collection, collection.SharedData, polishTable, englishTable);

        var assetCollection = LocalizationEditorSettings.GetAssetTableCollection(RavenLocalization.LogoTable)
                             ?? LocalizationEditorSettings.CreateAssetTableCollection(RavenLocalization.LogoTable, "Assets/Localization/Tables");
        var polishAssets = assetCollection.GetTable(polish.Identifier) as AssetTable;
        var englishAssets = assetCollection.GetTable(english.Identifier) as AssetTable;
        var logoShared = assetCollection.SharedData.GetEntry("MenuLogo") ?? assetCollection.SharedData.AddKey("MenuLogo");
        AddLogo(assetCollection, polishAssets, logoShared.Id, "Assets/Graphics/GitHub/LogoRavenWyspaCzaszki.png");
        AddLogo(assetCollection, englishAssets, logoShared.Id, "Assets/Graphics/GitHub/LogoRavenSkullIsland.png");
        SaveCollection(assetCollection, assetCollection.SharedData, polishAssets, englishAssets);

        LocalizationEditorSettings.EditorEvents.RaiseCollectionModified(null, collection);
        LocalizationEditorSettings.EditorEvents.RaiseCollectionModified(null, assetCollection);
        AssetDatabase.SaveAssets();
        Debug.Log($"Raven Localization ready: {texts.Count} UI source strings, pl/en string tables, and localized menu logos.");
    }

    [MenuItem("Raven/Localization/Validate Tables")]
    public static string ValidateTables()
    {
        var collection = LocalizationEditorSettings.GetStringTableCollection(RavenLocalization.StringTable);
        if (collection == null || collection.SharedData.Entries.Count == 0)
            return "INCONCLUSIVE: RavenUI has no entries.";

        var checkedEntries = 0;
        var gaps = new List<string>();
        foreach (var key in collection.SharedData.Entries)
        foreach (var table in collection.StringTables)
        {
            checkedEntries++;
            var entry = table.GetEntry(key.Id);
            if (entry == null || string.IsNullOrWhiteSpace(entry.Value))
                gaps.Add($"{table.LocaleIdentifier.Code} / {key.Key}");
        }

        var logoCollection = LocalizationEditorSettings.GetAssetTableCollection(RavenLocalization.LogoTable);
        if (logoCollection == null) gaps.Add("RavenLogos / collection missing");
        else foreach (var table in logoCollection.AssetTables)
        {
            if (table.GetEntry("MenuLogo") == null) gaps.Add($"{table.LocaleIdentifier.Code} / MenuLogo");
        }

        var result = gaps.Count == 0
            ? $"COMPLETE: {checkedEntries} string entries checked across {collection.StringTables.Count} locales; both logo asset entries are present."
            : $"GAPS ({gaps.Count}):\n  " + string.Join("\n  ", gaps);
        Debug.Log(result);
        return result;
    }

    [MenuItem("Raven/Localization/Apply UI Translation Corrections")]
    public static void ApplyUiTranslationCorrections()
    {
        var collection = LocalizationEditorSettings.GetStringTableCollection(RavenLocalization.StringTable);
        var englishTable = collection?.GetTable(new LocaleIdentifier("en")) as StringTable;
        if (englishTable == null) throw new InvalidOperationException("English RavenUI table is missing.");

        foreach (var source in new[] { "Pomiń", "Cel         Strzał" })
        {
            var shared = collection.SharedData.GetEntry(RavenLocalization.GetKey(source));
            if (shared == null) throw new InvalidOperationException($"RavenUI key is missing for '{source}'.");
            SetString(englishTable, shared.Id, English[source]);
        }

        SaveCollection(collection, collection.SharedData, englishTable);
        LocalizationEditorSettings.EditorEvents.RaiseCollectionModified(null, collection);
        AssetDatabase.SaveAssets();
        Debug.Log("Updated English translations for Pomiń and Cel / Strzał.");
    }

    private static HashSet<string> CollectPrefabTexts()
    {
        var texts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject root = null;
            try
            {
                root = PrefabUtility.LoadPrefabContents(path);
                foreach (var label in root.GetComponentsInChildren<TMP_Text>(true))
                {
                    AddText(texts, label.text);
                }
                foreach (var label in root.GetComponentsInChildren<Text>(true)) AddText(texts, label.text);
                foreach (var dropdown in root.GetComponentsInChildren<TMP_Dropdown>(true))
                    foreach (var option in dropdown.options) AddText(texts, option.text);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not read localized text from prefab {path}: {exception.Message}");
            }
            finally
            {
                if (root != null) PrefabUtility.UnloadPrefabContents(root);
            }
        }
        return texts;
    }

    private static HashSet<string> CollectSceneTexts()
    {
        var texts = new HashSet<string>(StringComparer.Ordinal);
        var setup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            foreach (var sceneGuid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(sceneGuid);
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var label in root.GetComponentsInChildren<TMP_Text>(true))
                    {
                        AddText(texts, label.text);
                    }
                    foreach (var label in root.GetComponentsInChildren<Text>(true)) AddText(texts, label.text);
                    foreach (var dropdown in root.GetComponentsInChildren<TMP_Dropdown>(true))
                        foreach (var option in dropdown.options) AddText(texts, option.text);
                }
                EditorSceneManager.CloseScene(scene, true);
            }
        }
        finally
        {
            EditorSceneManager.RestoreSceneManagerSetup(setup);
        }
        return texts;
    }

    private static void AddText(HashSet<string> texts, string value)
    {
        var normalized = RavenLocalization.Normalize(value);
        if (!string.IsNullOrWhiteSpace(normalized)) texts.Add(normalized);
    }

    private static string Translate(string source)
    {
        if (English.TryGetValue(source, out var translated)) return translated;
        if (source.StartsWith("Wpadasz w panikę", StringComparison.Ordinal))
            return "Panic takes hold, but soon gives way to calm. You are not suffocating. You are not breathing. You almost forget what air is. You search for the place where your heart should be, but feel no beat. A strong breeze lashes your wet body, yet you do not shiver from the cold. You are not dead, but neither are you alive. You look to the horizon, trying to work out where you are. A huge stone skull towering over the land catches your eye. You take your first uncertain step and head towards it.";
        if (source.StartsWith("Dryfujesz przez bezkresną ciemność", StringComparison.Ordinal))
            return "You drift through endless darkness beyond time. The rebellion and betrayal that led to your death seem like distant, almost unreal memories. Suddenly you feel… pain. Startled, you open your eyes. You… can see. You can still taste seawater and blood in your mouth. You try to take a deep breath, but cannot draw in air. Instinctively, gasping for breath, you grab your throat, mouth, nose…";
        if (source.StartsWith("W jaskini panuje nieprzenikniony mrok", StringComparison.Ordinal))
            return "The cave is pitch-black. You hesitate for a moment, wondering whether to enter. You grip your pistols tightly, take one last look at the misty island, and step into the skull. The sound of the wind fades into darkness, replaced by a steady, rhythmic murmur. It sounds a little like… breathing. You open your eyes again. You feel pain as salty seawater fills your fresh wounds. You cannot forget the smell of blood and gunpowder. You try to scream, but silence is all that leaves your lips as your lungs fill with salt water. Then only cold runs through you, and darkness covers your eyes. You find… peace.";
        if (source.StartsWith("Teleportuje na krótką odległość", StringComparison.Ordinal))
            return "Teleports a short distance\n\nPress [E] to pick up";
        if (source.StartsWith("Czujesz ból, kiedy słona, morska woda wypełnia twoje świeże rany", StringComparison.Ordinal))
            return "You feel pain as salty seawater fills your fresh wounds. The smell of blood and gunpowder will not leave your memory. You try to scream, but silence is all that leaves your lips as your lungs fill with salt water. Then only cold runs through you, and darkness covers your eyes. You find… peace.";
        if (source.StartsWith("Życia: ", StringComparison.Ordinal)) return source.Replace("Życia:", "Lives:");
        if (source.StartsWith("Lives: ", StringComparison.Ordinal)) return source;
        if (source.StartsWith("Naciśnij [E]", StringComparison.Ordinal)) return "Press [E] to pick up";
        return source;
    }

    private static void SetString(StringTable table, long keyId, string value)
    {
        var entry = table.GetEntry(keyId) ?? table.AddEntry(keyId, value);
        entry.Value = value;
    }

    private static void SaveCollection(UnityEngine.Object collection, SharedTableData sharedData, params UnityEngine.Object[] tables)
    {
        EditorUtility.SetDirty(collection);
        EditorUtility.SetDirty(sharedData);
        foreach (var table in tables) EditorUtility.SetDirty(table);
    }

    private static void AddLogo(AssetTableCollection collection, AssetTable table, long keyId, string path)
    {
        var guid = AssetDatabase.AssetPathToGUID(path);
        if (string.IsNullOrEmpty(guid)) throw new InvalidOperationException($"Logo asset is missing: {path}");
        var entry = table.GetEntry(keyId) ?? table.AddEntry(keyId, guid);
        entry.Guid = guid;

        var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
        settings.CreateOrMoveEntry(guid, settings.DefaultGroup);
    }

    private static Locale EnsureLocale(string code)
    {
        var locale = LocalizationEditorSettings.GetLocale(code);
        if (locale != null) return locale;
        locale = Locale.CreateLocale(new LocaleIdentifier(code));
        AssetDatabase.CreateAsset(locale, $"Assets/Localization/Locales/{code}.asset");
        return locale;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}
