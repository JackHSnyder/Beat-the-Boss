using System;
using System.Collections.Generic;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    internal sealed class MoveVelocityHistory
    {
        private readonly Dictionary<string, BoneHistory> histories = new(StringComparer.Ordinal);
        private TrackingMode mode;

        public MoveVelocityHistory(MoveDefinition definition)
        {
            foreach (MoveStep step in definition.steps)
            {
                if (step.kind != MoveStepKind.ActionSet)
                    continue;
                foreach (MoveAction action in step.actions)
                {
                    if (!histories.TryGetValue(action.boneId, out BoneHistory history))
                        histories.Add(action.boneId, history = new BoneHistory());
                    history.Window = Math.Max(history.Window, action.sampleSeconds);
                }
            }
        }

        public void Sample(TrackedPose frame, double now)
        {
            // A tracking gap or mode change breaks continuity, so it cannot create a false velocity spike.
            bool changedMode = mode != frame.Mode;
            mode = frame.Mode;
            foreach (var entry in histories)
            {
                if (changedMode)
                    entry.Value.Clear();
                if (frame.TryGet(entry.Key, out TrackedBone bone))
                    entry.Value.Add(now, bone);
                else
                    entry.Value.Clear();
            }
        }

        public bool TryMeasure(MoveAction action, double now, out Vector3 velocity) =>
            histories[action.boneId].TryMeasure(action, now, out velocity);

        private sealed class BoneHistory
        {
            private readonly List<SamplePoint> samples = new();
            private int first;
            public double Window;
            private string sourcePoint;
            private int mappingRevision;

            private struct SamplePoint
            {
                public double time;
                public Pose pose;
                public double orientationSince;
                public Vector3 rotationTravel;
            }

            public void Clear()
            {
                samples.Clear();
                first = 0;
            }

            public void Add(double now, TrackedBone bone)
            {
                if (sourcePoint != bone.SourcePoint || mappingRevision != bone.MappingRevision)
                    Clear();
                sourcePoint = bone.SourcePoint;
                mappingRevision = bone.MappingRevision;
                Pose pose = bone.PlayerPose;
                bool orientation = bone.HasTrackingRotation;
                var point = new SamplePoint { time = now, pose = pose,
                    orientationSince = orientation ? now : double.PositiveInfinity };
                if (samples.Count > 0)
                {
                    SamplePoint last = samples[^1];
                    if (last.time >= now)
                        return;
                    if (orientation && !double.IsPositiveInfinity(last.orientationSince))
                    {
                        point.orientationSince = last.orientationSince;
                        // Accumulate signed frame-to-frame rotation in player axes, including across angle wraparound.
                        Quaternion delta = pose.rotation * Quaternion.Inverse(last.pose.rotation);
                        delta.ToAngleAxis(out float angle, out Vector3 axis);
                        if (angle > 180)
                            angle -= 360;
                        point.rotationTravel = last.rotationTravel + axis * angle;
                    }
                }
                samples.Add(point);
                while (first + 1 < samples.Count && samples[first + 1].time <= now - Window)
                    first++;
                if (first >= 64 && first >= samples.Count / 2)
                {
                    samples.RemoveRange(0, first);
                    first = 0;
                    // Keep the cumulative angles bounded over long play sessions.
                    Vector3 origin = samples[0].rotationTravel;
                    for (int i = 0; i < samples.Count; i++)
                    {
                        SamplePoint sample = samples[i];
                        sample.rotationTravel -= origin;
                        samples[i] = sample;
                    }
                }
            }

            public bool TryMeasure(MoveAction action, double now, out Vector3 velocity)
            {
                velocity = default;
                double start = now - action.sampleSeconds;
                if (samples.Count - first < 2 || samples[first].time > start)
                    return false;
                SamplePoint end = samples[^1];
                bool rotation = action.kind == MoveVelocityKind.Rotation;
                if (rotation && end.orientationSince > start)
                    return false;

                // Find the two samples around the exact start time so frame rate does not change the averaging window.
                int low = first;
                int high = samples.Count - 1;
                while (high - low > 1)
                {
                    int mid = low + (high - low) / 2;
                    if (samples[mid].time <= start)
                        low = mid;
                    else
                        high = mid;
                }
                SamplePoint before = samples[low];
                SamplePoint after = samples[high];
                float fraction = (float)((start - before.time) / (after.time - before.time));
                Vector3 from = rotation ? before.rotationTravel : before.pose.position;
                Vector3 to = rotation ? after.rotationTravel : after.pose.position;
                Vector3 latest = rotation ? end.rotationTravel : end.pose.position;
                velocity = (latest - Vector3.Lerp(from, to, fraction)) / action.sampleSeconds;
                return true;
            }
        }
    }
}
