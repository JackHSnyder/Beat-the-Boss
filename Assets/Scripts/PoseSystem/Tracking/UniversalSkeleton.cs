using System;
using System.Collections.Generic;

namespace BeatTheBoss.Poses
{
    public readonly struct SkeletonBone
    {
        public readonly string Id;
        public readonly string ParentId;
        public readonly PoseCategory Category;

        public SkeletonBone(string id, string parentId, PoseCategory category)
        {
            Id = id;
            ParentId = parentId;
            Category = category;
        }
    }

    public static class UniversalSkeleton
    {
        // Parents precede children. Head and wrists use player space so their rotation
        // checks work even when the input source cannot track the neck or forearms.
        public static IReadOnlyList<SkeletonBone> Bones { get; } = Array.AsReadOnly(new SkeletonBone[]
        {
            new("Hips", null, PoseCategory.LowerBody),
            new("Spine", "Hips", PoseCategory.TorsoAndHead),
            new("Chest", "Spine", PoseCategory.TorsoAndHead),
            new("Neck", "Chest", PoseCategory.TorsoAndHead),
            new("Head", null, PoseCategory.TorsoAndHead),
            new("LeftShoulder", "Chest", PoseCategory.LeftArmWithoutHand),
            new("LeftUpperArm", "LeftShoulder", PoseCategory.LeftArmWithoutHand),
            new("LeftForearm", "LeftUpperArm", PoseCategory.LeftArmWithoutHand),
            new("LeftWrist", null, PoseCategory.LeftHand),
            new("LeftThigh", "Hips", PoseCategory.LowerBody),
            new("LeftShin", "LeftThigh", PoseCategory.LowerBody),
            new("LeftFoot", "LeftShin", PoseCategory.LowerBody),
            new("LeftToes", "LeftFoot", PoseCategory.LowerBody),
            new("LeftThumbMetacarpal", "LeftWrist", PoseCategory.LeftThumb),
            // Keep proximal rotations wrist-relative for existing captures and trackers without the thumb base.
            new("LeftThumbProximal", "LeftWrist", PoseCategory.LeftThumb),
            new("LeftThumbDistal", "LeftThumbProximal", PoseCategory.LeftThumb),
            new("LeftIndexProximal", "LeftWrist", PoseCategory.LeftIndex),
            new("LeftIndexIntermediate", "LeftIndexProximal", PoseCategory.LeftIndex),
            new("LeftIndexDistal", "LeftIndexIntermediate", PoseCategory.LeftIndex),
            new("LeftMiddleProximal", "LeftWrist", PoseCategory.LeftMiddle),
            new("LeftMiddleIntermediate", "LeftMiddleProximal", PoseCategory.LeftMiddle),
            new("LeftMiddleDistal", "LeftMiddleIntermediate", PoseCategory.LeftMiddle),
            new("LeftRingProximal", "LeftWrist", PoseCategory.LeftRing),
            new("LeftRingIntermediate", "LeftRingProximal", PoseCategory.LeftRing),
            new("LeftRingDistal", "LeftRingIntermediate", PoseCategory.LeftRing),
            new("LeftLittleProximal", "LeftWrist", PoseCategory.LeftLittle),
            new("LeftLittleIntermediate", "LeftLittleProximal", PoseCategory.LeftLittle),
            new("LeftLittleDistal", "LeftLittleIntermediate", PoseCategory.LeftLittle),
            new("RightShoulder", "Chest", PoseCategory.RightArmWithoutHand),
            new("RightUpperArm", "RightShoulder", PoseCategory.RightArmWithoutHand),
            new("RightForearm", "RightUpperArm", PoseCategory.RightArmWithoutHand),
            new("RightWrist", null, PoseCategory.RightHand),
            new("RightThigh", "Hips", PoseCategory.LowerBody),
            new("RightShin", "RightThigh", PoseCategory.LowerBody),
            new("RightFoot", "RightShin", PoseCategory.LowerBody),
            new("RightToes", "RightFoot", PoseCategory.LowerBody),
            new("RightThumbMetacarpal", "RightWrist", PoseCategory.RightThumb),
            new("RightThumbProximal", "RightWrist", PoseCategory.RightThumb),
            new("RightThumbDistal", "RightThumbProximal", PoseCategory.RightThumb),
            new("RightIndexProximal", "RightWrist", PoseCategory.RightIndex),
            new("RightIndexIntermediate", "RightIndexProximal", PoseCategory.RightIndex),
            new("RightIndexDistal", "RightIndexIntermediate", PoseCategory.RightIndex),
            new("RightMiddleProximal", "RightWrist", PoseCategory.RightMiddle),
            new("RightMiddleIntermediate", "RightMiddleProximal", PoseCategory.RightMiddle),
            new("RightMiddleDistal", "RightMiddleIntermediate", PoseCategory.RightMiddle),
            new("RightRingProximal", "RightWrist", PoseCategory.RightRing),
            new("RightRingIntermediate", "RightRingProximal", PoseCategory.RightRing),
            new("RightRingDistal", "RightRingIntermediate", PoseCategory.RightRing),
            new("RightLittleProximal", "RightWrist", PoseCategory.RightLittle),
            new("RightLittleIntermediate", "RightLittleProximal", PoseCategory.RightLittle),
            new("RightLittleDistal", "RightLittleIntermediate", PoseCategory.RightLittle),
        });

        private static readonly Dictionary<string, SkeletonBone> ById = BuildIndex();

        public static bool TryGet(string id, out SkeletonBone bone)
        {
            bone = default;
            return id != null && ById.TryGetValue(id, out bone);
        }

        private static Dictionary<string, SkeletonBone> BuildIndex()
        {
            var index = new Dictionary<string, SkeletonBone>(StringComparer.Ordinal);
            foreach (SkeletonBone bone in Bones)
                index.Add(bone.Id, bone);
            return index;
        }
    }
}
