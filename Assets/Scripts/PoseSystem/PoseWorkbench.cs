using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Meta.XR.Movement.Retargeting;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    [DefaultExecutionOrder(200)]
    public sealed class PoseWorkbench : MonoBehaviour
    {
        [SerializeField] private PlayerPoseTracker tracker;
        [SerializeField] private CharacterRetargeter avatar;
        [SerializeField, Range(0, 180)] private float toleranceDegrees = 15;
        [SerializeField] private PoseBranches ignoreBranches;
        [SerializeField] private Vector3 previewOffset = new(1.6f, 0, 1.5f);

        private readonly TrackedPose testFrame = new();
        private AvatarPoseRig rig;
        private PoseStore store;
        private GameObject preview;
        private Dictionary<string, Transform> previewBones;
        private Coroutine revealPreview;
        private string pendingName;
        private float captureTime;
        private bool modelTest;
        private bool retargeterWasEnabled;
        private bool showingDevicePose;
        private float testBend;
        private string loadedFileName;

        public string PoseDirectory => Path.Combine(Application.persistentDataPath, "Poses");
        public string Status { get; private set; } = "Ready.";
        public TrackedPose CurrentFrame => modelTest ? testFrame : tracker.Frame;
        public SavedPose LoadedPose { get; private set; }
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
        public PoseBranches IgnoreBranches { get => ignoreBranches; set => ignoreBranches = value; }
        public Vector3 PreviewOffset { get => previewOffset; set => previewOffset = value; }
        public PlayerPoseTracker.InputMode TrackingInput { get => tracker.Input; set => tracker.Input = value; }

        public void QueueCapture() => QueueCapture(CaptureName, CaptureDelay);

        private void Awake()
        {
            store = new PoseStore(PoseDirectory);
            if (tracker != null && avatar != null)
            {
                rig = new AvatarPoseRig(avatar);
                return;
            }

            Status = "Assign the player tracker and character retargeter.";
            enabled = false;
        }

        private void LateUpdate()
        {
            if (modelTest)
            {
                rig.ReadModelTest(testFrame);
                testFrame.SetPlayerSpace(rig.Root, tracker.PlayerSpace);
            }
            else if (tracker.Frame.Mode == TrackingMode.Devices)
            {
                rig.ApplyDevices(tracker.Frame, tracker.TrackingSpace);
                showingDevicePose = true;
            }
            else if (showingDevicePose)
            {
                if (!HasRetargetedBody())
                    rig.Reset();
                showingDevicePose = false;
            }

            if (pendingName != null && Time.unscaledTime >= captureTime)
            {
                string name = pendingName;
                pendingName = null;
                SaveCurrent(name);
            }

            if (MatchProfile != null)
                Match = PoseMatcher.Compare(CurrentFrame, MatchProfile, toleranceDegrees, ignoreBranches);
        }

        public void QueueCapture(string poseName, float delaySeconds)
        {
            if (HasUnsavedSettings())
                return;
            pendingName = (poseName ?? "").Trim();
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
                SetLoadedPose(name, store.Load(name));
                Status = "Loaded " + LoadedPose.name + ".";
            }
            catch (Exception error) when (IsFileError(error))
            {
                Status = "Could not load pose. " + error.Message;
            }
        }

        public void SaveMatchSettings()
        {
            if (MatchProfile == null || !MatchProfile.IsDirty)
                return;

            try
            {
                PoseMatchSettings settings = MatchProfile.Export();
                store.SaveMatchSettings(loadedFileName, settings);
                LoadedPose.matchSettings = settings;
                MatchProfile.MarkSaved();
                Status = "Saved matching settings for " + LoadedPose.name + ".";
            }
            catch (Exception error) when (IsFileError(error))
            {
                Status = "Could not save matching settings. " + error.Message;
            }
        }

        public void RevertMatchSettings()
        {
            if (MatchProfile == null || !MatchProfile.IsDirty)
                return;
            MatchProfile = new PoseMatchProfile(LoadedPose);
            Status = "Restored the saved matching settings.";
        }

        private void SetLoadedPose(string fileName, SavedPose pose)
        {
            var profile = new PoseMatchProfile(pose);
            LoadedPose = pose;
            loadedFileName = fileName;
            MatchProfile = profile;
            HidePreview();
        }

        private bool HasUnsavedSettings()
        {
            if (MatchProfile?.IsDirty != true)
                return false;
            Status = "Save or revert your matching edits before changing poses.";
            return true;
        }

        public void SetModelTest(bool enabled)
        {
            if (modelTest == enabled)
                return;

            modelTest = enabled;
            if (modelTest)
            {
                retargeterWasEnabled = avatar.enabled;
                avatar.enabled = false;
                showingDevicePose = false;
                rig.SetTestBend(testBend);
            }
            else
            {
                avatar.enabled = retargeterWasEnabled;
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
            AvatarSnapshot snapshot = LoadedPose?.avatar;
            if (snapshot?.joints == null || snapshot.rig != rig.Id)
            {
                Status = "This pose has no preview for the current avatar rig.";
                return;
            }

            if (preview == null)
            {
                // Keep the clone inactive until its live tracking and animation components are removed.
                preview = new GameObject("Saved pose preview");
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
            AvatarPoseRig.Apply(snapshot, preview.transform.GetChild(0), previewBones);
            Status = "Previewing " + LoadedPose.name + ".";
        }

        public void HidePreview()
        {
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
            if (modelTest && avatar != null)
                SetModelTest(false);
        }

        private void SaveCurrent(string name)
        {
            if (HasUnsavedSettings())
                return;
            if (CurrentFrame.Bones.Count == 0)
            {
                Status = "Nothing to capture. Wait for tracking or enable model test input.";
                return;
            }
            if (!modelTest && CurrentFrame.Mode != TrackingMode.Devices && !HasRetargetedBody())
            {
                Status = "The avatar is still waiting for body tracking. Capture again when it moves.";
                return;
            }

            try
            {
                SavedPose captured = SavedPose.Capture(name, CurrentFrame, rig.Capture());
                store.Save(captured);
                SetLoadedPose(name, captured);
                Status = $"Saved {captured.name} with {captured.joints.Length} joints.";
            }
            catch (Exception error) when (IsFileError(error))
            {
                Status = "Could not save pose. " + error.Message;
            }
        }

        private bool HasRetargetedBody() => avatar.isActiveAndEnabled && avatar.RetargeterValid;

        private static bool IsFileError(Exception error) =>
            error is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException;
    }
}
