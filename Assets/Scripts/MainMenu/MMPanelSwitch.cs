// Controls which section of MainMenuScene is visible and slides panels between their markers.

using System;
using UnityEngine;

public class MMPanelSwitch : MonoBehaviour
{
    [Serializable]
    private sealed class Panel
    {
        public GameObject root;
        public Transform content;
        public Transform activePosition;
        public Transform inactivePosition;

        public bool IsAssigned => root != null && content != null &&
            activePosition != null && inactivePosition != null;
    }

    [SerializeField] private Panel mainMenu;
    [SerializeField] private Panel levelSelect;
    [SerializeField] private Panel loadout;
    [SerializeField] private Panel settings;
    [SerializeField, Min(0f), Tooltip("Time in seconds for both panels to slide.")]
    private float slideDuration = 0.6f;

    private Panel activePanel;
    private Panel incomingPanel;
    private float elapsed;

    private void Awake()
    {
        Panel[] panels = { mainMenu, levelSelect, loadout, settings };
        foreach (Panel panel in panels)
        {
            if (panel == null || !panel.IsAssigned)
            {
                Debug.LogError("Assign the root, content, and both position markers for every menu panel.", this);
                enabled = false;
                return;
            }
        }

        activePanel = mainMenu;
        foreach (Panel panel in panels)
        {
            bool show = panel == activePanel;
            panel.content.position = (show ? panel.activePosition : panel.inactivePosition).position;
            panel.root.SetActive(show);
        }
    }

    public void ShowMainMenu() => ShowPanel(mainMenu);
    public void ShowLevelSelect() => ShowPanel(levelSelect);
    public void ShowLoadout() => ShowPanel(loadout);
    public void ShowSettings() => ShowPanel(settings);

    private void ShowPanel(Panel panel)
    {
        if (!isActiveAndEnabled || activePanel == null || incomingPanel != null || panel == activePanel)
            return;

        incomingPanel = panel;
        elapsed = 0f;
        incomingPanel.content.position = incomingPanel.inactivePosition.position;
        incomingPanel.root.SetActive(true);

        if (slideDuration <= 0f)
            MovePanels(1f);
    }

    private void Update()
    {
        if (incomingPanel == null)
            return;

        elapsed += Time.unscaledDeltaTime;
        MovePanels(slideDuration <= 0f ? 1f : Mathf.Clamp01(elapsed / slideDuration));
    }

    private void MovePanels(float progress)
    {
        // Both panels use the same eased progress so they start and finish together.
        float eased = Mathf.SmoothStep(0f, 1f, progress);
        activePanel.content.position = Vector3.Lerp(
            activePanel.activePosition.position, activePanel.inactivePosition.position, eased);
        incomingPanel.content.position = Vector3.Lerp(
            incomingPanel.inactivePosition.position, incomingPanel.activePosition.position, eased);

        if (progress < 1f)
            return;

        activePanel.root.SetActive(false);
        activePanel = incomingPanel;
        incomingPanel = null;
    }
}
