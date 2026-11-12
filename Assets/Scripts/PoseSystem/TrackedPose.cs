using System;
using System.Collections.Generic;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    [Flags]
    public enum PoseBranches { None = 0, LeftHand = 1, RightHand = 2 }

    public enum TrackingMode { None, FullBody, UpperBody, Devices, ModelTest }

    public readonly struct TrackedBone
    {
        public readonly string Id;
        public readonly string ParentId;
        public readonly PoseBranches Branch;
        public readonly Pose TrackingPose;
        public readonly Quaternion LocalRotation;
        public readonly bool HasLocalRotation;

        public TrackedBone(string id, string parentId, PoseBranches branch, Pose trackingPose,
            Quaternion localRotation, bool hasLocalRotation = true)
        {
            Id = id;
            ParentId = parentId;
            Branch = branch;
            TrackingPose = trackingPose;
            LocalRotation = localRotation;
            HasLocalRotation = hasLocalRotation;
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
    }

    [Serializable]
    public sealed class SavedPose
    {
        public int version = 1;
        public string name;
        public TrackingMode mode;
        public PoseJoint[] joints;
        public AvatarSnapshot avatar;

        public static SavedPose Capture(string name, TrackedPose frame, AvatarSnapshot avatar)
        {
            var joints = new List<PoseJoint>();
            foreach (TrackedBone bone in frame.Bones.Values)
            {
                if (!bone.HasLocalRotation)
                    continue;

                joints.Add(new PoseJoint
                {
                    id = bone.Id,
                    parentId = bone.ParentId,
                    branch = bone.Branch,
                    localRotation = bone.LocalRotation
                });
            }
            return new SavedPose { name = name, mode = frame.Mode, joints = joints.ToArray(), avatar = avatar };
        }
    }

    [Serializable]
    public struct PoseJoint
    {
        public string id;
        public string parentId;
        public PoseBranches branch;
        public Quaternion localRotation;
    }

    [Serializable]
    public sealed class AvatarSnapshot
    {
        public string rig;
        public Vector3 rootScale;
        public AvatarJoint[] joints;
    }

    [Serializable]
    public struct AvatarJoint
    {
        public string name;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale;
    }
}
