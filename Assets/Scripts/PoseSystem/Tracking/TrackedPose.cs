using System;
using System.Collections.Generic;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    public enum TrackingMode { None, FullBody, UpperBody, Devices, ModelTest, External }

    public readonly struct TrackedBone
    {
        public readonly string Id;
        public readonly string ParentId;
        public readonly Pose PlayerPose;
        public readonly string SourcePoint;
        public readonly int MappingRevision;
        public readonly Quaternion LocalRotation;
        public readonly bool HasLocalRotation;
        public readonly bool HasTrackingRotation;

        public TrackedBone(string id, string parentId, Pose playerPose,
            Quaternion localRotation, bool hasLocalRotation = true, bool hasTrackingRotation = true,
            string sourcePoint = null, int mappingRevision = 0)
        {
            Id = id;
            ParentId = parentId;
            PlayerPose = playerPose;
            SourcePoint = sourcePoint;
            MappingRevision = mappingRevision;
            LocalRotation = localRotation;
            HasLocalRotation = hasLocalRotation;
            HasTrackingRotation = hasTrackingRotation;
        }
    }

    public sealed class TrackedPose
    {
        private readonly Dictionary<string, TrackedBone> bones = new(StringComparer.Ordinal);
        public IReadOnlyDictionary<string, TrackedBone> Bones => bones;
        public TrackingMode Mode { get; private set; }

        public void Clear(TrackingMode mode)
        {
            bones.Clear();
            Mode = mode;
        }

        public void Add(TrackedBone bone) => bones[bone.Id] = bone;
        public bool TryGet(string id, out TrackedBone bone) => bones.TryGetValue(id, out bone);

        public bool TryGetPlayerPose(string id, out Pose pose, out bool hasOrientation)
        {
            pose = default;
            hasOrientation = false;
            if (string.IsNullOrEmpty(id) || !TryGet(id, out TrackedBone bone))
                return false;
            pose = bone.PlayerPose;
            hasOrientation = bone.HasTrackingRotation;
            return true;
        }
    }

    [Serializable]
    public sealed class SavedPose
    {
        public int version = 2;
        public string name;
        public PoseJoint[] joints;
        public PoseMatchSettings matchSettings;

        // Store neutral-relative rotations and the player-space anchors used by partial-body previews.
        public static SavedPose Capture(string name, TrackedPose frame)
        {
            var joints = new List<PoseJoint>();
            foreach (TrackedBone bone in frame.Bones.Values)
            {
                if (!bone.HasLocalRotation)
                    continue;

                joints.Add(new PoseJoint
                {
                    id = bone.Id,
                    localRotation = bone.LocalRotation,
                    hasPosition = bone.ParentId == null,
                    position = bone.ParentId == null ? bone.PlayerPose.position : Vector3.zero
                });
            }
            return new SavedPose { name = name, joints = joints.ToArray() };
        }
    }

    [Serializable]
    public struct PoseJoint
    {
        public string id;
        public Quaternion localRotation;
        public bool hasPosition;
        public Vector3 position;
    }

}
