using System.Collections.Generic;
using BeatTheBoss.Poses;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class AttackLaser : MonoBehaviour
{
    [SerializeField] private MoveDetection moveDetection;
    [SerializeField] private PlayerAvatarDriver avatarDriver;
    [SerializeField] private FiringAimTracker aimTracker;
    [SerializeField] private Material laserMaterial;
    [SerializeField, Min(0.01f)] private float speed = 60f;
    [SerializeField, Min(0.01f)] private float range = 75f;
    [SerializeField, Min(0.01f)] private float boltLength = 1.1f;
    [SerializeField, Min(0.001f)] private float boltWidth = 0.025f;
    [SerializeField] private LayerMask collisionLayers = Physics.DefaultRaycastLayers;

    private readonly List<Bolt> bolts = new();

    private sealed class Bolt
    {
        public Ray Aim;
        public LineRenderer Line;
        public float Distance;
        public bool Finished;
    }

    private void OnEnable()
    {
        if (moveDetection != null)
            moveDetection.LoadoutMovePerformed += FireOnAttack;
    }

    private void OnDisable()
    {
        if (moveDetection != null)
            moveDetection.LoadoutMovePerformed -= FireOnAttack;

        foreach (Bolt bolt in bolts)
            if (bolt.Line != null)
                Destroy(bolt.Line.gameObject);
        bolts.Clear();
    }

    private void FireOnAttack(MoveSlot slot)
    {
        if (slot != MoveSlot.Attack || laserMaterial == null || avatarDriver == null || avatarDriver.Rig == null ||
            !avatarDriver.Rig.TryGetRightIndexRay(out Ray aim))
            return;

        if (aimTracker != null)
        {
            const string aimJoint = AvatarPoseRig.RightIndexProximalJoint;
            if (aimTracker.TryGetFreeAimDirection(aimJoint, out Vector3 freeAimDirection))
                aim.direction = freeAimDirection;

            if (aimTracker.TryGetLastTarget(aimJoint, out FiringTarget target))
            {
                Vector3 targetDirection = target.WorldAimPoint - aim.origin;
                if (targetDirection.sqrMagnitude >= 0.00000001f)
                    aim.direction = targetDirection.normalized;
            }
        }

        var visual = new GameObject("Attack Laser Bolt");
        visual.transform.SetParent(transform, false);
        LineRenderer line = visual.AddComponent<LineRenderer>();
        line.sharedMaterial = laserMaterial;
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.widthMultiplier = boltWidth;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.SetPosition(0, aim.origin);
        line.SetPosition(1, aim.origin);

        var bolt = new Bolt { Aim = aim, Line = line };
        // Draw the first swept segment from the fingertip on the firing frame.
        AdvanceBolt(bolt, Mathf.Min(speed * Time.deltaTime, boltLength), avatarDriver.transform.root);
        bolts.Add(bolt);
    }

    private void Update()
    {
        float stepLength = speed * Time.deltaTime;
        Transform owner = avatarDriver != null ? avatarDriver.transform.root : null;
        for (int i = bolts.Count - 1; i >= 0; i--)
        {
            Bolt bolt = bolts[i];
            if (bolt.Line == null || bolt.Finished || bolt.Distance >= range)
            {
                if (bolt.Line != null)
                    Destroy(bolt.Line.gameObject);
                bolts.RemoveAt(i);
                continue;
            }

            AdvanceBolt(bolt, stepLength, owner);
        }
    }

    private void AdvanceBolt(Bolt bolt, float stepLength, Transform owner)
    {
        float nextDistance = Mathf.Min(bolt.Distance + stepLength, range);
        float travel = nextDistance - bolt.Distance;
        if (travel <= 0f)
            return;

        // Sweep the distance covered this frame so fast bolts cannot skip thin colliders.
        var step = new Ray(bolt.Aim.GetPoint(bolt.Distance), bolt.Aim.direction);
        bool hitSurface = TryHitSurface(step, travel, owner, out RaycastHit hit);
        bolt.Distance = hitSurface ? bolt.Distance + hit.distance : nextDistance;
        bolt.Line.SetPosition(0, bolt.Aim.GetPoint(Mathf.Max(0f, bolt.Distance - boltLength)));
        bolt.Line.SetPosition(1, hitSurface ? hit.point : bolt.Aim.GetPoint(bolt.Distance));
        bolt.Finished = hitSurface;

        if (hitSurface)
            hit.collider.GetComponentInParent<FiringTarget>()?.ReceiveLaserHit();
    }

    private bool TryHitSurface(Ray step, float distance, Transform owner, out RaycastHit hit)
    {
        if (!Physics.Raycast(step, out hit, distance, collisionLayers, QueryTriggerInteraction.Ignore))
            return false;

        if (owner == null || !hit.collider.transform.IsChildOf(owner))
            return true;

        // If the nearest hit was the player, look for the nearest solid hit behind them.
        float nearest = float.PositiveInfinity;
        foreach (RaycastHit candidate in Physics.RaycastAll(step, distance, collisionLayers, QueryTriggerInteraction.Ignore))
        {
            if (candidate.distance >= nearest || candidate.collider.transform.IsChildOf(owner))
                continue;

            hit = candidate;
            nearest = candidate.distance;
        }
        return nearest < float.PositiveInfinity;
    }
}
