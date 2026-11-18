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
    [SerializeField] private Panel settings;
    [SerializeField] private Panel credits;
    [SerializeField, Min(0f), Tooltip("Time in seconds for both panels to slide.")]
    private float slideDuration = 0.6f;
    [SerializeField, Tooltip("Shared active panel center height above the tracking floor.")]
    private float activePanelHeight = 1.2f;
    [SerializeField] private MenuHeightAdjustment menuHeight = new();
    private Camera headsetCamera;

    private Panel activePanel;
    private Panel incomingPanel;
    private float elapsed;

    private void Awake()
    {
        Panel[] panels = { mainMenu, levelSelect, settings, credits };
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
        var rig = UnityEngine.Object.FindAnyObjectByType<OVRCameraRig>();
        if (rig != null && rig.centerEyeAnchor != null)
            headsetCamera = rig.centerEyeAnchor.GetComponent<Camera>();
        foreach (Panel panel in panels)
        {
            Vector3 active = panel.activePosition.position;
            active.y = (rig != null && rig.trackingSpace != null ? rig.trackingSpace.position.y : 0f) + activePanelHeight;
            panel.activePosition.position = active;
            bool show = panel == activePanel;
            panel.content.position = (show ? panel.activePosition : panel.inactivePosition).position;
            panel.root.SetActive(show);
        }
    }

    public void ShowMainMenu() => ShowPanel(mainMenu);
    public void ShowLevelSelect() => ShowPanel(levelSelect);
    public void ShowSettings() => ShowPanel(settings);
    public void ShowCredits() => ShowPanel(credits);

    private void ShowPanel(Panel panel)
    {
        if (!isActiveAndEnabled || activePanel == null || incomingPanel != null || panel == activePanel)
            return;

        incomingPanel = panel;
        elapsed = 0f;
        incomingPanel.content.position = incomingPanel.inactivePosition.position + Vector3.up * menuHeight.Offset;
        incomingPanel.root.SetActive(true);

        if (slideDuration <= 0f)
            MovePanels(1f);
    }

    private void Update()
    {
        if (headsetCamera == null)
        {
            var rig = UnityEngine.Object.FindAnyObjectByType<OVRCameraRig>();
            if (rig != null && rig.centerEyeAnchor != null)
                headsetCamera = rig.centerEyeAnchor.GetComponent<Camera>();
        }
        menuHeight.Tick(headsetCamera);
        if (incomingPanel == null)
        {
            if (activePanel != null)
                activePanel.content.position = activePanel.activePosition.position + Vector3.up * menuHeight.Offset;
            return;
        }

        elapsed += Time.unscaledDeltaTime;
        MovePanels(slideDuration <= 0f ? 1f : Mathf.Clamp01(elapsed / slideDuration));
    }

    private void MovePanels(float progress)
    {
        // Both panels use the same eased progress so they start and finish together.
        float eased = Mathf.SmoothStep(0f, 1f, progress);
        activePanel.content.position = Vector3.Lerp(
            activePanel.activePosition.position, activePanel.inactivePosition.position, eased) + Vector3.up * menuHeight.Offset;
        incomingPanel.content.position = Vector3.Lerp(
            incomingPanel.inactivePosition.position, incomingPanel.activePosition.position, eased) + Vector3.up * menuHeight.Offset;

        if (progress < 1f)
            return;

        activePanel.root.SetActive(false);
        activePanel = incomingPanel;
        incomingPanel = null;
    }
}
