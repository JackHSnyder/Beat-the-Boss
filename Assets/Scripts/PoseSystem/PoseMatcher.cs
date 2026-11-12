using System;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    public static class PoseMatcher
    {
        private const float AngleEpsilon = 0.001f;

        public readonly struct Result
        {
            public readonly int Compared;
            public readonly int Missing;
            public readonly int OutsideTolerance;
            public readonly float WorstError;
            public readonly string WorstJoint;
            public readonly string FirstMissingJoint;
            public readonly PoseCategory? FirstFailedCategory;
            public readonly int ConstraintCount;
            public readonly int PassedConstraints;
            public readonly int FirstFailedConstraint;

            public bool IsMatch => (Compared > 0 || ConstraintCount > 0) && Missing == 0 &&
                FirstFailedCategory == null && PassedConstraints == ConstraintCount;

            internal Result(int compared, int missing, int outsideTolerance, float worstError,
                string worstJoint, string firstMissingJoint, PoseCategory? firstFailedCategory,
                int constraintCount, int passedConstraints, int firstFailedConstraint)
            {
                Compared = compared;
                Missing = missing;
                OutsideTolerance = outsideTolerance;
                WorstError = worstError;
                WorstJoint = worstJoint;
                FirstMissingJoint = firstMissingJoint;
                FirstFailedCategory = firstFailedCategory;
                ConstraintCount = constraintCount;
                PassedConstraints = passedConstraints;
                FirstFailedConstraint = firstFailedConstraint;
            }
        }

        public static Result Compare(TrackedPose current, SavedPose target, float toleranceDegrees,
            PoseBranches ignoredBranches = PoseBranches.None) =>
            Compare(current, target, target.matchSettings == null ? null : new PoseMatchProfile(target),
                toleranceDegrees, ignoredBranches);

        public static Result Compare(TrackedPose current, PoseMatchProfile profile, float toleranceDegrees,
            PoseBranches ignoredBranches = PoseBranches.None) =>
            Compare(current, profile.Pose, profile, toleranceDegrees, ignoredBranches);

        private static Result Compare(TrackedPose current, SavedPose target, PoseMatchProfile profile,
            float toleranceDegrees, PoseBranches ignoredBranches)
        {
            int compared = 0;
            int missing = 0;
            int outside = 0;
            float worst = 0;
            string worstJoint = null;
            string firstMissingJoint = null;
            float tolerance = Mathf.Clamp(toleranceDegrees, 0, 180);
            if (profile != null)
            {
                Array.Clear(profile.Excess, 0, profile.Excess.Length);
                Array.Clear(profile.CategoryFailed, 0, profile.CategoryFailed.Length);
                Array.Fill(profile.JointErrors, float.NaN);
            }

            for (int i = 0; i < target.joints.Length; i++)
            {
                PoseJoint joint = target.joints[i];
                if ((joint.branch & ignoredBranches) != 0 || (profile != null && !profile.IsBoneIncluded(i)))
                    continue;

                if (!current.TryGet(joint.id, out TrackedBone bone) || !bone.HasLocalRotation ||
                    (joint.parentId ?? "") != (bone.ParentId ?? ""))
                {
                    missing++;
                    firstMissingJoint ??= joint.id;
                    continue;
                }

                float angle = Quaternion.Angle(bone.LocalRotation, joint.localRotation);
                if (profile != null)
                    profile.JointErrors[i] = angle;
                compared++;
                float excess = angle - (profile?.BoneTolerance(i, tolerance) ?? tolerance);
                if (excess > AngleEpsilon)
                {
                    outside++;
                    if (profile != null)
                        SpendBudget(profile, profile.CategoryOf(i), excess);
                }
                if (worstJoint == null || angle > worst)
                {
                    worst = angle;
                    worstJoint = joint.id;
                }
            }

            PoseCategory? failed = profile == null
                ? (outside > 0 ? PoseCategory.All : null)
                : CheckBudgets(profile);
            int firstFailedConstraint = -1;
            int passedConstraints = profile?.EvaluateConstraints(current, out firstFailedConstraint) ?? 0;
            return new Result(compared, missing, outside, worst, worstJoint, firstMissingJoint, failed,
                profile?.Constraints.Count ?? 0, passedConstraints, firstFailedConstraint);
        }

        private static void SpendBudget(PoseMatchProfile profile, PoseCategory category, float excess)
        {
            PoseCategory leaf = category;
            bool hasBudget = false;
            while (true)
            {
                profile.Excess[(int)category] += excess;
                hasBudget |= profile.GetCategory(category).useBudget;
                if (category == PoseCategory.All)
                    break;
                category = PoseMatchProfile.Parent(category);
            }
            if (!hasBudget)
                profile.CategoryFailed[(int)leaf] = true;
        }

        private static PoseCategory? CheckBudgets(PoseMatchProfile profile)
        {
            PoseCategory? first = null;
            for (int i = 0; i < PoseMatchProfile.CategoryCount; i++)
            {
                PoseCategorySetting setting = profile.GetCategory((PoseCategory)i);
                float spent = profile.Excess[i];
                if (setting.useBudget && spent > AngleEpsilon && spent >= setting.budgetDegrees - AngleEpsilon)
                    profile.CategoryFailed[i] = true;
                if (profile.CategoryFailed[i])
                    first ??= (PoseCategory)i;
            }
            return first;
        }
    }
}
