using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

public class SettingsMenu : MonoBehaviour
{
    public AudioMixer audioMixer;
    public TMPro.TMP_Dropdown resolutionDropdown;

    Resolution[] resolutions;

    void Start()
    {
        resolutions = Screen.resolutions;

        if (resolutionDropdown == null) return;

        resolutionDropdown.ClearOptions();

        List<string> options = new List<string>();

        int currentResolutionIndex = 0;

        double bestRefreshDifference = double.MaxValue;
        for (int i = 0; i < resolutions.Length; i++)
        {
            int refreshRate = Mathf.RoundToInt((float)resolutions[i].refreshRateRatio.value);

            string option = resolutions[i].width + " x " + resolutions[i].height + " / " + refreshRate + "Hz";
            options.Add(option);

            double refreshDifference = System.Math.Abs(resolutions[i].refreshRateRatio.value - Screen.currentResolution.refreshRateRatio.value);
            if (resolutions[i].width == Screen.width && resolutions[i].height == Screen.height && refreshDifference < bestRefreshDifference)
            {
                currentResolutionIndex = i;
                bestRefreshDifference = refreshDifference;
            }
        }

        resolutionDropdown.AddOptions(options);
        resolutionDropdown.interactable = resolutions.Length > 0;
        resolutionDropdown.SetValueWithoutNotify(currentResolutionIndex);
        resolutionDropdown.RefreshShownValue();
    }

    public void SetResolution(int resolutionIndex)
    {
        if (resolutions == null || resolutionIndex < 0 || resolutionIndex >= resolutions.Length) return;
        Resolution resolution = resolutions[resolutionIndex];
        Screen.SetResolution(resolution.width, resolution.height, Screen.fullScreenMode, resolution.refreshRateRatio);
    }

    public void SetVolume(float volume)
    {
        audioMixer.SetFloat("volume", volume);
    }

    public void SetQuality(int qualityIndex)
    {
        QualitySettings.SetQualityLevel(qualityIndex);
    }

    public void SetFullscreen(bool isFullscreen)
    {
        Screen.fullScreen = isFullscreen;
    }
}
