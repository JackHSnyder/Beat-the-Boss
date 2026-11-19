using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    [DefaultExecutionOrder(225)]
    [RequireComponent(typeof(PoseWorkbench))]
    internal sealed class AnimationWorkbench : MonoBehaviour
    {
        private PoseWorkbench poses;
        private string loadedName, savedState;
        private bool newDraft;
        internal string LoadedName => loadedName;
        internal bool IsReadOnly => BuiltInContent.IsProtected("Animations", "animation-", loadedName);
        internal bool IsDirty => IsRecording || (savedState != null
            ? Name != loadedName || JsonUtility.ToJson(Draft) != savedState
            : newDraft || Draft != null || Name != "New animation");
        internal void DiscardChanges()
        {
            string name = loadedName, json = savedState;
            SetDraft(name ?? "New animation", json == null ? null : JsonUtility.FromJson<MoveAnimation>(json), name);
            newDraft = false;
            Status = "Unsaved animation changes discarded.";
        }
        internal bool DeleteSaved()
        {
            if (loadedName == null) return false;
            try
            {
                AnimationStore.Delete(loadedName);
                SetDraft("New animation", null, null); newDraft = false;
                Status = "Animation deleted."; return true;
            }
            catch (Exception error) when (AnimationStore.IsFileError(error)) { Status = error.Message; return false; }
        }
        private readonly List<string> availableBones = new();
        private List<MoveAnimationFrame> recording;
        private double recordAfter;
        private double firstSampleAt;
        private double nextSampleAt;
        private MoveAnimation preview;
        private double previewStartedAt;
        private float previewTime;
        private bool previewPlaying;
        private readonly List<PoseJoint> previewSample = new();
        private readonly Dictionary<string, PoseJoint> nextJoints = new(StringComparer.Ordinal);

        internal string Name { get; set; } = "New animation";
        internal MoveAnimation Draft { get; private set; }
        internal IReadOnlyList<string> AvailableBones => availableBones;
        internal bool IsRecording => recording != null;
        internal bool PreviewVisible => poses.AnimationPreviewVisible;
        internal string Status { get; private set; } = "";
        internal string PreviewStatus { get; private set; }

        internal string Feedback
        {
            get
            {
                if (!IsRecording)
                    return PreviewStatus ?? Status;
                double now = Time.unscaledTimeAsDouble;
                if (now < recordAfter)
                    return $"Recording starts in {Math.Ceiling(recordAfter - now):0}s";
                return recording.Count == 0 ? "Waiting for tracking…" : $"Recording  ·  {now - firstSampleAt:0.0}s";
            }
        }

        private void Awake() => poses = GetComponent<PoseWorkbench>();

        private void LateUpdate()
        {
            if (!poses.isActiveAndEnabled)
                return;
            double now = Time.unscaledTimeAsDouble;
            if (IsRecording)
                Capture(now);
            if (!previewPlaying || !PreviewVisible)
                return;

            float time = Mathf.Min(preview.PlaybackStart + (float)(now - previewStartedAt), preview.PlaybackEnd);
            if (time != previewTime)
                ApplyPreview(time);
            if (time >= preview.PlaybackEnd)
            {
                previewPlaying = false;
                PreviewStatus = "Preview finished.";
            }
        }

        private void OnDisable() => LeaveEditor();

        internal void LeaveEditor()
        {
            CancelRecording();
            HidePreview();
        }

        internal void New()
        {
            SetDraft("New animation", null, null);
            Status = "Record a take using the current capture delay.";
        }

        internal void CopyEmbedded(string moveName, MoveAnimation animation)
        {
            var copy = JsonUtility.FromJson<MoveAnimation>(JsonUtility.ToJson(animation));
            SetDraft(moveName, copy, null);
            Status = "Copied the take. Name and save it to add it to the library.";
        }

        internal string[] ListSaved()
        {
            try { return AnimationStore.List(); }
            catch (Exception error) when (AnimationStore.IsFileError(error))
            {
                Status = error.Message;
                return Array.Empty<string>();
            }
        }

        internal void Load(string name)
        {
            try
            {
                SetDraft(name, AnimationStore.Load(name), name);
                Status = $"Loaded {name}.";
            }
            catch (Exception error) when (AnimationStore.IsFileError(error)) { Status = error.Message; }
        }

        internal void Save()
        {
            PreviewStatus = null;
            try
            {
                string name = Name.Trim();
                AnimationStore.SaveDraft(name, Draft, loadedName);
                loadedName = Name = name;
                savedState = JsonUtility.ToJson(Draft); newDraft = false;
                Status = $"Saved {name}.";
            }
            catch (Exception error) when (AnimationStore.IsFileError(error)) { Status = error.Message; }
        }

        private void SetDraft(string name, MoveAnimation animation, string sourceName)
        {
            LeaveEditor();
            Name = name;
            Draft = animation;
            loadedName = sourceName;
            RefreshBones();
            if (Draft?.disabledCategories?.Count > 0)
            {
                var excluded = new List<string>();
                foreach (SkeletonBone bone in UniversalSkeleton.Bones)
                    if (!Draft.IncludesBone(bone.Id))
                        excluded.Add(bone.Id);
                Draft.excludedBones = excluded;
                Draft.disabledCategories.Clear();
            }
            newDraft = sourceName == null;
            savedState = sourceName == null ? null : JsonUtility.ToJson(Draft);
        }

        internal void StartRecording()
        {
            if (IsRecording || !poses.isActiveAndEnabled)
                return;
            HidePreview();
            poses.HidePreview();
            recording = new List<MoveAnimationFrame>();
            recordAfter = Time.unscaledTimeAsDouble + poses.CaptureDelay;
            nextSampleAt = recordAfter;
        }

        private void Capture(double now)
        {
            if (now < nextSampleAt)
                return;
            PoseJoint[] joints = SavedPose.CaptureJoints(poses.CurrentFrame);
            if (joints.Length == 0)
                return;
            if (recording.Count == 0)
                firstSampleAt = now;
            double elapsed = now - firstSampleAt;
            recording.Add(new MoveAnimationFrame { time = (float)elapsed, joints = joints });
            double interval = MoveAnimation.SampleInterval;
            nextSampleAt = Math.Max(nextSampleAt + interval,
                firstSampleAt + (Math.Floor(elapsed / interval) + 1) * interval);
        }

        internal void StopRecording()
        {
            if (!IsRecording)
                return;
            if (recording.Count >= 2)
            {
                Draft = new MoveAnimation { frames = recording.ToArray() };
                RefreshBones();
                Status = $"Recorded {Draft.Duration:0.00}s.";
            }
            else
                Status = "Record at least two tracked frames to keep a take.";
            recording = null;
        }

        internal void CancelRecording()
        {
            if (!IsRecording)
                return;
            recording = null;
            Status = "Recording cancelled.";
        }

        private void RefreshBones()
        {
            availableBones.Clear();
            if (Draft?.HasFrames != true)
                return;
            var captured = new HashSet<string>(StringComparer.Ordinal);
            foreach (MoveAnimationFrame frame in Draft.frames)
                foreach (PoseJoint joint in frame.joints)
                    captured.Add(joint.id);
            foreach (SkeletonBone bone in UniversalSkeleton.Bones)
                if (captured.Contains(bone.Id))
                    availableBones.Add(bone.Id);
        }

        internal void ToggleBone(string id)
        {
            var excluded = Draft.excludedBones ??= new List<string>();
            if (!excluded.Remove(id))
                excluded.Add(id);
            RefreshPreview();
        }

        internal void SetAllBones(bool include)
        {
            var excluded = new List<string>();
            if (!include)
                foreach (SkeletonBone bone in UniversalSkeleton.Bones)
                    excluded.Add(bone.Id);
            Draft.excludedBones = excluded;
            RefreshPreview();
        }

        private void RefreshPreview()
        {
            if (preview == Draft && PreviewVisible)
                ApplyPreview(previewTime);
        }

        internal void SetBoundary(bool start, float time)
        {
            Draft.SetPlaybackBoundary(start, time);
            Scrub(start ? Draft.PlaybackStart : Draft.PlaybackEnd);
        }

        internal void ResetRange()
        {
            Draft.ResetPlaybackRange();
            Scrub(Draft.PlaybackStart);
        }

        private void Scrub(float time)
        {
            previewPlaying = false;
            if (EnsurePreview(Draft))
            {
                ApplyPreview(time);
                PreviewStatus = "Save to keep the playback range.";
            }
        }

        internal void Play(MoveAnimation animation)
        {
            if (IsRecording || animation?.HasFrames != true)
                return;
            if (!animation.HasEnabledSamples())
            {
                PreviewStatus = "Include a bone tracked during the selected playback range.";
                return;
            }
            if (!EnsurePreview(animation))
                return;
            ApplyPreview(animation.PlaybackStart);
            previewStartedAt = Time.unscaledTimeAsDouble;
            previewPlaying = true;
            PreviewStatus = "Playing animation.";
        }

        private bool EnsurePreview(MoveAnimation animation)
        {
            if ((preview == animation && PreviewVisible) || poses.ShowAnimationPreview(animation))
            {
                preview = animation;
                return true;
            }
            HidePreview();
            PreviewStatus = "Could not show the avatar preview.";
            return false;
        }

        private void ApplyPreview(float time)
        {
            preview.Sample(time, previewSample, nextJoints);
            poses.ApplyAnimationPreview(previewSample, preview);
            previewTime = time;
        }

        internal void HidePreview()
        {
            previewPlaying = false;
            preview = null;
            PreviewStatus = null;
            poses.HideAnimationPreview();
        }
    }
}
