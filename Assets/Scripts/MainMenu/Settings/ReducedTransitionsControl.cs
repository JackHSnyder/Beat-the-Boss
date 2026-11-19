// Exposes the scene fade preference in the main menu Settings panel.

using UnityEngine;
using UnityEngine.UI;

namespace BeatTheBoss.Poses
{
    public sealed class ReducedTransitionsControl : MonoBehaviour
    {
        [SerializeField] private Toggle togglePrefab;

        private Toggle toggle;

        private void OnEnable()
        {
            if (toggle == null)
            {
                PosePanelLayout.CreateLabel(transform, "Reduced Transitions", 0, 0, 430, 64, 30, true);
                toggle = Instantiate(togglePrefab, transform, false);
                toggle.name = "Reduced Transitions Toggle";
                var rect = (RectTransform)toggle.transform;
                rect.anchorMin = rect.anchorMax = Vector2.one;
                rect.pivot = new Vector2(1, 0.5f);
                rect.anchoredPosition = new Vector2(0, -32);
                rect.localScale = Vector3.one * 2f;
            }

            toggle.isOn = SceneHandler.ReducedTransitions;
            toggle.onValueChanged.AddListener(SetReducedTransitions);
        }

        private void OnDisable()
        {
            if (toggle != null)
                toggle.onValueChanged.RemoveListener(SetReducedTransitions);
        }

        private void SetReducedTransitions(bool enabled) => SceneHandler.ReducedTransitions = enabled;
    }
}
