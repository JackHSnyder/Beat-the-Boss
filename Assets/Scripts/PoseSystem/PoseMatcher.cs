using UnityEngine;

namespace BeatTheBoss.Poses
{
    public static class PoseMatcher
    {
        public readonly struct Result
        {
            public readonly int Compared;
            public readonly int Missing;
            public readonly int OutsideTolerance;
            public readonly float WorstError;
            public readonly string WorstJoint;
            public readonly string FirstMissingJoint;

            public bool IsMatch => Compared > 0 && Missing == 0 && OutsideTolerance == 0;

            internal Result(int compared, int missing, int outsideTolerance, float worstError,
                string worstJoint, string firstMissingJoint)
            {
                Compared = compared;
                Missing = missing;
                OutsideTolerance = outsideTolerance;
                WorstError = worstError;
                WorstJoint = worstJoint;
                FirstMissingJoint = firstMissingJoint;
            }
        }

        public static Result Compare(TrackedPose current, SavedPose target, float toleranceDegrees,
            PoseBranches ignoredBranches = PoseBranches.None)
        {
            int compared = 0;
            int missing = 0;
            int outside = 0;
            float worst = 0;
            string worstJoint = null;
            string firstMissingJoint = null;
            float tolerance = Mathf.Clamp(toleranceDegrees, 0, 180);

            foreach (PoseJoint joint in target.joints)
            {
                if ((joint.branch & ignoredBranches) != 0)
                    continue;

                if (!current.TryGet(joint.id, out TrackedBone bone) || !bone.HasLocalRotation ||
                    (joint.parentId ?? "") != (bone.ParentId ?? ""))
                {
                    missing++;
                    firstMissingJoint ??= joint.id;
                    continue;
                }

                float angle = Quaternion.Angle(bone.LocalRotation, joint.localRotation);
                compared++;
                if (angle > tolerance)
                    outside++;
                if (worstJoint == null || angle > worst)
                {
                    worst = angle;
                    worstJoint = joint.id;
                }
            }

            return new Result(compared, missing, outside, worst, worstJoint, firstMissingJoint);
        }
    }
}
