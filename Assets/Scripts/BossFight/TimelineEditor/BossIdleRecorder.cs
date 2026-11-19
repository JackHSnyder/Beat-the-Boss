using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    // Saves a standalone idle animation to the animation library, without requiring a detection gesture.
    [DefaultExecutionOrder(300)]
    public sealed class BossIdleRecorder : MonoBehaviour
    {
        public PlayerPoseTracker tracker;
        public string recordingName = "Boss Idle";
        [Min(0)] public float countdown = 3;
        [Min(.1f)] public float duration = 8;
        public List<PoseCategory> disabledCategories = new() { PoseCategory.LowerBody };
        public string Status { get; private set; } = "Ready";
        public bool IsRecording { get; private set; }
        private readonly List<MoveAnimationFrame> frames = new();
        private double startsAt, firstAt, nextAt;

        [ContextMenu("Record boss idle (Play mode)")]
        public void Record()
        {
            if (!Application.isPlaying) { Status = "Enter Play mode to record."; return; }
            if (tracker == null) foreach (PlayerPoseTracker t in FindObjectsByType<PlayerPoseTracker>())
                if (t.gameObject.scene == gameObject.scene) { tracker = t; break; }
            if (tracker == null) { Status = "No player tracker in this scene."; return; }
            frames.Clear(); startsAt = Time.timeAsDouble + countdown; nextAt = startsAt;
            IsRecording = true; Status = "Get ready...";
        }
        private void LateUpdate()
        {
            if (!IsRecording || Time.timeAsDouble < nextAt) return;
            double now = Time.timeAsDouble;
            PoseJoint[] joints = SavedPose.CaptureJoints(tracker.Frame);
            if (joints.Length == 0) { Status = "Waiting for tracking..."; return; }
            if (frames.Count == 0) firstAt = now;
            frames.Add(new MoveAnimationFrame { time = (float)(now - firstAt), joints = joints });
            nextAt = now + MoveAnimation.SampleInterval;
            Status = $"Recording {now - firstAt:0.0}s";
            if (now - firstAt >= duration) Save();
        }
        [ContextMenu("Stop and save idle")]
        public void Save()
        {
            if (!IsRecording) return;
            IsRecording = false;
            if (frames.Count < 2) { Status = "Not enough tracked frames; previous recording retained."; return; }
            var animation = new MoveAnimation { frames = frames.ToArray(), disabledCategories = new List<PoseCategory>(disabledCategories) };
            try
            {
                animation.Validate();
                string path = AnimationStore.PathFor(recordingName);
                AnimationStore.Save(recordingName, animation, replace: true);
                Status = "Saved " + recordingName; Debug.Log(Status + " to " + path, this);
            }
            catch (System.Exception e) when (e is IOException or System.UnauthorizedAccessException or System.ArgumentException)
            { Status = "Could not save: " + e.Message; Debug.LogWarning(Status, this); }
        }
        public void Cancel() { IsRecording = false; Status = "Cancelled; previous recording retained."; }
        private void OnDisable() => Cancel();
    }
}
