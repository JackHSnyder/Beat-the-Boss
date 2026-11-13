using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    public enum PoseCategory
    {
        All, LowerBody, UpperBody, LeftArm, RightArm, LeftHand, RightHand,
        LeftThumb, LeftIndex, LeftMiddle, LeftRing, LeftLittle,
        RightThumb, RightIndex, RightMiddle, RightRing, RightLittle,
        // Append categories to keep the numeric IDs in existing pose files stable.
        TorsoAndHead, LeftArmWithoutHand, RightArmWithoutHand
    }

    [Serializable]
    public sealed class PoseMatchSettings
    {
        public PoseCategorySetting[] categories;
        public PoseBoneSetting[] bones;
        public PoseConstraint[] constraints;
    }

    [Serializable]
    public struct PoseCategorySetting
    {
        public PoseCategory category;
        public bool disabled;
        public bool overrideTolerance;
        public float toleranceDegrees;
        public bool useBudget;
        public float budgetDegrees;
    }

    [Serializable]
    public struct PoseBoneSetting
    {
        public string id;
        public bool disabled;
        public bool overrideTolerance;
        public float toleranceDegrees;
    }

    public sealed class PoseMatchProfile
    {
        internal const int CategoryCount = (int)PoseCategory.RightArmWithoutHand + 1;
        private static readonly PoseCategory[] Parents =
        {
            PoseCategory.All, PoseCategory.All, PoseCategory.All,
            PoseCategory.UpperBody, PoseCategory.UpperBody, PoseCategory.LeftArm, PoseCategory.RightArm,
            PoseCategory.LeftHand, PoseCategory.LeftHand, PoseCategory.LeftHand, PoseCategory.LeftHand, PoseCategory.LeftHand,
            PoseCategory.RightHand, PoseCategory.RightHand, PoseCategory.RightHand, PoseCategory.RightHand, PoseCategory.RightHand,
            PoseCategory.UpperBody, PoseCategory.LeftArm, PoseCategory.RightArm
        };
        private static readonly string[] Labels =
        {
            "Whole pose", "Lower body", "Upper body", "Left arm", "Right arm", "Left hand", "Right hand",
            "Left thumb", "Left index", "Left middle", "Left ring", "Left little",
            "Right thumb", "Right index", "Right middle", "Right ring", "Right little",
            "Torso and head", "Left arm without hand", "Right arm without hand"
        };

        private readonly PoseCategorySetting[] categories = new PoseCategorySetting[CategoryCount];
        private readonly PoseBoneSetting[] bones;
        private readonly PoseCategory[] boneCategories;
        private readonly int[] categoryBoneCounts = new int[CategoryCount];
        private readonly List<PoseConstraint> constraints;
        private readonly List<PoseConstraintResult> constraintResults = new();

        internal readonly float[] Excess = new float[CategoryCount];
        internal readonly bool[] CategoryFailed = new bool[CategoryCount];
        internal readonly float[] JointErrors;
        public SavedPose Pose { get; }
        public bool IsDirty { get; private set; }
        public IReadOnlyList<PoseConstraint> Constraints => constraints;

        public PoseMatchProfile(SavedPose pose)
        {
            Pose = pose;
            constraints = new List<PoseConstraint>(pose.matchSettings?.constraints ?? Array.Empty<PoseConstraint>());
            foreach (PoseConstraint _ in constraints)
                constraintResults.Add(default);
            bones = new PoseBoneSetting[pose.joints.Length];
            boneCategories = new PoseCategory[bones.Length];
            JointErrors = new float[bones.Length];
            Array.Fill(JointErrors, float.NaN);
            var indices = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < bones.Length; i++)
            {
                bones[i].id = pose.joints[i].id;
                indices.Add(bones[i].id, i);
            }
            for (int i = 0; i < bones.Length; i++)
            {
                if (!UniversalSkeleton.TryGet(pose.joints[i].id, out SkeletonBone definition))
                    throw new ArgumentException("Unknown universal bone: " + pose.joints[i].id);
                PoseCategory category = boneCategories[i] = definition.Category;
                while (true)
                {
                    categoryBoneCounts[(int)category]++;
                    if (category == PoseCategory.All)
                        break;
                    category = Parent(category);
                }
            }

            foreach (PoseCategorySetting setting in pose.matchSettings?.categories ?? Array.Empty<PoseCategorySetting>())
            {
                if ((uint)setting.category < CategoryCount)
                    categories[(int)setting.category] = Normalize(setting);
            }
            foreach (PoseBoneSetting setting in pose.matchSettings?.bones ?? Array.Empty<PoseBoneSetting>())
            {
                if (setting.id != null && indices.TryGetValue(setting.id, out int index))
                    bones[index] = Normalize(setting);
            }
        }

        public static PoseCategory Parent(PoseCategory category) => Parents[(int)category];
        public static string Label(PoseCategory category) => Labels[(int)category];

        public static bool IsSessionIgnored(PoseCategory category, IReadOnlyCollection<PoseCategory> ignoredCategories)
        {
            if (ignoredCategories == null || ignoredCategories.Count == 0)
                return false;

            while (true)
            {
                if (ignoredCategories.Contains(category))
                    return true;
                if (category == PoseCategory.All)
                    return false;
                category = Parent(category);
            }
        }
        internal static IEnumerable<PoseCategory> Children(PoseCategory category)
        {
            for (int i = 1; i < CategoryCount; i++)
            {
                if (Parents[i] == category)
                    yield return (PoseCategory)i;
            }
        }

        public PoseCategory CategoryOf(int index) => boneCategories[index];
        public int BoneCount(PoseCategory category) => categoryBoneCounts[(int)category];
        public float UsedBudget(PoseCategory category) => Excess[(int)category];
        public bool HasFailed(PoseCategory category) => CategoryFailed[(int)category];
        public float JointError(int index) => JointErrors[index];
        public PoseConstraintResult ConstraintResult(int index) => constraintResults[index];

        public void AddConstraint(PoseConstraint rule)
        {
            constraints.Add(rule);
            constraintResults.Add(default);
            IsDirty = true;
        }

        public void SetConstraint(int index, PoseConstraint rule)
        {
            constraints[index] = rule;
            constraintResults[index] = default;
            IsDirty = true;
        }

        public void RemoveConstraint(int index)
        {
            constraints.RemoveAt(index);
            constraintResults.RemoveAt(index);
            IsDirty = true;
        }

        // Evaluate every rule so the editor can show each live result even when one already failed.
        internal int EvaluateConstraints(TrackedPose frame, out int firstFailure)
        {
            firstFailure = -1;
            int passed = 0;
            for (int i = 0; i < constraints.Count; i++)
            {
                PoseConstraintResult result = PoseConstraintResult.Evaluate(frame, constraints[i]);
                constraintResults[i] = result;
                if (result.State == PoseConstraintState.Passed)
                    passed++;
                else if (firstFailure < 0)
                    firstFailure = i;
            }
            return passed;
        }

        public PoseCategorySetting GetCategory(PoseCategory category) => categories[(int)category];
        public PoseBoneSetting GetBone(int index) => bones[index];

        public void SetCategory(PoseCategory category, PoseCategorySetting setting)
        {
            setting.category = category;
            categories[(int)category] = Normalize(setting);
            IsDirty = true;
        }

        public void SetBone(int index, PoseBoneSetting setting)
        {
            setting.id = bones[index].id;
            bones[index] = Normalize(setting);
            IsDirty = true;
        }

        public bool IsIncluded(PoseCategory category)
        {
            while (true)
            {
                if (GetCategory(category).disabled)
                    return false;
                if (category == PoseCategory.All)
                    return true;
                category = Parent(category);
            }
        }

        public bool IsBoneIncluded(int index) => !bones[index].disabled && IsIncluded(boneCategories[index]);

        // Walk up the hierarchy until a category provides its own default tolerance.
        public float Tolerance(PoseCategory category, float fallback)
        {
            while (true)
            {
                PoseCategorySetting setting = GetCategory(category);
                if (setting.overrideTolerance)
                    return setting.toleranceDegrees;
                if (category == PoseCategory.All)
                    return fallback;
                category = Parent(category);
            }
        }

        public float BoneTolerance(int index, float fallback) => bones[index].overrideTolerance
            ? bones[index].toleranceDegrees : Tolerance(boneCategories[index], fallback);

        // Persist only explicit bone and category settings. Inherited values are resolved when the pose is used.
        internal PoseMatchSettings Export()
        {
            var categorySettings = new List<PoseCategorySetting>();
            for (int i = 0; i < CategoryCount; i++)
            {
                PoseCategorySetting setting = categories[i];
                if (setting.disabled || setting.overrideTolerance || setting.useBudget)
                    categorySettings.Add(setting);
            }
            var boneSettings = new List<PoseBoneSetting>();
            foreach (PoseBoneSetting setting in bones)
            {
                if (setting.disabled || setting.overrideTolerance)
                    boneSettings.Add(setting);
            }
            return new PoseMatchSettings
            {
                categories = categorySettings.ToArray(), bones = boneSettings.ToArray(), constraints = constraints.ToArray()
            };
        }

        internal void MarkSaved() => IsDirty = false;

        private static PoseCategorySetting Normalize(PoseCategorySetting setting)
        {
            setting.toleranceDegrees = Degrees(setting.toleranceDegrees, 180);
            setting.budgetDegrees = Degrees(setting.budgetDegrees, float.MaxValue);
            return setting;
        }

        private static PoseBoneSetting Normalize(PoseBoneSetting setting)
        {
            setting.toleranceDegrees = Degrees(setting.toleranceDegrees, 180);
            return setting;
        }

        private static float Degrees(float value, float max) => float.IsFinite(value) ? Mathf.Clamp(value, 0, max) : 0;

    }
}
