using System;
using System.Collections.Generic;
using BeatTheBoss.Poses;
using UnityEngine;

// Sample the posed avatar after PlayerAvatarDriver and before MoveDetection raises an attack.
[DefaultExecutionOrder(200)]
public sealed class FiringAimTracker : MonoBehaviour
{
    [Serializable]
    private struct BoneAim
    {
        [Tooltip("Exact joint name on the stylized avatar.")]
        public string boneName;
        [Tooltip("Direction in the joint's local axes, for example (0, 0, 1) for forward or (0, -1, 0) for down.")]
        public Vector3 localDirection;
    }

    [SerializeField] private PlayerAvatarDriver avatarDriver;
    [SerializeField] private BoneAim[] trackedBones = Array.Empty<BoneAim>();
    [SerializeField, Min(0f), Tooltip("Seconds to remember a target after aim leaves its tolerance.")]
    private float holdSeconds = 0.35f;
    [SerializeField, Min(0f), Tooltip("Seconds to look back for aim when firing without a remembered target. Zero uses live aim.")]
    private float freeAimLookbackSeconds = 0.12f;

    private readonly Dictionary<string, (FiringTarget Target, double LastAimedAt)> rememberedTargets =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, AimHistory> aimHistories = new(StringComparer.Ordinal);

    private sealed class AimHistory
    {
        public readonly Queue<(double Time, Vector3 Direction)> Pending = new();
        public (double Time, Vector3 Direction)? BeforeLookback;
        public int LastSampleFrame;
    }

    private void OnDisable()
    {
        rememberedTargets.Clear();
        aimHistories.Clear();
    }

    private void LateUpdate()
    {
        AvatarPoseRig rig = avatarDriver != null && avatarDriver.isActiveAndEnabled ? avatarDriver.Rig : null;
        if (rig == null)
            return;

        double now = Time.unscaledTimeAsDouble;
        foreach (BoneAim aim in trackedBones)
        {
            if (string.IsNullOrEmpty(aim.boneName) || aim.localDirection.sqrMagnitude < 0.00000001f ||
                !rig.TryGetJoint(aim.boneName, out Transform joint) || joint == null)
                continue;

            Vector3 direction = joint.TransformDirection(aim.localDirection.normalized);
            RememberDirection(aim.boneName, direction, now);
            FiringTarget target = FindBestTarget(joint.position, direction);
            if (target != null)
                rememberedTargets[aim.boneName] = (target, now);
        }
    }

    private void RememberDirection(string boneName, Vector3 direction, double now)
    {
        if (freeAimLookbackSeconds <= 0f)
            return;

        int frame = Time.frameCount;
        if (!aimHistories.TryGetValue(boneName, out AimHistory history) || history.LastSampleFrame < frame - 1)
        {
            history = new AimHistory();
            aimHistories[boneName] = history;
        }

        history.LastSampleFrame = frame;
        history.Pending.Enqueue((now, direction));
        double cutoff = now - freeAimLookbackSeconds;
        while (history.Pending.Count > 0 && history.Pending.Peek().Time <= cutoff)
            history.BeforeLookback = history.Pending.Dequeue();
    }

    public bool TryGetFreeAimDirection(string boneName, out Vector3 direction)
    {
        direction = default;
        if (freeAimLookbackSeconds <= 0f || string.IsNullOrEmpty(boneName) ||
            !aimHistories.TryGetValue(boneName, out AimHistory history) ||
            history.LastSampleFrame != Time.frameCount || !history.BeforeLookback.HasValue)
            return false;

        var sample = history.BeforeLookback.Value;
        if (sample.Time > Time.unscaledTimeAsDouble - freeAimLookbackSeconds)
            return false;

        direction = sample.Direction;
        return true;
    }

    public bool TryGetLastTarget(string boneName, out FiringTarget target)
    {
        target = null;
        if (string.IsNullOrEmpty(boneName) || !rememberedTargets.TryGetValue(boneName, out var entry))
            return false;

        if (entry.Target == null || !entry.Target.isActiveAndEnabled || entry.Target.gameObject.scene != gameObject.scene ||
            Time.unscaledTimeAsDouble - entry.LastAimedAt > holdSeconds)
        {
            rememberedTargets.Remove(boneName);
            return false;
        }

        target = entry.Target;
        return true;
    }

    private FiringTarget FindBestTarget(Vector3 origin, Vector3 direction)
    {
        FiringTarget best = null;
        float bestAngle = float.PositiveInfinity;
        float bestDistanceSquared = float.PositiveInfinity;
        foreach (FiringTarget target in FiringTarget.ActiveTargets)
        {
            if (target == null || target.gameObject.scene != gameObject.scene)
                continue;

            Vector3 offset = target.WorldAimPoint - origin;
            float distanceSquared = offset.sqrMagnitude;
            if (distanceSquared < 0.00000001f)
                continue;

            float angle = Vector3.Angle(direction, offset);
            if (angle > target.AngularTolerance || angle > bestAngle ||
                (angle == bestAngle && distanceSquared >= bestDistanceSquared))
                continue;

            best = target;
            bestAngle = angle;
            bestDistanceSquared = distanceSquared;
        }

        return best;
    }
}
