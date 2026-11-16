// Loads configured moves and checks them against the live frame from PlayerPoseTracker.
// The Move Detection prefab uses this component to notify gameplay scripts when a move passes.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Events;

namespace BeatTheBoss.Poses
{
    [DefaultExecutionOrder(225)]
    public sealed class MoveDetection : MonoBehaviour
    {
        [Tooltip("Optional when the scene contains exactly one active player tracker.")]
        [SerializeField] private PlayerPoseTracker tracker;
        [Tooltip("Watch only the assignments made in the main menu Loadout.")]
        [SerializeField] private bool useLoadout;
        [Tooltip("Names of moves saved on this device through the recording tools.")]
        [SerializeField] private string[] savedMoveNames = Array.Empty<string>();
        [Tooltip("Move JSON files included in the project for use in a build.")]
        [SerializeField] private TextAsset[] bundledMoves = Array.Empty<TextAsset>();
        [Tooltip("Pose JSON files referenced by the bundled moves.")]
        [SerializeField] private TextAsset[] bundledPoses = Array.Empty<TextAsset>();
        [SerializeField, Range(0, 180)] private float fallbackToleranceDegrees = 15;
        [SerializeField] private UnityEvent<string> onMovePerformed = new();

        private PlayerPoseTracker activeTracker;
        private List<(string Name, MoveDetector Detector, MoveSlot? Slot)> detectors;

        /// <summary>Raised with the saved move name each time its full sequence passes.</summary>
        public event Action<string> MovePerformed;

        /// <summary>Raised with the loadout slot and move name when its sequence passes.</summary>
        public event Action<MoveSlot, string> LoadoutMovePerformed;

        private void Start() => BeginDetection();

        private void OnEnable()
        {
            // Start handles the first setup after the scene has initialized. Re-enabling starts fresh.
            if (detectors != null)
                BeginDetection();
        }

        private void BeginDetection()
        {
            detectors = new List<(string, MoveDetector, MoveSlot?)>();
            activeTracker = tracker;
            if (activeTracker == null)
            {
                var trackers = FindObjectsByType<PlayerPoseTracker>();
                if (trackers.Length != 1)
                {
                    Debug.LogWarning("Move Detection needs a player tracker. Assign one if the scene has multiple players.", this);
                    return;
                }
                activeTracker = trackers[0];
            }

            var devicePoses = new PoseStore(Path.Combine(Application.persistentDataPath, "Poses"));
            if (useLoadout)
            {
                foreach (MoveSlot slot in MoveLoadout.Slots)
                {
                    string name = MoveLoadout.Get(slot);
                    if (!string.IsNullOrEmpty(name))
                        TryAddMove(name, () => MoveStore.Load(name, out _), devicePoses.Load, slot);
                }
                return;
            }

            foreach (string name in savedMoveNames)
                TryAddMove(name, () => MoveStore.Load(name, out _), devicePoses.Load);

            // Index bundled poses by their saved names, which is how the move steps reference them.
            var posesByName = new Dictionary<string, SavedPose>(StringComparer.OrdinalIgnoreCase);
            foreach (TextAsset asset in bundledPoses)
            {
                if (asset == null)
                    continue;
                try
                {
                    SavedPose pose = PoseStore.ReadJson(asset.text);
                    posesByName.Add(pose.name, pose);
                }
                catch (Exception error) when (IsDataError(error))
                {
                    Debug.LogWarning($"Could not load bundled pose '{asset.name}'. {error.Message}", this);
                }
            }

            foreach (TextAsset asset in bundledMoves)
            {
                if (asset != null)
                    TryAddMove(asset.name, () => MoveStore.ReadJson(asset.text, out _),
                        name => posesByName.TryGetValue(name, out SavedPose pose) ? pose : null);
            }
        }

        private void TryAddMove(string label, Func<MoveDefinition> loadMove, Func<string, SavedPose> loadPose,
            MoveSlot? slot = null)
        {
            try
            {
                MoveDefinition move = loadMove();
                if (string.IsNullOrWhiteSpace(move.name))
                    throw new InvalidDataException("The move has no name.");
                if (slot == null && detectors.Exists(entry =>
                    string.Equals(entry.Name, move.name, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("A move with this name is already configured.");
                detectors.Add((move.name, new MoveDetector(move, loadPose), slot));
            }
            catch (Exception error) when (IsDataError(error))
            {
                Debug.LogWarning($"Could not watch move '{label}'. {error.Message}", this);
            }
        }

        private void LateUpdate()
        {
            if (activeTracker == null || !activeTracker.isActiveAndEnabled)
                return;

            double now = Time.unscaledTimeAsDouble;
            var activeDetectors = detectors;
            foreach (var entry in activeDetectors)
            {
                if (entry.Detector.Tick(activeTracker.Frame, now, fallbackToleranceDegrees) != MoveOutcome.Passed)
                    continue;

                MovePerformed?.Invoke(entry.Name);
                onMovePerformed.Invoke(entry.Name);
                if (entry.Slot is MoveSlot slot)
                    LoadoutMovePerformed?.Invoke(slot, entry.Name);
                // A listener may disable or restart this component while handling the move.
                if (!isActiveAndEnabled || detectors != activeDetectors)
                    return;
            }
        }

        private static bool IsDataError(Exception error) =>
            error is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException;
    }
}
