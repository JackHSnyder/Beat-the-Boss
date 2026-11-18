// Manages pose capture, preview, matching settings, and model tests in the recording scene.
// PoseGamePanel and PoseWorkbenchEditor provide controls. MoveWorkbench reads its current frame for testing.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    [DefaultExecutionOrder(200)]
    public sealed class PoseWorkbench : MonoBehaviour
    {
        [SerializeField] private PlayerPoseTracker tracker;
        [SerializeField] private PlayerAvatarDriver avatarDriver;
        [SerializeField, Range(0, 180)] private float toleranceDegrees = 15;
        [SerializeField] private Vector3 previewOffset = new(1.6f, 0, 1.5f);

        private readonly TrackedPose testFrame = new();
        private readonly HashSet<PoseCategory> sessionIgnoredCategories = new();
        private AvatarPoseRig rig;
        private PoseStore store;
        private GameObject preview;
        private Dictionary<string, Transform> previewBones;
        private PreviewBodyMask animationPreviewMask;
        private Coroutine revealPreview;
        private string pendingName;
        private bool replaceLegacyCapture;
        private float captureTime;
        private bool modelTest;
        private float testBend;
        private string loadedFileName, savedState;
        private bool captureSetup, legacyDraft;
        public string LoadedName => loadedFileName;
        public bool IsDirty => captureSetup || CapturePending || (LoadedPose != null &&
            (savedState == null || MatchProfile?.IsDirty == true || JsonUtility.ToJson(LoadedPose) != savedState));
        public void RenameDraft(string name)
        {
            if (LoadedPose == null) { CaptureName = name; return; }
            LoadedPose.name = (name ?? "").Trim(); CaptureName = LoadedPose.name;
        }
        public void BeginNewPose()
        {
            ClearDraft(); captureSetup = true; Status = "Capture a pose, preview it, then save.";
        }
        private void ClearDraft()
        {
            pendingName = null; HidePreview(); LoadedPose = null; MatchProfile = null;
            loadedFileName = savedState = LegacyPoseName = null; captureSetup = legacyDraft = false;
        }
        public void DiscardChanges()
        {
            pendingName = null;
            if (savedState != null) SetLoadedPose(loadedFileName, JsonUtility.FromJson<SavedPose>(savedState));
            else ClearDraft();
            Status = "Unsaved pose changes discarded.";
        }
        public bool DeleteSaved(string name)
        {
            try
            {
                store.Delete(name);
                if (loadedFileName == name || LegacyPoseName == name) ClearDraft();
                Status = "Deleted " + name + "."; return true;
            }
            catch (Exception error) when (IsFileError(error)) { Status = error.Message; return false; }
        }

        public string PoseDirectory => Path.Combine(Application.persistentDataPath, "Poses");
        public string Status { get; private set; } = "Ready.";
        public TrackedPose CurrentFrame => modelTest ? testFrame : tracker.Frame;
        public SavedPose LoadedPose { get; private set; }
        public string LegacyPoseName { get; private set; }
        public PoseMatchProfile MatchProfile { get; private set; }
        public PoseMatcher.Result Match { get; private set; }
        public GameObject Preview => preview;
        public bool ModelTest => modelTest;
        public float TestBend => testBend;
        public float CaptureIn => pendingName == null ? 0 : Mathf.Max(0, captureTime - Time.unscaledTime);
        public bool CapturePending => pendingName != null;
        public string CaptureName { get; set; } = "My Pose";
        public float CaptureDelay { get; set; } = 3;
        public float ToleranceDegrees { get => toleranceDegrees; set => toleranceDegrees = Mathf.Clamp(value, 0, 180); }
        public IReadOnlyCollection<PoseCategory> SessionIgnoredCategories => sessionIgnoredCategories;

        public void SetSessionCategoryIgnored(PoseCategory category, bool ignored)
        {
            if (ignored)
                sessionIgnoredCategories.Add(category);
            else
                sessionIgnoredCategories.Remove(category);
        }

        public bool IsSessionIgnored(PoseCategory category) =>
            PoseMatchProfile.IsSessionIgnored(category, sessionIgnoredCategories);

        public bool IsExplicitlySessionIgnored(PoseCategory category) => sessionIgnoredCategories.Contains(category);
        public Vector3 PreviewOffset { get => previewOffset; set => previewOffset = value; }
        public PlayerPoseTracker.InputMode TrackingInput { get => tracker.Input; set => tracker.Input = value; }
        public PlayerPoseTracker Tracker => tracker;

        public void QueueCapture() => QueueCapture(CaptureName, CaptureDelay);

        public bool CanChangePose() => !HasUnsavedSettings();

        private void Awake()
        {
            store = new PoseStore(PoseDirectory);
            rig = avatarDriver != null ? avatarDriver.Rig : null;
            if (tracker != null && rig != null)
                return;

            Status = "Assign the player tracker and avatar driver.";
            enabled = false;
        }

        // Model test input substitutes for live tracking only in the recording tools.
        private void LateUpdate()
        {
            if (modelTest)
                rig.ReadModelTest(testFrame, tracker.PlayerSpace);

            if (pendingName != null && Time.unscaledTime >= captureTime)
            {
                string name = pendingName;
                pendingName = null;
                CaptureCurrent(name, replaceLegacyCapture);
            }

            if (MatchProfile != null)
                Match = PoseMatcher.Compare(CurrentFrame, MatchProfile, toleranceDegrees, sessionIgnoredCategories);
        }

        public void QueueCapture(string poseName, float delaySeconds) => QueueCapture(poseName, delaySeconds, false);

        public void RecaptureLegacyPose()
        {
            if (LegacyPoseName != null)
                QueueCapture(LegacyPoseName, CaptureDelay, true);
        }

        private void QueueCapture(string poseName, float delaySeconds, bool replaceLegacy)
        {
            pendingName = (poseName ?? "").Trim();
            replaceLegacyCapture = replaceLegacy;
            captureTime = Time.unscaledTime + Mathf.Max(0, delaySeconds);
            Status = "Capture queued.";
        }

        public string[] ListPoses()
        {
            try
            {
                return store.List();
            }
            catch (Exception error) when (IsFileError(error))
            {
                Status = "Could not list poses. " + error.Message;
                return Array.Empty<string>();
            }
        }

        public void Load(string name)
        {
            if (HasUnsavedSettings())
                return;
            try
            {
                LegacyPoseName = null;
                if (store.IsLegacy(name))
                {
                    LegacyPoseName = name;
                    Status = "Hold the pose and choose Recapture old pose. Review direction constraints after updating.";
                    return;
                }
                SetLoadedPose(name, store.Load(name));
                Status = "Loaded " + LoadedPose.name + ".";
            }
            catch (Exception error) when (IsFileError(error))
            {
                Status = "Could not load pose. " + error.Message;
            }
        }

        public void SaveMatchSettings() => SavePose();

        public void SavePose()
        {
            if (LoadedPose == null || !IsDirty) return;
            try
            {
                LoadedPose.matchSettings = MatchProfile.Export();
                if (legacyDraft)
                {
                    if (LoadedPose.name != loadedFileName)
                        throw new ArgumentException("Save the updated old pose before renaming it.");
                    store.ReplaceLegacy(LoadedPose, useDraftSettings: true);
                }
                else
                    store.SaveDraft(LoadedPose, loadedFileName);
                loadedFileName = LoadedPose.name;
                savedState = JsonUtility.ToJson(LoadedPose); legacyDraft = captureSetup = false;
                MatchProfile.MarkSaved(); Status = "Saved " + LoadedPose.name + ".";
            }
            catch (Exception error) when (IsFileError(error)) { Status = "Could not save pose. " + error.Message; }
        }

        public void RevertMatchSettings()
        {
            if (MatchProfile == null || !MatchProfile.IsDirty)
                return;
            MatchProfile = new PoseMatchProfile(LoadedPose);
            Status = "Restored the saved matching settings.";
        }

        // Build the new profile before switching the active pose, then clear the previous preview.
        private void SetLoadedPose(string fileName, SavedPose pose)
        {
            var profile = new PoseMatchProfile(pose);
            LoadedPose = pose;
            LegacyPoseName = null;
            loadedFileName = fileName;
            MatchProfile = profile;
            savedState = JsonUtility.ToJson(pose); captureSetup = legacyDraft = false;
            HidePreview();
        }

        private bool HasUnsavedSettings()
        {
            if (!IsDirty) return false;
            Status = "Save or discard your pose changes before changing poses.";
            return true;
        }

        public void SetModelTest(bool enabled)
        {
            if (modelTest == enabled)
                return;

            modelTest = enabled;
            avatarDriver.SetManualControl(modelTest);
            if (modelTest)
            {
                rig.SetTestBend(testBend);
            }
            else
            {
                rig.Reset();
            }
        }

        public void SetTestBend(float degrees)
        {
            testBend = degrees;
            if (modelTest)
                rig.SetTestBend(degrees);
        }

        public void ShowPreview()
        {
            if (LoadedPose == null)
                return;

            HideAnimationPreview();
            PreparePreview("Saved pose preview");
            rig.ApplyPreview(LoadedPose.joints, preview.transform.GetChild(0), previewBones);
            Status = "Previewing " + LoadedPose.name + ".";
        }

        internal bool AnimationPreviewVisible => animationPreviewMask != null;

        internal bool ShowAnimationPreview(MoveAnimation animation)
        {
            if (!isActiveAndEnabled)
                return false;
            HidePreview();
            PreparePreview("Animation preview");
            animationPreviewMask = new PreviewBodyMask(preview,previewBones);
            if (animationPreviewMask.Apply(animation.disabledCategories))
                return true;
            HidePreview();
            return false;
        }

        internal void ApplyAnimationPreview(IReadOnlyList<PoseJoint> joints, MoveAnimation animation)
        {
            if (AnimationPreviewVisible)
                rig.ApplyPreview(joints, preview.transform.GetChild(0), previewBones, animation);
        }

        internal void HideAnimationPreview()
        {
            if (AnimationPreviewVisible)
                HidePreview();
        }

        private void PreparePreview(string previewName)
        {
            if (preview == null)
            {
                // Keep the clone inactive until its live tracking and animation components are removed.
                preview = new GameObject(previewName);
                preview.SetActive(false);
                Transform clone = Instantiate(rig.Root.gameObject, preview.transform).transform;
                foreach (Behaviour behaviour in clone.GetComponentsInChildren<Behaviour>(true))
                    Destroy(behaviour);
                previewBones = rig.MapClone(clone);
                revealPreview = StartCoroutine(RevealPreview());
            }

            Transform space = tracker.TrackingSpace;
            preview.transform.SetPositionAndRotation(space.TransformPoint(previewOffset),
                space.rotation * Quaternion.Euler(0, 180, 0));
        }

        public void HidePreview()
        {
            animationPreviewMask?.Dispose();
            animationPreviewMask = null;
            if (revealPreview != null)
            {
                StopCoroutine(revealPreview);
                revealPreview = null;
            }
            if (preview != null)
            {
                preview.SetActive(false);
                Destroy(preview);
                preview = null;
                previewBones = null;
            }
        }

        public void FramePreview(Camera viewer)
        {
            if (preview == null || !preview.activeInHierarchy || viewer == null)
                return;

            var renderers = preview.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return;

            Transform model = preview.transform;
            Quaternion facing = PlayerPoseTracker.Heading(viewer.transform.rotation) * Quaternion.Euler(0, 180, 0);
            model.rotation = facing;
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers)
                bounds.Encapsulate(renderer.bounds);

            float halfHeight = Mathf.Tan(viewer.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float halfWidth = halfHeight * viewer.aspect;
            float distance = Mathf.Max(1.35f, bounds.extents.magnitude / (0.35f * Mathf.Min(halfHeight, halfWidth)));
            Vector3 center = viewer.ViewportToWorldPoint(new Vector3(0.8f, 0.49f, distance));
            model.position += center - bounds.center;
        }

        private IEnumerator RevealPreview()
        {
            yield return null;
            preview.SetActive(true);
            revealPreview = null;
        }

        private void OnDisable()
        {
            pendingName = null;
            HidePreview();
            if (modelTest && avatarDriver != null)
                SetModelTest(false);
        }

        private void CaptureCurrent(string name, bool replaceLegacy)
        {
            if (CurrentFrame.Bones.Count == 0)
            {
                Status = "Nothing to capture. Wait for tracking or enable model test input.";
                return;
            }
            try
            {
                SavedPose captured = SavedPose.Capture(name, CurrentFrame);
                if (replaceLegacy) store.PrepareLegacyReplacement(captured);
                SetLoadedPose(replaceLegacy ? name : null, captured);
                savedState = null; legacyDraft = replaceLegacy;
                ShowPreview();
                Status = $"Captured {captured.name}. Preview it, then choose Save to create the file.";
            }
            catch (Exception error) when (IsFileError(error))
            {
                Status = "Could not capture pose. " + error.Message;
            }
        }

        private static bool IsFileError(Exception error) =>
            error is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException;
    }
}
