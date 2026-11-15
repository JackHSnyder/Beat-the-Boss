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
        private string loadedName;

        public MoveDefinition Draft { get; private set; }
        public MoveDetector Detector { get; private set; }
        public string Status { get; private set; } = "";
        public MoveOutcome LastOutcome { get; private set; }
        public int Passes { get; private set; }
        public int Timeouts { get; private set; }
        public event Action<MoveOutcome> MoveFinished;

        private void Awake()
        {
            workbench = GetComponent<PoseWorkbench>();
            poses = new PoseStore(workbench.PoseDirectory);
        }

        // Consume the frame after the pose workbench has chosen live tracking or model test input.
        private void LateUpdate()
        {
            if (Detector == null || !workbench.isActiveAndEnabled)
                return;
            MoveOutcome outcome = Detector.Tick(workbench.CurrentFrame, Time.unscaledTimeAsDouble,
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

        private void OnDisable() => StopTest();

        public void NewMove()
        {
            StopTest();
            Draft = new MoveDefinition();
            loadedName = null;
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
                foreach (MoveStep step in Draft.steps)
                    if (step.kind == MoveStepKind.Pose)
                        poses.Load(step.poseName);
                string path = MoveStore.PathFor(Draft.name);
                // Only overwrite the file associated with this draft. A different name goes through the new-file path.
                bool replace = string.Equals(loadedName, Draft.name, StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(path);
                PoseStore.WriteJson(path, Draft, replace);
                loadedName = Draft.name;
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

        private static bool IsFileError(Exception error) =>
            error is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException;
    }
}
