// Defines the saved move format, including pose steps, action sets, velocity thresholds, and timers.
// MovePanel and MoveWorkbench edit it. MoveStore loads it, and MoveDetector runs it.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    public enum MoveStepKind { Pose, ActionSet }
    public enum MoveVelocityKind { Position, Rotation }
    public enum MoveTimerMode { None, Set, Add }

    [Serializable]
    public sealed class MoveDefinition
    {
        public int version = 2;
        public string name = "New move";
        public float timeLimit = 5;
        public float cooldown = 0.5f;
        public List<MoveStep> steps = new();
        public string animationName;
        public MoveAnimation animation;

        // Validate before saving or testing so every step can be evaluated.
        public void Validate()
        {
            if (version != 2 || !float.IsFinite(timeLimit) || timeLimit <= 0 || !Nonnegative(cooldown) ||
                steps == null || steps.Count == 0)
                throw new InvalidDataException("A move needs at least one step, a positive time limit, and a nonnegative cooldown.");

            for (int i = 0; i < steps.Count; i++)
            {
                MoveStep step = steps[i];
                if (step == null || !Nonnegative(step.timerSeconds) || (uint)step.timerMode > (uint)MoveTimerMode.Add)
                    throw new InvalidDataException($"Check the temporary timer on step {i + 1}.");

                if (step.kind == MoveStepKind.Pose && !string.IsNullOrWhiteSpace(step.poseName))
                    continue;

                if (step.kind != MoveStepKind.ActionSet || !Nonnegative(step.actionWindow) ||
                    step.actions == null || step.actions.Count == 0)
                    throw new InvalidDataException($"Choose a saved pose or configure an action set on step {i + 1}.");

                for (int j = 0; j < step.actions.Count; j++)
                {
                    MoveAction action = step.actions[j];
                    if (!UniversalSkeleton.TryGet(action.boneId, out _) || !float.IsFinite(action.sampleSeconds) ||
                        action.sampleSeconds <= 0 || (uint)action.kind > (uint)MoveVelocityKind.Rotation ||
                        !float.IsFinite(action.velocity.x) || !float.IsFinite(action.velocity.y) ||
                        !float.IsFinite(action.velocity.z) || (action.velocity.x == 0 && action.velocity.y == 0 && action.velocity.z == 0))
                        throw new InvalidDataException($"Set a bone, sample time, and nonzero velocity for action {j + 1} on step {i + 1}.");
                }
            }
        }

        private static bool Nonnegative(float value) => float.IsFinite(value) && value >= 0;

        internal void UpdateLegacyBoneIds()
        {
            if (version != 1 || steps == null)
                return;
            foreach (MoveStep step in steps)
            {
                if (step?.actions == null)
                    continue;
                for (int i = 0; i < step.actions.Count; i++)
                {
                    MoveAction action = step.actions[i];
                    action.boneId = BoneMappings.DefaultBone(action.boneId);
                    step.actions[i] = action;
                }
            }
            version = 2;
        }
    }

    [Serializable]
    public sealed class MoveStep
    {
        public MoveStepKind kind;
        public string poseName;
        public float actionWindow = 0.25f;
        public List<MoveAction> actions = new();
        public MoveTimerMode timerMode;
        public float timerSeconds;
    }

    [Serializable]
    public struct MoveAction
    {
        public string boneId;
        public MoveVelocityKind kind;
        public float sampleSeconds;
        public Vector3 velocity;
        public bool directionMatters;

        internal bool Passes(Vector3 measured) =>
            PassesAxis(measured.x, velocity.x) && PassesAxis(measured.y, velocity.y) &&
            PassesAxis(measured.z, velocity.z);

        private bool PassesAxis(float actual, float required)
        {
            // Zero leaves this axis unchecked. A signed threshold projects the measured velocity onto the required direction.
            if (required == 0)
                return true;
            float aligned = directionMatters ? actual * Mathf.Sign(required) : Mathf.Abs(actual);
            return aligned >= Mathf.Abs(required);
        }
    }
}
