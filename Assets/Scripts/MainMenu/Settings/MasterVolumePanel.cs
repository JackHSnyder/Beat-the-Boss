// Restores the device's master volume at launch and lets the Settings panel adjust it.

using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BeatTheBoss.Poses
{
    public sealed class MasterVolumePanel : MonoBehaviour
    {
        private const string VolumeKey = "Audio.MasterVolume";

        [SerializeField] private Slider sliderPrefab;

        private Slider slider;
        private TMP_Text valueLabel;
        private bool needsSave;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RestoreVolume()
        {
            AudioListener.volume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, 1f));
        }

        private void OnEnable()
        {
            if (slider == null)
                BuildControls();

            int percent = Mathf.RoundToInt(AudioListener.volume * 100f);
            slider.SetValueWithoutNotify(percent);
            valueLabel.text = $"{percent}%";
        }

        private void BuildControls()
        {
            PosePanelLayout.CreateLabel(transform, "Master Volume", 0, 0, 260, 44, 30, true);
            valueLabel = PosePanelLayout.CreateLabel(transform, "", 0, 0, 120, 44, 28);
            valueLabel.alignment = TextAlignmentOptions.MidlineRight;
            RectTransform valueRect = valueLabel.rectTransform;
            valueRect.anchorMin = valueRect.anchorMax = valueRect.pivot = Vector2.one;

            slider = PosePanelLayout.CreateSlider(sliderPrefab, transform, "Master Volume Slider", 0, 50,
                300, 0, 100, ApplyVolume);
            var sliderRect = (RectTransform)slider.transform;
            sliderRect.anchorMax = new Vector2(1, 1);
            sliderRect.sizeDelta = new Vector2(0, sliderRect.sizeDelta.y);
        }

        private void ApplyVolume(float percent)
        {
            AudioListener.volume = percent / 100f;
            PlayerPrefs.SetFloat(VolumeKey, AudioListener.volume);
            valueLabel.text = $"{Mathf.RoundToInt(percent)}%";
            needsSave = true;
        }

        private void OnDisable() => SaveVolume();

        private void OnApplicationPause(bool paused)
        {
            if (paused)
                SaveVolume();
        }

        private void SaveVolume()
        {
            if (!needsSave)
                return;
            PlayerPrefs.Save();
            needsSave = false;
        }
    }
}
