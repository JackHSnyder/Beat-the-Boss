// Builds the recording scene's desktop and headset controls on a Meta UI Set backplate.
// It routes pose, move, and animation controls to their workbenches.

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
        private static readonly Vector2 PanelSize = new(680, 700);
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
        private RectTransform keyboardPage;
        private RectTransform movePage;
        private RectTransform animationPage;
        private RectTransform trackingPage;
        private TMP_Text poseTab;
        private TMP_Text settingsTab;
        private TMP_Text moveTab;
        private TMP_Text animationTab;
        private RecordingConfirmation confirmation;
        private RectTransform currentPage;
        private MoveWorkbench moves;
        private AnimationWorkbench animations;
        private PosePanel posePanel;
        private MovePanel movePanel;
        private AnimationPanel animationPanel;
        private TrackingMapPanel trackingMap;
        private TMP_Text tracking;
        private TMP_Text footer;
        private TMP_Text recenter;
        private TMP_Text keyboardName;
        private TMP_Text keyboardTitle;
        private TMP_InputField keyboardInput;
        private string nameBuffer;
        private Action<string> acceptName;
        private RectTransform nameReturnPage;
        private TouchScreenKeyboard systemKeyboard;
        private float systemKeyboardDeadline;
        private bool systemKeyboardOpened;
        private TMP_Text input;
        private TMP_Text tolerance;
        private TMP_Text leftHand;
        private TMP_Text rightHand;
        private TMP_Text modelTest;
        private TMP_Text elbow;
        private TMP_Text[] previewOffsets;
        private Slider toleranceSlider;
        private Slider elbowSlider;
        private Button openFolder;
        private bool inHeadset;
        private bool needsInitialPlacement;
        private bool capitalLetters = true;
        private TMP_Text[] letterKeys;
        private float nextRefresh;
        private double recenterAt = -1;

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
            UpdateLabels();
        }

        private void LateUpdate()
        {
            bool headset = HeadsetActive();
            if (headset != inHeadset)
                SetPresentation(headset);
            confirmation?.Tick();
            UpdateSystemKeyboard();
            if (inHeadset && needsInitialPlacement && OVRPlugin.GetNodePositionTracked(OVRPlugin.Node.EyeCenter))
            {
                PlacePanel();
                needsInitialPlacement = false;
            }
            if (inHeadset && OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch)) RequestRecenter();
            TickRecenter();

            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + 0.1f;
                UpdateLabels();
            }
        }

        private void OnEnable()
        {
            if (panelRoot != null)
            {
                panelRoot.SetActive(true);
                if (movePage.gameObject.activeSelf)
                    movePanel.EnterPage();
                if (animationPage.gameObject.activeSelf)
                    animationPanel.EnterPage();
            }
        }

        private void OnDisable()
        {
            recenterAt = -1;
            CloseSystemKeyboard();
            confirmation?.Cancel();
            movePanel?.LeavePage();
            animationPanel?.LeavePage();
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
            layout.Button(panel, "Exit", 365, 22, 130, 38, RequestBack);
            recenter = layout.Button(panel, "Recenter (B)", 505, 22, 151, 38, RequestRecenter);
            tracking = layout.Label(panel, "", 24, 58, 632, 28);
            tracking.color = PosePanelLayout.Muted;
            poseTab = layout.Button(panel, "Poses", 24, 96, 150, 42, () => SelectPage(posePage));
            moveTab = layout.Button(panel, "Moves", 184, 96, 150, 42, () => SelectPage(movePage));
            animationTab = layout.Button(panel, "Animations", 344, 96, 150, 42, () => SelectPage(animationPage));
            settingsTab = layout.Button(panel, "Settings", 504, 96, 152, 42, () => SelectPage(settingsPage));
            footer = layout.Label(panel, "", 24, 643, 632, 46, 17);
            footer.color = PosePanelLayout.Muted;

            posePage = layout.Rect(panel, "Poses page", 24, 150, 632, 490);
            settingsPage = layout.Rect(panel, "Settings page", 24, 150, 632, 490);
            movePage = layout.Rect(panel, "Moves page", 24, 150, 632, 490);
            animationPage = layout.Rect(panel, "Animations page", 24, 150, 632, 436);
            trackingPage = layout.Rect(panel, "Tracking map page", 24, 150, 632, 490);
            keyboardPage = layout.Rect(panel, "Name keyboard", 24, 150, 632, 432);
            posePanel = new PosePanel(layout, posePage, workbench,
                (title, value, accept) => EditName(posePage, title, value, accept), FramePreview, GuardPose, ShowConfirmation);
            moves = workbench.GetComponent<MoveWorkbench>();
            if (moves == null)
                moves = workbench.gameObject.AddComponent<MoveWorkbench>();
            animations = workbench.GetComponent<AnimationWorkbench>();
            if (animations == null)
                animations = workbench.gameObject.AddComponent<AnimationWorkbench>();
            animationPanel = new AnimationPanel(layout, animationPage, animations,
                (title, value, accept) => EditName(animationPage, title, value, accept), FramePreview, GuardAnimation, ShowConfirmation);
            movePanel = new MovePanel(layout, movePage, moves, workbench, animations,
                (title, value, accept) => EditName(movePage, title, value, accept), FramePreview, (name, clip) =>
                {
                    GuardCurrent(animationPage, () => GuardAnimation(() =>
                    {
                        animations.CopyEmbedded(name, clip);
                        ShowPage(animationPage);
                    }));
                }, GuardMove, ShowConfirmation);
            layout.Button(trackingPage, "Back to Settings", 0, 0, 166, 40, () => SelectPage(settingsPage));
            layout.Label(trackingPage, "Tracking mappings", 184, 0, 448, 40, 23, true);
            trackingMap = new TrackingMapPanel(layout,
                layout.Rect(trackingPage, "Tracking controls", 0, 54, 632, 436), workbench.Tracker);
            BuildSettingsPage();
            BuildKeyboard();
            confirmation = new RecordingConfirmation(layout, panel);
        }

        private void BuildSettingsPage()
        {
            layout.Button(settingsPage, "Tracking mappings  ›", 0, 0, 632, 42, () => SelectPage(trackingPage));
            layout.Label(settingsPage, "Tracking input", 0, 54, 218, 46);
            input = layout.Button(settingsPage, "Automatic", 218, 54, 414, 46, CycleInput);

            layout.Label(settingsPage, "Fallback tolerance", 0, 110, 218, 46);
            toleranceSlider = layout.Slider(settingsPage, "Tolerance", 218, 110, 326, 0, 180,
                value => workbench.ToleranceDegrees = value);
            tolerance = layout.Label(settingsPage, "", 558, 110, 74, 46);

            layout.Label(settingsPage, "Session hand checks", 0, 166, 218, 46);
            leftHand = layout.Button(settingsPage, "Left hand included", 218, 166, 200, 46, () => ToggleHand(PoseCategory.LeftHand));
            rightHand = layout.Button(settingsPage, "Right hand included", 432, 166, 200, 46, () => ToggleHand(PoseCategory.RightHand));

            layout.Label(settingsPage, "Model test input", 0, 222, 218, 46);
            modelTest = layout.Button(settingsPage, "Off", 218, 222, 414, 46,
                () => workbench.SetModelTest(!workbench.ModelTest));

            layout.Label(settingsPage, "Test left elbow", 0, 278, 218, 46);
            elbowSlider = layout.Slider(settingsPage, "Test left elbow", 218, 278, 326, -90, 90, workbench.SetTestBend);
            elbow = layout.Label(settingsPage, "", 558, 278, 74, 46);

            layout.Label(settingsPage, "Preview offset (m)", 0, 334, 212, 46);
            previewOffsets = new TMP_Text[3];
            for (int axis = 0; axis < previewOffsets.Length; axis++)
            {
                int index = axis;
                float x = 218 + axis * 140;
                layout.Button(settingsPage, "−", x, 334, 32, 46, () => AdjustOffset(index, -0.1f));
                TMP_Text value = layout.Label(settingsPage, "", x + 34, 334, 60, 46, 16);
                value.alignment = TextAlignmentOptions.Midline;
                previewOffsets[axis] = value;
                layout.Button(settingsPage, "+", x + 94, 334, 32, 46, () => AdjustOffset(index, 0.1f));
            }

            layout.Label(settingsPage, "Saved game files", 0, 392, 310, 34, 21, true);
            openFolder = layout.Button(settingsPage, "Open save folder", 438, 388, 194, 40, OpenSaveFolder)
                .GetComponentInParent<Button>();
            TMP_Text path = layout.Label(settingsPage, Application.persistentDataPath, 0, 434, 632, 34, 14);
            path.color = PosePanelLayout.Muted;
            layout.Label(settingsPage, "Poses / Moves / Animations / BossTimelines / Tracking", 0, 470, 632, 20, 14).color = PosePanelLayout.Muted;
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
            recenterAt = -1;
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
            keyboardInput.gameObject.SetActive(!headset);
            keyboardName.gameObject.SetActive(headset);
            recenter.GetComponentInParent<Button>(true).gameObject.SetActive(headset);
            openFolder.gameObject.SetActive(!headset);
        }

        // Place the world-space panel from the current headset heading when opening or recentering.
        private void RequestRecenter()
        {
            if (inHeadset && recenterAt < 0) recenterAt=Time.unscaledTimeAsDouble+3;
        }
        private void TickRecenter()
        {
            if (recenterAt>=0 && Time.unscaledTimeAsDouble>=recenterAt)
            { recenterAt=-1; PlacePanel(); needsInitialPlacement=false; }
            if (recenter != null)
                recenter.text=recenterAt<0?"Recenter (B)":$"Recenter in {Mathf.CeilToInt((float)(recenterAt-Time.unscaledTimeAsDouble))}…";
        }
        private void PlacePanel()
        {
            Transform eye = headsetCamera.transform;
            Quaternion heading = PlayerPoseTracker.Heading(eye.rotation);
            Vector3 position = eye.position + heading * new Vector3(-0.39f, -0.17f, 1.23f);
            Vector3 forward = Vector3.ProjectOnPlane(position - eye.position, Vector3.up);
            canvas.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward, Vector3.up));
        }

        private void ShowConfirmation(string title, string text, string accept, Action action)
        {
            CloseSystemKeyboard();
            confirmation.Show(title, text, accept, action);
        }
        private void GuardUnsaved(string kind, bool dirty, Action discard, Action next)
        {
            if (confirmation.IsOpen) return;
            if (!dirty) { next(); return; }
            ShowConfirmation("Discard unsaved changes?",
                $"Your {kind} has unsaved changes. Discard them and continue?", "Discard", () => { discard(); next(); });
        }
        private void GuardPose(Action next) => GuardUnsaved("pose", workbench.IsDirty, workbench.DiscardChanges, next);
        private void GuardMove(Action next) => GuardUnsaved("move", moves.IsDirty, moves.DiscardChanges, next);
        private void GuardAnimation(Action next) => GuardUnsaved("animation", animations.IsDirty, animations.DiscardChanges, next);
        private RectTransform LogicalPage(RectTransform page) => page == keyboardPage ? nameReturnPage : page;
        private void GuardCurrent(RectTransform next, Action action)
        {
            RectTransform outgoing = LogicalPage(currentPage);
            if (outgoing == LogicalPage(next)) { action(); return; }
            if (outgoing == posePage) GuardPose(action);
            else if (outgoing == movePage) GuardMove(action);
            else if (outgoing == animationPage) GuardAnimation(action);
            else action();
        }
        private void ApplyPendingName()
        {
            if (currentPage == keyboardPage) acceptName?.Invoke(nameBuffer ?? "");
        }
        private void RequestBack()
        {
            if (confirmation.IsOpen) return;
            ApplyPendingName();
            var changed = new System.Collections.Generic.List<string>();
            if (workbench.IsDirty) changed.Add("pose");
            if (moves.IsDirty) changed.Add("move");
            if (animations.IsDirty) changed.Add("animation");
            if (changed.Count == 0) { SceneHandler.ReturnFromRecording(); return; }
            ShowConfirmation("Leave recording tools?", "Discard unsaved changes to your " + string.Join(", ", changed) + " and go back?",
                "Discard and leave", () =>
                {
                    workbench.DiscardChanges(); moves.DiscardChanges(); animations.DiscardChanges();
                    SceneHandler.ReturnFromRecording();
                });
        }
        private void SelectPage(RectTransform page)
        {
            if (confirmation.IsOpen) return;
            if (page == keyboardPage) { ShowPage(page); return; }
            if (currentPage == keyboardPage && page != nameReturnPage) ApplyPendingName();
            GuardCurrent(page, () => ShowPage(page));
        }

        private void ShowPage(RectTransform page)
        {
            CloseSystemKeyboard();
            currentPage = page;
            if (page == movePage)
                movePanel.EnterPage();
            else if (movePage.gameObject.activeSelf && page != keyboardPage)
                movePanel.LeavePage();
            if (page == animationPage)
                animationPanel.EnterPage();
            else if (page != keyboardPage &&
                (animationPage.gameObject.activeSelf || (keyboardPage.gameObject.activeSelf && nameReturnPage == animationPage)))
                animationPanel.LeavePage();
            if (page == posePage) posePanel.EnterPage();
            posePage.gameObject.SetActive(page == posePage);
            movePage.gameObject.SetActive(page == movePage);
            animationPage.gameObject.SetActive(page == animationPage);
            trackingPage.gameObject.SetActive(page == trackingPage);
            settingsPage.gameObject.SetActive(page == settingsPage);
            keyboardPage.gameObject.SetActive(page == keyboardPage);
            poseTab.color = page == posePage || (page == keyboardPage && nameReturnPage == posePage)
                ? PosePanelLayout.Accent : PosePanelLayout.Muted;
            moveTab.color = page == movePage || (page == keyboardPage && nameReturnPage == movePage)
                ? PosePanelLayout.Accent : PosePanelLayout.Muted;
            animationTab.color = page == animationPage || (page == keyboardPage && nameReturnPage == animationPage)
                ? PosePanelLayout.Accent : PosePanelLayout.Muted;
            settingsTab.color = page == settingsPage || page == trackingPage ? PosePanelLayout.Accent : PosePanelLayout.Muted;
            UpdateLabels();
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

        private void OpenSaveFolder()
        {
            if (Directory.Exists(Application.persistentDataPath))
                Application.OpenURL(new Uri(Application.persistentDataPath + Path.DirectorySeparatorChar).AbsoluteUri);
        }

        private void UpdateLabels()
        {
            TrackedPose current = workbench.CurrentFrame;
            tracking.text = $"{TrackingLabel(current.Mode)}  ·  {current.Bones.Count} joints";
            footer.text = workbench.CapturePending ? $"Capturing in {workbench.CaptureIn:F1}s" : workbench.Status;
            keyboardName.text = nameBuffer ?? "";
            if (!keyboardInput.isFocused)
                keyboardInput.SetTextWithoutNotify(nameBuffer ?? "");
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
            openFolder.interactable = Directory.Exists(Application.persistentDataPath);
            if (posePage.gameObject.activeSelf)
                posePanel.Refresh();
            if (movePage.gameObject.activeSelf)
                movePanel.Refresh();
            if (animationPage.gameObject.activeSelf)
                animationPanel.Refresh();
            if (trackingPage.gameObject.activeSelf)
                trackingMap.Refresh();
        }

        private string HandLabel(PoseCategory category) =>
            PoseMatchProfile.Label(category) + (workbench.IsSessionIgnored(category) ? " ignored" : " included");

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
