// Compares a tracked frame with a saved pose using joint rotations, category tolerances, and constraints.
// PoseWorkbench uses it for standalone pose tests. MoveDetector uses it for pose steps.

using System;
using System.Collections.Generic;
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
            IReadOnlyCollection<PoseCategory> ignoredCategories = null) =>
            Compare(current, new PoseMatchProfile(target), toleranceDegrees, ignoredCategories);

        public static Result Compare(TrackedPose current, PoseMatchProfile profile, float toleranceDegrees,
            IReadOnlyCollection<PoseCategory> ignoredCategories = null)
        {
            SavedPose target = profile.Pose;
            int compared = 0;
            int missing = 0;
            int outside = 0;
            float worst = 0;
            string worstJoint = null;
            string firstMissingJoint = null;
            float tolerance = Mathf.Clamp(toleranceDegrees, 0, 180);
            Array.Clear(profile.Excess, 0, profile.Excess.Length);
            Array.Clear(profile.CategoryFailed, 0, profile.CategoryFailed.Length);
            Array.Fill(profile.JointErrors, float.NaN);

            for (int i = 0; i < target.joints.Length; i++)
            {
                PoseJoint joint = target.joints[i];
                PoseCategory category = profile.CategoryOf(i);
                if (!profile.IsBoneIncluded(i) || PoseMatchProfile.IsSessionIgnored(category, ignoredCategories))
                    continue;

                // The universal skeleton fixes the reference for this ID across all sources.
                if (!current.TryGet(joint.id, out TrackedBone bone) || !bone.HasLocalRotation)
                {
                    missing++;
                    firstMissingJoint ??= joint.id;
                    continue;
                }

                float angle = Quaternion.Angle(bone.LocalRotation, joint.localRotation);
                profile.JointErrors[i] = angle;
                compared++;
                float excess = angle - profile.BoneTolerance(i, tolerance);
                if (excess > AngleEpsilon)
                {
                    outside++;
                    SpendBudget(profile, category, excess);
                }
                if (worstJoint == null || angle > worst)
                {
                    worst = angle;
                    worstJoint = joint.id;
                }
            }

            PoseCategory? failed = CheckBudgets(profile);
            int passedConstraints = profile.EvaluateConstraints(current, out int firstFailedConstraint);
            return new Result(compared, missing, outside, worst, worstJoint, firstMissingJoint, failed,
                profile.Constraints.Count, passedConstraints, firstFailedConstraint);
        }

        // Charge excess degrees to the bone's category and every ancestor. An overage with no budget fails immediately.
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
                // An exact spend exhausts the budget. A zero budget still allows a frame with no excess.
                if (setting.useBudget && spent > AngleEpsilon && spent >= setting.budgetDegrees - AngleEpsilon)
                    profile.CategoryFailed[i] = true;
                if (profile.CategoryFailed[i])
                    first ??= (PoseCategory)i;
            }
            return first;
        }
    }
}
