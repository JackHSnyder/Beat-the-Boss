// Defines position and direction rules and evaluates them against a tracked frame.
// PoseConstraintPanel edits the rules. PoseMatcher uses their results when checking a pose.

using System;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    public enum PoseConstraintType { Position, Direction }
    public enum PoseAxis { Forward, Back, Right, Left, Up, Down }
    public enum PoseConstraintState { Waiting, Passed, OutsideTolerance, MissingTracking, Invalid }

    [Serializable]
    public struct PoseConstraint
    {
        public PoseConstraintType type;
        public string boneId;
        // An empty target selects a point or axis in player-local space.
        public string targetBoneId;
        public Vector3 localPoint;
        public PoseAxis boneAxis;
        public PoseAxis targetAxis;
        public float maxDistance;
        public float maxAngle;

        public bool IsValid => UniversalSkeleton.TryGet(boneId, out _) &&
            (!UsesBone || UniversalSkeleton.TryGet(targetBoneId, out _)) &&
            (type switch
            {
                PoseConstraintType.Position => float.IsFinite(maxDistance) && maxDistance >= 0 &&
                    (UsesBone || (float.IsFinite(localPoint.x) && float.IsFinite(localPoint.y) && float.IsFinite(localPoint.z))),
                PoseConstraintType.Direction => IsAxis(boneAxis) && IsAxis(targetAxis) &&
                    float.IsFinite(maxAngle) && maxAngle >= 0 && maxAngle <= 180,
                _ => false
            });

        public bool UsesBone => !string.IsNullOrEmpty(targetBoneId);

        private static bool IsAxis(PoseAxis axis) => (uint)axis <= (uint)PoseAxis.Down;

        internal static Vector3 AxisVector(PoseAxis axis) => axis switch
        {
            PoseAxis.Back => Vector3.back,
            PoseAxis.Right => Vector3.right,
            PoseAxis.Left => Vector3.left,
            PoseAxis.Up => Vector3.up,
            PoseAxis.Down => Vector3.down,
            _ => Vector3.forward
        };
    }

    public readonly struct PoseConstraintResult
    {
        public readonly PoseConstraintState State;
        public readonly float Error;

        internal PoseConstraintResult(PoseConstraintState state, float error = 0)
        {
            State = state;
            Error = error;
        }

        internal static PoseConstraintResult Evaluate(TrackedPose frame, PoseConstraint rule)
        {
            if (!rule.IsValid)
                return new PoseConstraintResult(PoseConstraintState.Invalid);
            if (!frame.TryGetPlayerPose(rule.boneId, out Pose bone, out bool boneOrientation))
                return new PoseConstraintResult(PoseConstraintState.MissingTracking);

            // Identity supplies the axes of player-local space when the rule has no target bone.
            Pose target = Pose.identity;
            bool targetOrientation = true;
            if (rule.UsesBone && !frame.TryGetPlayerPose(rule.targetBoneId, out target, out targetOrientation))
                return new PoseConstraintResult(PoseConstraintState.MissingTracking);

            float error;
            float limit;
            if (rule.type == PoseConstraintType.Position)
            {
                error = Vector3.Distance(bone.position, rule.UsesBone ? target.position : rule.localPoint);
                limit = rule.maxDistance;
            }
            else
            {
                // Some tracked joints supply a position only. A direction rule needs both orientations.
                if (!boneOrientation || !targetOrientation)
                    return new PoseConstraintResult(PoseConstraintState.MissingTracking);
                error = Vector3.Angle(bone.rotation * PoseConstraint.AxisVector(rule.boneAxis),
                    target.rotation * PoseConstraint.AxisVector(rule.targetAxis));
                limit = rule.maxAngle;
            }

            return new PoseConstraintResult(error <= limit + (rule.type == PoseConstraintType.Position ? 0.000001f : 0.001f)
                ? PoseConstraintState.Passed : PoseConstraintState.OutsideTolerance, error);
        }
    }
}
