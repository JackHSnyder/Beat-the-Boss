// Manages move drafts, saved move files, and test runs in the recording scene.
// MovePanel calls it to edit and test moves. PoseWorkbench supplies the current tracking or model-test frame.

using System;
using System.IO;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    [DefaultExecutionOrder(225)]
    [RequireComponent(typeof(PoseWorkbench))]
    public sealed class MoveWorkbench : MonoBehaviour
    {
        private PoseStore poses;
        private PoseWorkbench workbench;
        private string loadedName, savedState;
        public string LoadedName => loadedName;
        public bool IsReadOnly => BuiltInContent.IsProtected("Moves", "move-", loadedName);
        public bool IsDirty => Draft != null && (savedState == null || JsonUtility.ToJson(Draft) != savedState);
        public void DiscardChanges()
        {
            StopTest();
            Draft = savedState == null ? null : JsonUtility.FromJson<MoveDefinition>(savedState);
            Status = "Unsaved move changes discarded.";
        }
        public bool DeleteSaved()
        {
            if (loadedName == null) return false;
            try
            {
                MoveStore.Delete(loadedName);
                StopTest(); Draft = null; loadedName = savedState = null;
                Status = "Move deleted."; return true;
            }
            catch (Exception error) when (IsFileError(error)) { Status = error.Message; return false; }
        }

        public MoveDefinition Draft { get; private set; }
        public MoveDetector Detector { get; private set; }
        public string Status { get; private set; } = "";
        public MoveOutcome LastOutcome { get; private set; }
        public int Passes { get; private set; }
        public int Timeouts { get; private set; }
        public event Action<MoveOutcome> MoveFinished;
        internal bool HasAnimation => Draft?.animation?.HasFrames == true ||
            !string.IsNullOrWhiteSpace(Draft?.animationName);

        private void Awake()
        {
            workbench = GetComponent<PoseWorkbench>();
            poses = new PoseStore(workbench.PoseDirectory);
        }

        // Consume the frame after the pose workbench has chosen live tracking or model test input.
        private void LateUpdate()
        {
            if (!workbench.isActiveAndEnabled)
                return;
            double now = Time.unscaledTimeAsDouble;
            if (Detector == null)
                return;
            MoveOutcome outcome = Detector.Tick(workbench.CurrentFrame, now,
                workbench.ToleranceDegrees, workbench.SessionIgnoredCategories);
            if (outcome == MoveOutcome.None)
                return;
            LastOutcome = outcome;
            if (outcome == MoveOutcome.Passed)
                Passes++;
            else
                Timeouts++;
            MoveFinished?.Invoke(outcome);
        }

        private void OnDisable()
        {
            StopTest();
        }

        public void NewMove()
        {
            StopTest();
            Draft = new MoveDefinition();
            loadedName = savedState = null;
            Status = "Add a pose or action set to begin.";
        }

        public string[] ListMoves()
        {
            try { return MoveStore.List(); }
            catch (Exception error) when (IsFileError(error))
            {
                Status = error.Message;
                return Array.Empty<string>();
            }
        }

        public void Load(string name)
        {
            try
            {
                MoveDefinition definition = MoveStore.Load(name, out bool legacy);
                StopTest();
                Draft = definition;
                loadedName = name;
                savedState = JsonUtility.ToJson(Draft);
                Status = legacy ? "Loaded " + name + ". Review its actions and recapture any old pose steps." :
                    "Loaded " + name + ".";
            }
            catch (Exception error) when (IsFileError(error)) { Status = error.Message; }
        }

        public bool Save()
        {
            try
            {
                if (Draft == null)
                    return false;
                Draft.name = Draft.name?.Trim();
                Draft.Validate();
                Draft.animation?.Validate();
                foreach (MoveStep step in Draft.steps)
                    if (step.kind == MoveStepKind.Pose)
                        poses.Load(step.poseName);
                SavedFileNames.Write("move", loadedName, Draft.name, MoveStore.PathFor, Draft);
                loadedName = Draft.name;
                savedState = JsonUtility.ToJson(Draft);
                Status = "Saved " + Draft.name + ".";
                return true;
            }
            catch (Exception error) when (IsFileError(error))
            {
                Status = error.Message;
                return false;
            }
        }

        public void StartTest()
        {
            StopTest();
            if (Draft == null)
                return;
            try
            {
                Detector = new MoveDetector(Draft, poses.Load);
                Status = "Testing " + Draft.name + ".";
            }
            catch (Exception error) when (IsFileError(error)) { Status = error.Message; }
        }

        public void StopTest()
        {
            Detector = null;
            Passes = Timeouts = 0;
            LastOutcome = MoveOutcome.None;
            Status = "Detection stopped.";
        }

        internal string[] ListAnimations()
        {
            try { return AnimationStore.List(); }
            catch (Exception error) when (IsFileError(error))
            {
                Status = error.Message;
                return Array.Empty<string>();
            }
        }

        internal void LinkAnimation(string name)
        {
            try
            {
                AnimationStore.Load(name);
                Draft.animationName = name;
                Draft.animation = null;
                Status = $"Linked {name}. Save the move to keep the link.";
            }
            catch (Exception error) when (IsFileError(error)) { Status = error.Message; }
        }

        internal MoveAnimation GetAnimation()
        {
            try { return MoveStore.LoadAnimation(Draft); }
            catch (Exception error) when (IsFileError(error))
            {
                Status = $"Could not open animation '{Draft.animationName}'. {error.Message}";
                return null;
            }
        }

        internal void RemoveAnimation()
        {
            Draft.animationName = null;
            Draft.animation = null;
            Status = "Animation removed from the draft.";
        }

        private static bool IsFileError(Exception error) =>
            error is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException;
    }
}
