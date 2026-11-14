// Builds the recording scene's desktop and headset controls on a Meta UI Set backplate.
// It routes recording and preview actions through PoseWorkbench and connects the Moves page to MoveWorkbench.

using System;
using System.IO;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.XR;

namespace BeatTheBoss.Poses
{
    [DefaultExecutionOrder(250)]
    public sealed class PoseGamePanel : MonoBehaviour
    {
        private static readonly Vector2 PanelSize = new(680, 646);
        private const float SystemKeyboardOpenTimeout = 2f;
        private static readonly PlayerPoseTracker.InputMode[] InputModes =
        {
            PlayerPoseTracker.InputMode.Automatic,
            PlayerPoseTracker.InputMode.HandsAndHead,
            PlayerPoseTracker.InputMode.ControllersAndHead,
            PlayerPoseTracker.InputMode.ExternalOnly
        };

        [SerializeField] private PoseWorkbench workbench;
        [SerializeField] private Camera headsetCamera;

        [Header("Meta UI Set prefabs")]
        [SerializeField] private GameObject backplatePrefab;
        [SerializeField] private Button primaryButtonPrefab;
        [SerializeField] private Button secondaryButtonPrefab;
        [SerializeField] private GameObject nameFieldPrefab;
        [SerializeField] private Slider sliderPrefab;

        private PosePanelLayout layout;
        private GameObject panelRoot;
        private Canvas canvas;
        private RayInteractable rayTarget;
        private PokeInteractable pokeTarget;
        private GazeInteractable gazeTarget;
        private InputSystemUIInputModule mouseModule;
        private PointableCanvasModule xrModule;
        private RectTransform panel;
        private RectTransform posePage;
        private RectTransform settingsPage;
        private RectTransform matchPage;
        private RectTransform constraintPage;
        private RectTransform keyboardPage;
        private RectTransform movePage;
        private RectTransform trackingPage;
        private TMP_Text poseTab;
        private TMP_Text settingsTab;
        private TMP_Text matchTab;
        private TMP_Text constraintTab;
        private TMP_Text moveTab;
        private TMP_Text trackingTab;
        private PoseMatchPanel matchPanel;
        private PoseConstraintPanel constraintPanel;
        private MovePanel movePanel;
        private TrackingMapPanel trackingMap;
        private TMP_Text tracking;
        private TMP_Text footer;
        private TMP_Text recenter;
        private TMP_Text poseName;
        private TMP_Text keyboardName;
        private TMP_Text keyboardTitle;
        private TMP_InputField keyboardInput;
        private string nameBuffer;
        private Action<string> acceptName;
        private RectTransform nameReturnPage;
        private TouchScreenKeyboard systemKeyboard;
        private float systemKeyboardDeadline;
        private bool systemKeyboardOpened;
        private TMP_Text delay;
        private TMP_Text selectedPose;
        private TMP_Text preview;
        private TMP_Text match;
        private TMP_Text matchDetails;
        private TMP_Text input;
        private TMP_Text tolerance;
        private TMP_Text leftHand;
        private TMP_Text rightHand;
        private TMP_Text modelTest;
        private TMP_Text elbow;
        private TMP_Text[] previewOffsets;
        private Slider toleranceSlider;
        private Slider elbowSlider;
        private TMP_InputField desktopName;
        private Button captureButton;
        private Button previousPose;
        private Button nextPose;
        private Button loadPose;
        private Button recapturePose;
        private Button previewButton;
        private Button frameButton;
        private Button openFolder;
        private string[] savedPoses = Array.Empty<string>();
        private SavedPose lastLoaded;
        private int selectedIndex;
        private bool inHeadset;
        private bool needsInitialPlacement;
        private bool capitalLetters = true;
        private TMP_Text[] letterKeys;
        private float nextRefresh;

        private void Start()
        {
            if (workbench == null || !workbench.isActiveAndEnabled)
            {
                enabled = false;
                return;
            }

            if (headsetCamera == null && workbench.Tracker.TryGetComponent<OVRCameraRig>(out var rig) &&
                rig.centerEyeAnchor != null)
                headsetCamera = rig.centerEyeAnchor.GetComponent<Camera>();

            if (backplatePrefab == null || primaryButtonPrefab == null || secondaryButtonPrefab == null ||
                nameFieldPrefab == null || sliderPrefab == null)
            {
                Debug.LogError("Assign the Meta UI Set prefabs to the recording panel.", this);
                enabled = false;
                return;
            }

            layout = new PosePanelLayout(primaryButtonPrefab, secondaryButtonPrefab, nameFieldPrefab, sliderPrefab);
            BuildPanel();
            ConfigureInput();
            SetPresentation(HeadsetActive());
            SelectPage(posePage);
            RefreshPoses();
            UpdateLabels();
        }

        private void LateUpdate()
        {
            bool headset = HeadsetActive();
            if (headset != inHeadset)
                SetPresentation(headset);
            UpdateSystemKeyboard();
            if (inHeadset && ((needsInitialPlacement && OVRPlugin.GetNodePositionTracked(OVRPlugin.Node.EyeCenter)) ||
                OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch)))
            {
                PlacePanel();
                needsInitialPlacement = false;
            }

            if (lastLoaded != workbench.LoadedPose)
                RefreshPoses();
            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + 0.1f;
                UpdateLabels();
            }
        }

        private void OnEnable()
        {
            if (panelRoot != null)
                panelRoot.SetActive(true);
        }

        private void OnDisable()
        {
            CloseSystemKeyboard();
            if (panelRoot != null)
                panelRoot.SetActive(false);
        }

        private bool HeadsetActive() => headsetCamera != null && XRSettings.isDeviceActive;

        private void BuildPanel()
        {
            panelRoot = Instantiate(backplatePrefab, transform, false);
            panelRoot.name = "Recording menu";
            canvas = panelRoot.GetComponentInChildren<Canvas>(true);
            canvas.sortingOrder = 10;
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1150, 720);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            // Use the backplate's fixed-size region for the existing page layouts.
            canvas.GetComponent<HorizontalLayoutGroup>().enabled = false;
            panel = (RectTransform)canvas.transform.Find("UIBackplate");
            panel.GetComponent<VerticalLayoutGroup>().enabled = false;
            panel.sizeDelta = PanelSize;
            layout.Label(panel, "Recording tools", 24, 14, 400, 44, 32, true);
            recenter = layout.Button(panel, "Recenter (B)", 505, 22, 151, 38, PlacePanel);
            tracking = layout.Label(panel, "", 24, 58, 632, 28);
            tracking.color = PosePanelLayout.Muted;
            poseTab = layout.Button(panel, "Poses", 24, 96, 88, 42, () => SelectPage(posePage));
            matchTab = layout.Button(panel, "Matching", 120, 96, 102, 42, () => SelectPage(matchPage));
            constraintTab = layout.Button(panel, "Constraints", 230, 96, 120, 42, () => SelectPage(constraintPage));
            moveTab = layout.Button(panel, "Moves", 358, 96, 88, 42, () => SelectPage(movePage));
            trackingTab = layout.Button(panel, "Tracking", 454, 96, 102, 42, () => SelectPage(trackingPage));
            settingsTab = layout.Button(panel, "Settings", 564, 96, 92, 42, () => SelectPage(settingsPage));
            footer = layout.Label(panel, "", 24, 589, 632, 46, 17);
            footer.color = PosePanelLayout.Muted;

            posePage = layout.Rect(panel, "Poses page", 24, 150, 632, 432);
            matchPage = layout.Rect(panel, "Matching page", 24, 150, 632, 436);
            constraintPage = layout.Rect(panel, "Constraints page", 24, 150, 632, 436);
            settingsPage = layout.Rect(panel, "Settings page", 24, 150, 632, 432);
            movePage = layout.Rect(panel, "Moves page", 24, 150, 632, 436);
            trackingPage = layout.Rect(panel, "Tracking map page", 24, 150, 632, 436);
            keyboardPage = layout.Rect(panel, "Name keyboard", 24, 150, 632, 432);
            BuildPosePage();
            matchPanel = new PoseMatchPanel(workbench, layout, matchPage);
            constraintPanel = new PoseConstraintPanel(workbench, layout, constraintPage);
            var moves = workbench.GetComponent<MoveWorkbench>();
            if (moves == null)
                moves = workbench.gameObject.AddComponent<MoveWorkbench>();
            movePanel = new MovePanel(layout, movePage, moves, workbench,
                (title, value, accept) => EditName(movePage, title, value, accept));
            trackingMap = new TrackingMapPanel(layout, trackingPage, workbench.Tracker);
            BuildSettingsPage();
            BuildKeyboard();
        }

        private void BuildPosePage()
        {
            layout.Label(posePage, "Pose name", 0, 0, 144, 44);
            desktopName = layout.NameField(posePage, 150, 0, 482, 44);
            desktopName.onValueChanged.AddListener(value => workbench.CaptureName = value);
            poseName = layout.Button(posePage, "Edit pose name", 150, 0, 482, 44,
                () => EditName(posePage, "Pose name", workbench.CaptureName, value => workbench.CaptureName = value));

            layout.Label(posePage, "Capture delay", 0, 54, 144, 44);
            layout.Button(posePage, "−", 150, 54, 48, 44, () => AdjustDelay(-1));
            delay = layout.Label(posePage, "", 204, 54, 86, 44);
            delay.alignment = TextAlignmentOptions.Midline;
            layout.Button(posePage, "+", 296, 54, 48, 44, () => AdjustDelay(1));
            captureButton = layout.Button(posePage, "Capture and save", 356, 54, 276, 44, workbench.QueueCapture, true)
                .GetComponentInParent<Button>();

            layout.Label(posePage, "Saved poses", 0, 115, 405, 32, 23, true);
            layout.Button(posePage, "Refresh", 514, 112, 118, 40, RefreshPoses);
            previousPose = layout.Button(posePage, "‹", 0, 161, 46, 48, () => MoveSelection(-1))
                .GetComponentInParent<Button>();
            selectedPose = layout.Label(posePage, "", 58, 161, 350, 48);
            nextPose = layout.Button(posePage, "›", 420, 161, 46, 48, () => MoveSelection(1))
                .GetComponentInParent<Button>();
            loadPose = layout.Button(posePage, "Load selected", 480, 161, 152, 48, LoadSelected)
                .GetComponentInParent<Button>();
            recapturePose = layout.Button(posePage, "Recapture old pose", 319, 223, 313, 46,
                workbench.RecaptureLegacyPose).GetComponentInParent<Button>();
            preview = layout.Button(posePage, "Show avatar preview", 0, 223, 305, 46, TogglePreview);
            previewButton = preview.GetComponentInParent<Button>();
            frameButton = layout.Button(posePage, "Frame preview", 319, 223, 313, 46, FramePreview)
                .GetComponentInParent<Button>();

            layout.Fill(layout.Rect(posePage, "Match background", 0, 286, 632, 134), PosePanelLayout.Surface);
            match = layout.Label(posePage, "", 14, 294, 604, 38, 23, true);
            matchDetails = layout.Label(posePage, "", 14, 333, 604, 77, 17);
            matchDetails.alignment = TextAlignmentOptions.TopLeft;
        }

        private void BuildSettingsPage()
        {
            layout.Label(settingsPage, "Tracking input", 0, 0, 218, 46);
            input = layout.Button(settingsPage, "Automatic", 218, 0, 414, 46, CycleInput);

            layout.Label(settingsPage, "Fallback tolerance", 0, 56, 218, 46);
            toleranceSlider = layout.Slider(settingsPage, "Tolerance", 218, 56, 326, 0, 180,
                value => workbench.ToleranceDegrees = value);
            tolerance = layout.Label(settingsPage, "", 558, 56, 74, 46);

            layout.Label(settingsPage, "Session hand checks", 0, 112, 218, 46);
            leftHand = layout.Button(settingsPage, "Left hand included", 218, 112, 200, 46, () => ToggleHand(PoseCategory.LeftHand));
            rightHand = layout.Button(settingsPage, "Right hand included", 432, 112, 200, 46, () => ToggleHand(PoseCategory.RightHand));

            layout.Label(settingsPage, "Model test input", 0, 168, 218, 46);
            modelTest = layout.Button(settingsPage, "Off", 218, 168, 414, 46,
                () => workbench.SetModelTest(!workbench.ModelTest));

            layout.Label(settingsPage, "Test left elbow", 0, 224, 218, 46);
            elbowSlider = layout.Slider(settingsPage, "Test left elbow", 218, 224, 326, -90, 90, workbench.SetTestBend);
            elbow = layout.Label(settingsPage, "", 558, 224, 74, 46);

            layout.Label(settingsPage, "Preview offset (m)", 0, 280, 212, 46);
            previewOffsets = new TMP_Text[3];
            for (int axis = 0; axis < previewOffsets.Length; axis++)
            {
                int index = axis;
                float x = 218 + axis * 140;
                layout.Button(settingsPage, "−", x, 280, 32, 46, () => AdjustOffset(index, -0.1f));
                TMP_Text value = layout.Label(settingsPage, "", x + 34, 280, 60, 46, 16);
                value.alignment = TextAlignmentOptions.Midline;
                previewOffsets[axis] = value;
                layout.Button(settingsPage, "+", x + 94, 280, 32, 46, () => AdjustOffset(index, 0.1f));
            }

            layout.Label(settingsPage, "Pose files", 0, 345, 148, 34, 21, true);
            openFolder = layout.Button(settingsPage, "Open pose folder", 438, 341, 194, 40, OpenPoseFolder)
                .GetComponentInParent<Button>();
            TMP_Text path = layout.Label(settingsPage, workbench.PoseDirectory, 0, 385, 632, 46, 16);
            path.color = PosePanelLayout.Muted;
        }

        private void BuildKeyboard()
        {
            keyboardTitle = layout.Label(keyboardPage, "Name", 0, 0, 300, 34, 23, true);
            layout.Fill(layout.Rect(keyboardPage, "Name display", 0, 42, 632, 52), PosePanelLayout.Surface);
            keyboardName = layout.Label(keyboardPage, "", 12, 42, 608, 52, 22);
            keyboardInput = layout.NameField(keyboardPage, 0, 42, 632, 52);
            keyboardInput.onValueChanged.AddListener(value => nameBuffer = value);
            const string keys = "1234567890QWERTYUIOPASDFGHJKL-ZXCVBNM_";
            letterKeys = new TMP_Text[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                char key = keys[i];
                letterKeys[i] = layout.Button(keyboardPage, key.ToString(), (i % 10) * 64, 112 + (i / 10) * 55,
                    56, 46, () => AppendName(capitalLetters ? key : char.ToLowerInvariant(key)));
            }
            layout.Button(keyboardPage, "Shift", 0, 338, 78, 48, ToggleCapitalLetters);
            layout.Button(keyboardPage, "Space", 88, 338, 160, 48, () => AppendName(' '));
            layout.Button(keyboardPage, "Backspace", 258, 338, 133, 48, Backspace);
            layout.Button(keyboardPage, "Clear", 401, 338, 91, 48, () =>
            {
                nameBuffer = "";
                UpdateLabels();
            });
            layout.Button(keyboardPage, "Done", 502, 338, 130, 48, () => CommitName(nameBuffer), true);
        }

        // Both name editors share the same commit callback regardless of the keyboard used.
        private void EditName(RectTransform returnPage, string title, string value, Action<string> accept)
        {
            CloseSystemKeyboard();
            nameReturnPage = returnPage;
            nameBuffer = value ?? "";
            acceptName = accept;
            keyboardTitle.text = title;
            if (!TryOpenSystemKeyboard(title))
                SelectPage(keyboardPage);
        }

        private bool TryOpenSystemKeyboard(string title)
        {
            if (!inHeadset || Application.platform != RuntimePlatform.Android)
                return false;

            try
            {
                // Android also runs on other headsets. Check that the Meta runtime recognizes this device.
                if (!TouchScreenKeyboard.isSupported ||
                    OVRPlugin.GetSystemHeadsetType() is not (OVRPlugin.SystemHeadset.Oculus_Quest or
                    OVRPlugin.SystemHeadset.Oculus_Quest_2 or OVRPlugin.SystemHeadset.Meta_Quest_Pro or
                    OVRPlugin.SystemHeadset.Meta_Quest_3 or OVRPlugin.SystemHeadset.Meta_Quest_3S or
                    OVRPlugin.SystemHeadset.Meta_VR_Glasses))
                    return false;

                systemKeyboard = TouchScreenKeyboard.Open(nameBuffer, TouchScreenKeyboardType.Default,
                    false, false, false, false, title, 64);
                systemKeyboardDeadline = Time.unscaledTime + SystemKeyboardOpenTimeout;
                return systemKeyboard != null;
            }
            catch (Exception error)
            {
                Debug.LogWarning("Could not open the Horizon OS keyboard. " + error.Message, this);
                return false;
            }
        }

        private void UpdateSystemKeyboard()
        {
            if (systemKeyboard == null)
                return;

            switch (systemKeyboard.status)
            {
                case TouchScreenKeyboard.Status.Done:
                    CommitName(systemKeyboard.text);
                    return;
                case TouchScreenKeyboard.Status.Canceled:
                    CloseSystemKeyboard();
                    return;
                case TouchScreenKeyboard.Status.LostFocus:
                    if (systemKeyboardOpened)
                        nameBuffer = systemKeyboard.text ?? nameBuffer;
                    SelectPage(keyboardPage);
                    return;
            }

            // Horizon OS takes input focus while the overlay is open. Allow time for it to appear.
            systemKeyboardOpened |= TouchScreenKeyboard.visible || !OVRManager.hasInputFocus;
            if (!inHeadset || (!systemKeyboardOpened && Time.unscaledTime >= systemKeyboardDeadline))
                SelectPage(keyboardPage);
        }

        private void CloseSystemKeyboard()
        {
            if (systemKeyboard != null)
                systemKeyboard.active = false;
            systemKeyboard = null;
            systemKeyboardOpened = false;
        }

        private void CommitName(string value)
        {
            CloseSystemKeyboard();
            acceptName?.Invoke(value ?? "");
            SelectPage(nameReturnPage);
        }

        // Route desktop pointer events through Unity Input System and headset events through Meta's canvas module.
        private void ConfigureInput()
        {
            var events = FindAnyObjectByType<EventSystem>();
            if (events == null)
            {
                var eventObject = new GameObject("Pose input", typeof(EventSystem));
                eventObject.transform.SetParent(transform, false);
                events = eventObject.GetComponent<EventSystem>();
            }
            mouseModule = events.GetComponent<InputSystemUIInputModule>();
            if (mouseModule == null)
                mouseModule = events.gameObject.AddComponent<InputSystemUIInputModule>();
            xrModule = events.GetComponent<PointableCanvasModule>();
            if (xrModule == null)
                xrModule = events.gameObject.AddComponent<PointableCanvasModule>();
            xrModule.ExclusiveMode = false;

            // Ray and poke are wired on the prefab. Keep the recording rig's existing gaze input on the same surface.
            rayTarget = panelRoot.GetComponent<RayInteractable>();
            pokeTarget = panelRoot.GetComponent<PokeInteractable>();
            var gazeRoot = new GameObject("Recording gaze interaction");
            gazeRoot.SetActive(false);
            gazeRoot.transform.SetParent(panelRoot.transform, false);
            gazeTarget = gazeRoot.AddComponent<GazeInteractable>();
            gazeTarget.InjectAllGazeInteractable(panelRoot.GetComponentInChildren<ClippedPlaneSurface>(true));
            gazeTarget.InjectOptionalPointableElement(panelRoot.GetComponent<PointableCanvas>());
            gazeTarget.InjectOptionalMultiGrabScalingSupport(false);
            gazeRoot.SetActive(true);
        }

        private void SetPresentation(bool headset)
        {
            inHeadset = headset;
            needsInitialPlacement = headset;
            rayTarget.enabled = pokeTarget.enabled = gazeTarget.enabled = headset;
            mouseModule.enabled = !headset;
            xrModule.enabled = headset;
            canvas.renderMode = headset ? RenderMode.WorldSpace : RenderMode.ScreenSpaceOverlay;
            canvas.worldCamera = headset ? headsetCamera : null;
            var root = (RectTransform)canvas.transform;
            if (headset)
            {
                root.sizeDelta = PanelSize;
                root.localScale = Vector3.one * 0.00115f;
                panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
                panel.anchoredPosition = Vector2.zero;
                PlacePanel();
            }
            else
            {
                root.localScale = Vector3.one;
                panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0, 0.5f);
                panel.anchoredPosition = new Vector2(24, 0);
            }
            desktopName.gameObject.SetActive(!headset);
            keyboardInput.gameObject.SetActive(!headset);
            keyboardName.gameObject.SetActive(headset);
            poseName.GetComponentInParent<Button>(true).gameObject.SetActive(headset);
            recenter.GetComponentInParent<Button>(true).gameObject.SetActive(headset);
            openFolder.gameObject.SetActive(!headset);
        }

        // Place the world-space panel from the current headset heading when opening or recentering.
        private void PlacePanel()
        {
            Transform eye = headsetCamera.transform;
            Quaternion heading = PlayerPoseTracker.Heading(eye.rotation);
            Vector3 position = eye.position + heading * new Vector3(-0.39f, -0.17f, 1.23f);
            Vector3 forward = Vector3.ProjectOnPlane(position - eye.position, Vector3.up);
            canvas.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward, Vector3.up));
        }

        private void SelectPage(RectTransform page)
        {
            CloseSystemKeyboard();
            posePage.gameObject.SetActive(page == posePage);
            matchPage.gameObject.SetActive(page == matchPage);
            constraintPage.gameObject.SetActive(page == constraintPage);
            movePage.gameObject.SetActive(page == movePage);
            trackingPage.gameObject.SetActive(page == trackingPage);
            settingsPage.gameObject.SetActive(page == settingsPage);
            keyboardPage.gameObject.SetActive(page == keyboardPage);
            poseTab.color = page == posePage || (page == keyboardPage && nameReturnPage == posePage)
                ? PosePanelLayout.Accent : PosePanelLayout.Muted;
            moveTab.color = page == movePage || (page == keyboardPage && nameReturnPage == movePage)
                ? PosePanelLayout.Accent : PosePanelLayout.Muted;
            matchTab.color = page == matchPage ? PosePanelLayout.Accent : PosePanelLayout.Muted;
            constraintTab.color = page == constraintPage ? PosePanelLayout.Accent : PosePanelLayout.Muted;
            settingsTab.color = page == settingsPage ? PosePanelLayout.Accent : PosePanelLayout.Muted;
            trackingTab.color = page == trackingPage ? PosePanelLayout.Accent : PosePanelLayout.Muted;
            UpdateLabels();
        }

        private void RefreshPoses()
        {
            string preferred = workbench.LoadedPose != lastLoaded ? workbench.LoadedPose?.name :
                savedPoses.Length > 0 ? savedPoses[selectedIndex] : null;
            lastLoaded = workbench.LoadedPose;
            savedPoses = workbench.ListPoses();
            int retained = Array.IndexOf(savedPoses, preferred);
            selectedIndex = retained >= 0 ? retained : Mathf.Clamp(selectedIndex, 0, Mathf.Max(0, savedPoses.Length - 1));
            UpdateLabels();
        }

        private void MoveSelection(int direction)
        {
            if (savedPoses.Length > 1)
                selectedIndex = (selectedIndex + direction + savedPoses.Length) % savedPoses.Length;
            UpdateLabels();
        }

        private void LoadSelected()
        {
            if (savedPoses.Length != 0)
                workbench.Load(savedPoses[selectedIndex]);
        }

        private void TogglePreview()
        {
            if (workbench.Preview != null)
                workbench.HidePreview();
            else
                workbench.ShowPreview();
        }

        private void FramePreview() => workbench.FramePreview(headsetCamera);

        private void CycleInput()
        {
            int index = Array.IndexOf(InputModes, workbench.TrackingInput);
            workbench.TrackingInput = InputModes[(index + 1) % InputModes.Length];
            UpdateLabels();
        }

        private void ToggleHand(PoseCategory category)
        {
            workbench.SetSessionCategoryIgnored(category, !workbench.IsExplicitlySessionIgnored(category));
            UpdateLabels();
        }

        private void AdjustDelay(float change)
        {
            workbench.CaptureDelay = Mathf.Max(0, workbench.CaptureDelay + change);
            UpdateLabels();
        }

        private void AdjustOffset(int axis, float change)
        {
            Vector3 offset = workbench.PreviewOffset;
            offset[axis] += change;
            workbench.PreviewOffset = offset;
            UpdateLabels();
        }

        private void AppendName(char character)
        {
            if (nameBuffer.Length < 64)
                nameBuffer += character;
            UpdateLabels();
        }

        private void Backspace()
        {
            if (nameBuffer.Length != 0)
                nameBuffer = nameBuffer.Substring(0, nameBuffer.Length - 1);
            UpdateLabels();
        }

        private void ToggleCapitalLetters()
        {
            capitalLetters = !capitalLetters;
            foreach (TMP_Text key in letterKeys)
                key.text = capitalLetters ? key.text.ToUpperInvariant() : key.text.ToLowerInvariant();
        }

        private void OpenPoseFolder()
        {
            if (Directory.Exists(workbench.PoseDirectory))
                Application.OpenURL(new Uri(workbench.PoseDirectory + Path.DirectorySeparatorChar).AbsoluteUri);
        }

        private void UpdateLabels()
        {
            TrackedPose current = workbench.CurrentFrame;
            tracking.text = $"{TrackingLabel(current.Mode)}  ·  {current.Bones.Count} joints";
            if ((matchPage.gameObject.activeSelf || constraintPage.gameObject.activeSelf) && workbench.LoadedPose != null)
            {
                tracking.text += $"  ·  {MatchLabel(workbench.Match)}";
                if (workbench.MatchProfile.IsDirty)
                    tracking.text += "  ·  Unsaved";
            }
            footer.text = workbench.CapturePending ? $"Capturing in {workbench.CaptureIn:F1}s" : workbench.Status;
            string name = workbench.CaptureName;
            if (!desktopName.isFocused)
                desktopName.SetTextWithoutNotify(name);
            poseName.text = string.IsNullOrWhiteSpace(name) ? "Enter pose name" : name + "  ·  Edit";
            keyboardName.text = nameBuffer ?? "";
            if (!keyboardInput.isFocused)
                keyboardInput.SetTextWithoutNotify(nameBuffer ?? "");
            delay.text = $"{workbench.CaptureDelay:0.#} s";
            captureButton.interactable = !workbench.CapturePending && !string.IsNullOrWhiteSpace(name);
            previousPose.interactable = nextPose.interactable = savedPoses.Length > 1;
            loadPose.interactable = savedPoses.Length > 0;
            selectedPose.text = savedPoses.Length == 0 ? "No saved poses" :
                $"{savedPoses[selectedIndex]}  ({selectedIndex + 1}/{savedPoses.Length})";
            preview.text = workbench.Preview == null ? "Show avatar preview" : "Hide avatar preview";
            previewButton.interactable = workbench.LoadedPose != null;
            frameButton.interactable = headsetCamera != null && workbench.Preview != null && workbench.Preview.activeInHierarchy;
            bool legacySelected = savedPoses.Length > 0 && savedPoses[selectedIndex] == workbench.LegacyPoseName;
            recapturePose.gameObject.SetActive(legacySelected);
            recapturePose.interactable = !workbench.CapturePending;
            frameButton.gameObject.SetActive(!legacySelected);
            input.text = workbench.TrackingInput switch
            {
                PlayerPoseTracker.InputMode.HandsAndHead => "Head and hands  ›",
                PlayerPoseTracker.InputMode.ControllersAndHead => "Head and controllers  ›",
                PlayerPoseTracker.InputMode.ExternalOnly => "External sources  ›",
                _ => "Automatic  ›"
            };
            tolerance.text = $"{workbench.ToleranceDegrees:0}°";
            toleranceSlider.SetValueWithoutNotify(workbench.ToleranceDegrees);
            leftHand.text = HandLabel(PoseCategory.LeftHand);
            rightHand.text = HandLabel(PoseCategory.RightHand);
            modelTest.text = workbench.ModelTest ? "On" : "Off";
            elbowSlider.interactable = workbench.ModelTest;
            elbowSlider.SetValueWithoutNotify(workbench.TestBend);
            elbow.text = $"{workbench.TestBend:0}°";
            previewOffsets[0].text = $"X {workbench.PreviewOffset.x:0.0}";
            previewOffsets[1].text = $"Y {workbench.PreviewOffset.y:0.0}";
            previewOffsets[2].text = $"Z {workbench.PreviewOffset.z:0.0}";
            openFolder.interactable = savedPoses.Length > 0;
            UpdateMatch();
            if (matchPage.gameObject.activeSelf)
                matchPanel.Refresh();
            if (constraintPage.gameObject.activeSelf)
                constraintPanel.Refresh();
            if (movePage.gameObject.activeSelf)
                movePanel.Refresh();
            if (trackingPage.gameObject.activeSelf)
                trackingMap.Refresh();
        }

        private string HandLabel(PoseCategory category) =>
            PoseMatchProfile.Label(category) + (workbench.IsSessionIgnored(category) ? " ignored" : " included");

        private void UpdateMatch()
        {
            if (workbench.LoadedPose == null)
            {
                match.text = "No pose loaded";
                match.color = PosePanelLayout.Foreground;
                matchDetails.text = "";
                return;
            }

            PoseMatcher.Result result = workbench.Match;
            string state = MatchLabel(result);
            match.text = $"{state}  ·  {workbench.LoadedPose.name}";
            match.color = result.IsMatch ? PosePanelLayout.Accent : PosePanelLayout.Warning;
            string detail = $"{result.Compared} compared  ·  {result.Missing} missing  ·  {result.OutsideTolerance} over individual limits";
            if (result.ConstraintCount > 0)
                detail += $"\nConstraints  ·  {result.PassedConstraints}/{result.ConstraintCount} passing";
            if (result.FirstFailedCategory is PoseCategory failed)
                detail += $"\nFailed category  ·  {PoseMatchProfile.Label(failed)}";
            if (result.FirstMissingJoint != null)
                detail += $"\nFirst missing  ·  {result.FirstMissingJoint}";
            else if (result.ConstraintCount == 0 && result.WorstJoint != null)
                detail += $"\nLargest error {result.WorstError:F1}°  ·  {result.WorstJoint}";
            matchDetails.text = detail;
        }

        private static string MatchLabel(PoseMatcher.Result result) => result.Missing > 0 ? "Tracking missing" :
            result.Compared == 0 && result.ConstraintCount == 0 ? "No checks selected" : result.IsMatch ? "Match" :
            result.PassedConstraints < result.ConstraintCount ? $"Constraint {result.FirstFailedConstraint + 1} failed" : "Outside tolerance";

        private static string TrackingLabel(TrackingMode mode) => mode switch
        {
            TrackingMode.FullBody => "Full-body tracking",
            TrackingMode.UpperBody => "Upper-body tracking",
            TrackingMode.Devices => "Headset and devices",
            TrackingMode.ModelTest => "Model test input",
            TrackingMode.External => "External tracking",
            _ => "Waiting for tracking"
        };
    }
}
