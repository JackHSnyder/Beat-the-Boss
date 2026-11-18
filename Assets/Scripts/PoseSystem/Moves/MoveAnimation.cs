using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    [Serializable]
    public sealed class MoveAnimation
    {
        internal const double SampleInterval = 1d / 30d;

        public MoveAnimationFrame[] frames = Array.Empty<MoveAnimationFrame>();
        public List<PoseCategory> disabledCategories = new();
        public List<string> excludedBones = new();
        [SerializeField] private float playbackStart;
        // Zero means the full remaining take, including in saves made before trimming was added.
        [SerializeField] private float playbackEnd;

        public bool HasFrames => frames != null && frames.Length >= 2;
        public float Duration => HasFrames ? frames[^1].time : 0f;
        public float PlaybackStart => playbackStart;
        public float PlaybackEnd => playbackEnd == 0f ? Duration : playbackEnd;

        internal bool IncludesBone(string id) => excludedBones?.Contains(id) != true &&
            (disabledCategories == null || !UniversalSkeleton.TryGet(id, out SkeletonBone bone) ||
            !PoseMatchProfile.IsSessionIgnored(bone.Category, disabledCategories));

        internal bool HasEnabledSamples()
        {
            for (int i = 0; i < frames.Length; i++)
            {
                // Include the neighboring samples used to interpolate a trim boundary.
                if (i + 1 < frames.Length && frames[i + 1].time <= PlaybackStart)
                    continue;
                if (i > 0 && frames[i - 1].time >= PlaybackEnd)
                    break;
                foreach (PoseJoint joint in frames[i].joints)
                    if (IncludesBone(joint.id))
                        return true;
            }
            return false;
        }

        internal void SetPlaybackBoundary(bool start, float time)
        {
            // Keep at least one recording interval, or the entire take if it is shorter.
            float minimumLength = Mathf.Min(Duration, (float)SampleInterval);
            if (start)
                playbackStart = Mathf.Clamp(time, 0f, Mathf.Max(0f, PlaybackEnd - minimumLength));
            else
                playbackEnd = Mathf.Clamp(time, Mathf.Min(Duration, playbackStart + minimumLength), Duration);
        }

        internal void ResetPlaybackRange()
        {
            playbackStart = playbackEnd = 0f;
        }

        internal void Validate()
        {
            foreach (string id in excludedBones ?? (IEnumerable<string>)Array.Empty<string>())
                if (!UniversalSkeleton.TryGet(id, out _))
                    throw new InvalidDataException("The animation contains an unknown excluded bone.");

            foreach (PoseCategory category in disabledCategories ?? (IEnumerable<PoseCategory>)Array.Empty<PoseCategory>())
                if ((uint)category >= PoseMatchProfile.CategoryCount)
                    throw new InvalidDataException("The move animation contains an unknown body part.");

            if (frames == null || frames.Length == 0)
                return;
            if (!HasFrames || frames[0].time != 0f)
                throw new InvalidDataException("A move animation needs at least two frames starting at zero.");

            var ids = new HashSet<string>(StringComparer.Ordinal);
            float previousTime = -1f;
            foreach (MoveAnimationFrame frame in frames)
            {
                if (!float.IsFinite(frame.time) || frame.time <= previousTime || frame.joints == null || frame.joints.Length == 0)
                    throw new InvalidDataException("The move animation contains an invalid frame.");
                previousTime = frame.time;
                ids.Clear();
                foreach (PoseJoint joint in frame.joints)
                {
                    Quaternion rotation = joint.localRotation;
                    float lengthSquared = Quaternion.Dot(rotation, rotation);
                    Vector3 position = joint.position;
                    if (!UniversalSkeleton.TryGet(joint.id, out _) || !ids.Add(joint.id) ||
                        !float.IsFinite(lengthSquared) || lengthSquared < 0.000001f ||
                        (joint.hasPosition && (!float.IsFinite(position.x) || !float.IsFinite(position.y) || !float.IsFinite(position.z))))
                        throw new InvalidDataException("The move animation contains an invalid joint sample.");
                }
            }

            if (!float.IsFinite(playbackStart) || !float.IsFinite(playbackEnd) ||
                PlaybackStart < 0f || PlaybackEnd > Duration || PlaybackStart >= PlaybackEnd)
                throw new InvalidDataException("The move animation contains an invalid playback range.");
        }

        internal void Sample(float time, List<PoseJoint> result, Dictionary<string, PoseJoint> nextJoints)
        {
            result.Clear();
            nextJoints.Clear();
            if (!HasFrames)
                return;
            if (time <= 0f || time >= Duration)
            {
                result.AddRange(time <= 0f ? frames[0].joints : frames[^1].joints);
                return;
            }

            int lo = 0, hi = frames.Length - 1;
            while (hi - lo > 1)
            {
                int middle = (lo + hi) / 2;
                if (frames[middle].time <= time)
                    lo = middle;
                else
                    hi = middle;
            }

            MoveAnimationFrame from = frames[lo], to = frames[hi];
            float blend = (time - from.time) / (to.time - from.time);
            foreach (PoseJoint joint in to.joints)
                nextJoints.Add(joint.id, joint);
            foreach (PoseJoint joint in from.joints)
            {
                if (!nextJoints.Remove(joint.id, out PoseJoint next))
                {
                    result.Add(joint);
                    continue;
                }
                PoseJoint sample = joint;
                sample.localRotation = Quaternion.Slerp(joint.localRotation, next.localRotation, blend);
                sample.hasPosition = joint.hasPosition || next.hasPosition;
                sample.position = joint.hasPosition && next.hasPosition
                    ? Vector3.Lerp(joint.position, next.position, blend) : joint.hasPosition ? joint.position : next.position;
                result.Add(sample);
            }
            result.AddRange(nextJoints.Values);
        }
    }

    [Serializable]
    public struct MoveAnimationFrame
    {
        public float time;
        public PoseJoint[] joints;
    }
}
