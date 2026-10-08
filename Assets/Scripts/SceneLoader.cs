using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class SceneLoader : MonoBehaviour
{
    [Header("Panels")]
    public GameObject environmentPanel;
    public GameObject sessionSettingsPanel;
    public GameObject audiencePanel;

    [Header("Session Duration UI")]
    public Slider customDurationSlider;
    public TextMeshProUGUI durationDisplay;

    private string selectedScene;
    private int sessionDuration = 5; // default 5 minutes

    void Start()
    {
        customDurationSlider.minValue = 1;
        customDurationSlider.maxValue = 30;
        customDurationSlider.value = 5;
        customDurationSlider.onValueChanged.AddListener(OnSliderChanged);
        UpdateDurationDisplay();
    }

    // Environment selection
    public void SelectConferenceRoom()
    {
        selectedScene = "Conference Room 01_V2";
        ShowSessionSettings();
    }

    public void SelectAuditorium()
    {
        selectedScene = "Auditorium";
        ShowSessionSettings();
    }

    public void SelectUniversityClassroom()
    {
        selectedScene = "UniversityClassroom";
        ShowSessionSettings();
    }

    private void ShowSessionSettings()
    {
        environmentPanel.SetActive(false);
        sessionSettingsPanel.SetActive(true);
    }

    // Preset durations
    public void SetTwoMinutes() { sessionDuration = 2; UpdateDurationDisplay(); }
    public void SetFiveMinutes() { sessionDuration = 5; UpdateDurationDisplay(); }
    public void SetTenMinutes() { sessionDuration = 10; UpdateDurationDisplay(); }

    // Custom slider
    void OnSliderChanged(float value)
    {
        sessionDuration = Mathf.RoundToInt(value);
        UpdateDurationDisplay();
    }

    void UpdateDurationDisplay()
    {
        if (durationDisplay != null)
            durationDisplay.text = "Duration: " + sessionDuration + " min";
        if (customDurationSlider != null)
            customDurationSlider.value = sessionDuration;
    }

    // Confirm duration -> show audience panel
    public void ConfirmDuration()
    {
        PlayerPrefs.SetInt("SessionDuration", sessionDuration);
        sessionSettingsPanel.SetActive(false);
        audiencePanel.SetActive(true);
    }

    // Audience selection
    public void LoadWithSupportive()
    {
        PlayerPrefs.SetInt("AudienceMode", 0);
        SceneManager.LoadScene(selectedScene);
    }

    public void LoadWithMixed()
    {
        PlayerPrefs.SetInt("AudienceMode", 1);
        SceneManager.LoadScene(selectedScene);
    }

    public void LoadWithDistracting()
    {
        PlayerPrefs.SetInt("AudienceMode", 2);
        SceneManager.LoadScene(selectedScene);
    }
}