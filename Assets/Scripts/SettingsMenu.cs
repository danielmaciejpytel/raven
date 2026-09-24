using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

public class SettingsMenu : MonoBehaviour
{
    public AudioMixer audioMixer;
    public TMPro.TMP_Dropdown resolutionDropdown;
    public TMPro.TMP_Dropdown languageDropdown;
    private Resolution[] resolutions;

    private void OnEnable()
    {
        RavenLocalization.LanguageChanged += RefreshLanguageSelection;
        RefreshLanguageSelection();
    }
    private void OnDisable() => RavenLocalization.LanguageChanged -= RefreshLanguageSelection;

    private void RefreshLanguageSelection()
    {
        if (languageDropdown == null) return;
        languageDropdown.SetValueWithoutNotify(RavenLocalization.IsEnglish ? 1 : 0);
        languageDropdown.RefreshShownValue();
    }

    void Start()
    {
        resolutions = Screen.resolutions;
        if (resolutionDropdown != null)
        {
            resolutionDropdown.ClearOptions();
            List<string> options = new List<string>();
            int currentResolutionIndex = 0;
            double bestRefreshDifference = double.MaxValue;
            for (int i = 0; i < resolutions.Length; i++)
            {
                int refreshRate = Mathf.RoundToInt((float)resolutions[i].refreshRateRatio.value);
                options.Add(resolutions[i].width + " x " + resolutions[i].height + " / " + refreshRate + "Hz");
                double difference = System.Math.Abs(resolutions[i].refreshRateRatio.value - Screen.currentResolution.refreshRateRatio.value);
                if (resolutions[i].width == Screen.width && resolutions[i].height == Screen.height && difference < bestRefreshDifference)
                {
                    currentResolutionIndex = i;
                    bestRefreshDifference = difference;
                }
            }
            resolutionDropdown.AddOptions(options);
            resolutionDropdown.interactable = resolutions.Length > 0;
            resolutionDropdown.SetValueWithoutNotify(currentResolutionIndex);
            resolutionDropdown.RefreshShownValue();
        }

        if (languageDropdown != null)
        {
            languageDropdown.ClearOptions();
            languageDropdown.AddOptions(new List<string> { "Polski", "English" });
            languageDropdown.SetValueWithoutNotify(RavenLocalization.IsEnglish ? 1 : 0);
            languageDropdown.RefreshShownValue();
        }
    }

    public void SetLanguage(int languageIndex)
    {
        RavenLocalization.SetLanguage(languageIndex);
    }

    public void SetResolution(int resolutionIndex)
    {
        if (resolutions == null || resolutionIndex < 0 || resolutionIndex >= resolutions.Length) return;
        Resolution resolution = resolutions[resolutionIndex];
        Screen.SetResolution(resolution.width, resolution.height, Screen.fullScreenMode, resolution.refreshRateRatio);
    }

    public void SetVolume(float volume) => audioMixer.SetFloat("volume", volume);
    public void SetQuality(int qualityIndex) => QualitySettings.SetQualityLevel(qualityIndex);
    public void SetFullscreen(bool isFullscreen) => Screen.fullScreen = isFullscreen;
}
