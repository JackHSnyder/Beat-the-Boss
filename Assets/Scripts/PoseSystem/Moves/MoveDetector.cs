// Evaluates one move in sequence, tracking action timing, deadlines, and cooldown.
// MoveDetection uses it in gameplay scenes. MoveWorkbench uses it to test drafts.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    public enum MoveOutcome { None, Passed, TimedOut }

    public sealed class MoveDetector
    {
        private readonly MoveDefinition move;
        private readonly PoseMatchProfile[] poses;
        private readonly MoveVelocityHistory history;
        private readonly double[] actionPasses;
        private double globalDeadline = double.PositiveInfinity;
        private double temporaryDeadline = double.PositiveInfinity;
        private double cooldownDeadline;
        private double stepStarted;

        public int StepIndex { get; private set; }
        public int StepCount => move.steps.Count;
        public int ActionsInWindow { get; private set; }
        public double? GlobalRemaining(double now) => Remaining(globalDeadline, now);
        public double? TemporaryRemaining(double now) => Remaining(temporaryDeadline, now);
        public double CooldownRemaining(double now) => Math.Max(0, cooldownDeadline - now);

        public MoveDetector(MoveDefinition definition, Func<string, SavedPose> loadPose)
        {
            definition.Validate();
            // Snapshot the draft and cache referenced poses for this run so edits take effect on the next test.
            var detection = new MoveDefinition
            {
                name = definition.name, timeLimit = definition.timeLimit,
                cooldown = definition.cooldown, steps = definition.steps
            };
            move = JsonUtility.FromJson<MoveDefinition>(JsonUtility.ToJson(detection));
            poses = new PoseMatchProfile[StepCount];
            var cached = new Dictionary<string, PoseMatchProfile>(StringComparer.Ordinal);
            int maxActions = 0;
            for (int i = 0; i < StepCount; i++)
            {
                MoveStep step = move.steps[i];
                if (step.kind == MoveStepKind.ActionSet)
                    maxActions = Math.Max(maxActions, step.actions.Count);
                else
                {
                    if (!cached.TryGetValue(step.poseName, out PoseMatchProfile profile))
                    {
                        SavedPose pose = loadPose(step.poseName);
                        if (pose == null)
                            throw new InvalidDataException("Saved pose unavailable: " + step.poseName);
                        cached.Add(step.poseName, profile = new PoseMatchProfile(pose));
                    }
                    poses[i] = profile;
                }
            }
            history = new MoveVelocityHistory(move);
            actionPasses = new double[maxActions];
            ResetStep();
        }

        public MoveOutcome Tick(TrackedPose frame, double now, float fallbackTolerance,
            IReadOnlyCollection<PoseCategory> ignoredCategories = null)
        {
            history.Sample(frame, now);
            if (now < cooldownDeadline)
                return MoveOutcome.None;
            // Deadlines take precedence over a step that would pass on the same frame.
            if (now >= globalDeadline || now >= temporaryDeadline)
            {
                ResetProgress();
                stepStarted = now;
                return MoveOutcome.TimedOut;
            }

            MoveStep step = move.steps[StepIndex];
            bool passed = step.kind == MoveStepKind.Pose
                ? PoseMatcher.Compare(frame, poses[StepIndex], fallbackTolerance, ignoredCategories).IsMatch
                : CheckActions(step, now);
            if (!passed)
                return MoveOutcome.None;

            // Timers begin or change only after the current step passes. Infinity represents a stopped timer.
            if (StepIndex == 0)
                globalDeadline = now + move.timeLimit;
            if (step.timerMode == MoveTimerMode.Set)
                temporaryDeadline = step.timerSeconds == 0 ? double.PositiveInfinity : now + step.timerSeconds;
            else if (step.timerMode == MoveTimerMode.Add && step.timerSeconds > 0)
                temporaryDeadline = (double.IsPositiveInfinity(temporaryDeadline) ? now : temporaryDeadline) + step.timerSeconds;

            StepIndex++;
            stepStarted = now;
            ResetStep();
            if (StepIndex < StepCount)
                return MoveOutcome.None;

            ResetProgress();
            cooldownDeadline = now + move.cooldown;
            stepStarted = cooldownDeadline;
            return MoveOutcome.Passed;
        }

        private bool CheckActions(MoveStep step, double now)
        {
            ActionsInWindow = 0;
            for (int i = 0; i < step.actions.Count; i++)
            {
                MoveAction action = step.actions[i];
                // A measurement window must start within this step. Keep each action's latest pass until it expires.
                if (now - stepStarted >= action.sampleSeconds &&
                    history.TryMeasure(action, now, out Vector3 velocity) && action.Passes(velocity))
                    actionPasses[i] = now;
                if (now - actionPasses[i] <= step.actionWindow)
                    ActionsInWindow++;
            }
            return ActionsInWindow == step.actions.Count;
        }

        private void ResetStep()
        {
            Array.Fill(actionPasses, double.NegativeInfinity);
            ActionsInWindow = 0;
        }

        private void ResetProgress()
        {
            StepIndex = 0;
            globalDeadline = temporaryDeadline = double.PositiveInfinity;
            ResetStep();
        }

        private static double? Remaining(double deadline, double now) =>
            double.IsPositiveInfinity(deadline) ? null : Math.Max(0, deadline - now);
    }
}
