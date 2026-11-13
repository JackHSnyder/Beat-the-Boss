using System;
using System.Collections.Generic;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    // Adapters submit stable, source-qualified IDs and poses in player-local space.
    // The neutral orientation uses the same axes as the live sample.
    public interface ITrackingPointSource
    {
        void ReadTrackingPoints(Transform playerSpace, TrackingPointBuffer points);
    }

    public readonly struct TrackingPoint
    {
        public readonly Pose Pose;
        public readonly Quaternion NeutralRotation;
        public readonly bool HasOrientation;

        public TrackingPoint(Pose pose, Quaternion neutralRotation, bool hasOrientation)
        {
            Pose = pose;
            NeutralRotation = neutralRotation;
            HasOrientation = hasOrientation;
        }
    }

    public sealed class TrackingPointBuffer
    {
        private readonly Dictionary<string, TrackingPoint> points = new(StringComparer.Ordinal);
        public IReadOnlyDictionary<string, TrackingPoint> Points => points;
        internal void Clear() => points.Clear();

        public void Add(string id, Pose playerPose, Quaternion neutralRotation, bool hasOrientation = true)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Tracking points need a stable source ID.", nameof(id));
            if (!float.IsFinite(playerPose.position.x) || !float.IsFinite(playerPose.position.y) ||
                !float.IsFinite(playerPose.position.z))
                return;
            hasOrientation &= ValidRotation(playerPose.rotation) && ValidRotation(neutralRotation);
            if (hasOrientation)
            {
                playerPose.rotation = Quaternion.Normalize(playerPose.rotation);
                neutralRotation = Quaternion.Normalize(neutralRotation);
            }
            points[id] = new TrackingPoint(playerPose, neutralRotation, hasOrientation);
        }

        internal static bool ValidRotation(Quaternion rotation)
        {
            float length = Quaternion.Dot(rotation, rotation);
            return float.IsFinite(length) && length > 0.000001f;
        }
    }
}
