using System.Collections;
using BeatTheBoss.Poses;
using Oculus.Interaction.Locomotion;
using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(LineRenderer))]
public class MoveCircle : MonoBehaviour
{
    [Min(0f)] public float radius = 3f;
    [SerializeField, Min(3)] private int segments = 96;
    [SerializeField, Min(0f), Tooltip("Dash distance along the arc in world units.")]
    private float moveDistance = 1f;
    [SerializeField, Min(0.01f), Tooltip("Top dash speed in world units per second.")]
    private float maxDashSpeed = 14f;
    [SerializeField, Min(0.01f), Tooltip("How quickly the dash gains speed, in world units per second squared.")]
    private float dashAcceleration = 90f;
    [SerializeField, Min(0.01f), Tooltip("How quickly the dash loses speed. Lower values give a longer falloff.")]
    private float dashDeceleration = 60f;
    [SerializeField, Tooltip("World-space height offset from the line to the landing surface.")]
    private float landingHeightOffset;
    [SerializeField] private OVRCameraRig playerRig;
    [SerializeField, Tooltip("The Player root to move when no locomotor is assigned.")]
    private Transform playerRoot;
    [SerializeField, Tooltip("Assign this if Meta's First Person Locomotor controls the player.")]
    private FirstPersonLocomotor locomotor;
    [SerializeField, Tooltip("Optional when this scene has one active Move Detection component.")]
    private MoveDetection moveDetection;

    private LineRenderer line;
    private MoveDetection activeMoveDetection;
    private Coroutine activeDash;
    private float drawnRadius;
    private int drawnSegments;

    private void OnEnable()
    {
        line = GetComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = true;
        DrawCircle();

        if (!Application.IsPlaying(gameObject))
            return;

        if (playerRig == null)
            playerRig = FindSingleSceneComponent<OVRCameraRig>();
        if (playerRoot == null && playerRig != null)
            playerRoot = playerRig.transform.root;

        activeMoveDetection = moveDetection != null ? moveDetection : FindSingleSceneComponent<MoveDetection>();
        if (activeMoveDetection != null)
            activeMoveDetection.LoadoutMovePerformed += HandleLoadoutMove;
    }

    private void OnDisable()
    {
        if (activeMoveDetection != null)
            activeMoveDetection.LoadoutMovePerformed -= HandleLoadoutMove;
        activeMoveDetection = null;

        if (activeDash != null)
            StopCoroutine(activeDash);
        activeDash = null;
    }

    private void HandleLoadoutMove(MoveSlot slot)
    {
        // Facing the centre, increasing the circle's angle moves to the player's right.
        switch (slot)
        {
            case MoveSlot.DashLeft:
                MovePlayerAlongCircle(-1f);
                break;
            case MoveSlot.DashRight:
                MovePlayerAlongCircle(1f);
                break;
        }
    }

    private T FindSingleSceneComponent<T>() where T : Component
    {
        T match = null;
        foreach (T component in FindObjectsByType<T>())
        {
            if (component.gameObject.scene != gameObject.scene)
                continue;
            if (match != null)
                return null;
            match = component;
        }
        return match;
    }

    private void Update()
    {
        if (radius != drawnRadius || segments != drawnSegments)
            DrawCircle();
    }

    public Vector3 GetWorldPoint(float angleDegrees)
    {
        return transform.TransformPoint(GetLocalPoint(angleDegrees));
    }

    public Vector3 GetClosestWorldPoint(Vector3 worldPosition)
    {
        return GetWorldPoint(GetAngle(worldPosition));
    }

    internal bool TryGetForwardEdgeDistance(Vector3 origin, Vector3 direction, out float distance)
    {
        // Keep the ray parameter in world metres even if the arena is scaled.
        Vector3 localOrigin = transform.InverseTransformPoint(origin);
        Vector3 localDirection = transform.InverseTransformVector(direction);
        Vector2 offset = new(localOrigin.x, localOrigin.z);
        Vector2 ray = new(localDirection.x, localDirection.z);
        float a = ray.sqrMagnitude;
        float b = Vector2.Dot(offset, ray);
        float discriminant = b * b - a * (offset.sqrMagnitude - radius * radius);
        distance = 0f;
        if (a < 0.000001f || discriminant < 0f)
            return false;

        float root = Mathf.Sqrt(discriminant);
        float near = (-b - root) / a;
        distance = near > 0f ? near : (-b + root) / a;
        return distance > 0f;
    }

    // Positive direction moves from local +X toward +Z. Negative reverses it.
    public void MovePlayerAlongCircle(float direction)
    {
        if (playerRig == null || playerRig.centerEyeAnchor == null)
            return;

        MovePlayerAlongCircle(direction, playerRig.centerEyeAnchor.position);
    }

    public void MovePlayerAlongCircle(float direction, Vector3 closestPoint)
    {
        if (!Application.IsPlaying(gameObject) || !isActiveAndEnabled || activeDash != null)
            return;

        float worldRadius = transform.TransformVector(Vector3.right * radius).magnitude;
        if (direction == 0f || moveDistance <= 0f || radius <= 0f || worldRadius <= 0f ||
            maxDashSpeed <= 0f || dashAcceleration <= 0f || dashDeceleration <= 0f)
            return;

        bool hasStandingPosition = TryGetStandingPosition(out Vector3 standingPosition);
        if (locomotor == null && (playerRoot == null || !hasStandingPosition))
            return;

        float startAngle = GetAngle(closestPoint);
        float degreesPerUnit = Mathf.Sign(direction) / worldRadius * Mathf.Rad2Deg;
        Vector3 startingOffset = hasStandingPosition
            ? standingPosition - GetLandingPoint(startAngle) : Vector3.zero;
        var profile = new DashSpeedProfile(moveDistance, maxDashSpeed, dashAcceleration, dashDeceleration);
        activeDash = StartCoroutine(DashAlongCircle(profile, startAngle, degreesPerUnit, startingOffset));
    }

    private IEnumerator DashAlongCircle(DashSpeedProfile profile, float startAngle, float degreesPerUnit,
        Vector3 startingOffset)
    {
        float elapsed = 0f;
        float previousAngleOffset = 0f;
        while (elapsed < profile.Duration)
        {
            yield return null;

            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f)
                continue;

            elapsed = Mathf.Min(elapsed + deltaTime, profile.Duration);
            float travelled = profile.DistanceAt(elapsed);
            float angleOffset = travelled * degreesPerUnit;
            Vector3 destination = GetLandingPoint(startAngle + angleOffset);
            // Ease onto the arc if the player started off it, without snapping at either end.
            destination += (1f - travelled / profile.Distance) * startingOffset;
            // Increasing the circle's angle turns from +X toward +Z, opposite positive Unity yaw.
            Quaternion turn = Quaternion.AngleAxis(previousAngleOffset - angleOffset, Vector3.up);
            if (!ApplyDashStep(destination, turn))
                break;

            previousAngleOffset = angleOffset;
        }

        activeDash = null;
    }

    private bool ApplyDashStep(Vector3 destination, Quaternion turn)
    {
        if (locomotor != null)
        {
            locomotor.HandleLocomotionEvent(new LocomotionEvent(
                0, new Pose(destination, turn),
                LocomotionEvent.TranslationType.Absolute, LocomotionEvent.RotationType.Relative));
            return true;
        }

        if (playerRoot == null || !TryGetStandingPosition(out Vector3 standingPosition))
            return false;

        // Pivot around the tracked standing point so the turn does not shift the landing point.
        Vector3 rootOffset = playerRoot.position - standingPosition;
        playerRoot.SetPositionAndRotation(destination + turn * rootOffset, turn * playerRoot.rotation);
        return true;
    }

    private bool TryGetStandingPosition(out Vector3 standingPosition)
    {
        standingPosition = default;
        if (playerRig == null || playerRig.centerEyeAnchor == null || playerRig.trackingSpace == null)
            return false;

        standingPosition = playerRig.centerEyeAnchor.position;
        standingPosition.y = playerRig.trackingSpace.position.y;
        return true;
    }

    private Vector3 GetLandingPoint(float angle) => GetWorldPoint(angle) + Vector3.up * landingHeightOffset;

    private float GetAngle(Vector3 worldPosition)
    {
        Vector3 localPosition = transform.InverseTransformPoint(worldPosition);
        return Mathf.Atan2(localPosition.z, localPosition.x) * Mathf.Rad2Deg;
    }

    private Vector3 GetLocalPoint(float angleDegrees)
    {
        float angle = angleDegrees * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
    }

    private void DrawCircle()
    {
        line.positionCount = segments;

        for (int i = 0; i < segments; i++)
        {
            float angle = 360f * i / segments;
            line.SetPosition(i, GetLocalPoint(angle));
        }

        drawnRadius = radius;
        drawnSegments = segments;
    }

    private readonly struct DashSpeedProfile
    {
        private readonly float acceleration;
        private readonly float deceleration;
        private readonly float peakSpeed;
        private readonly float accelerationTime;
        private readonly float accelerationDistance;
        private readonly float decelerationStart;

        public float Distance { get; }
        public float Duration { get; }

        public DashSpeedProfile(float distance, float maxSpeed, float acceleration, float deceleration)
        {
            Distance = distance;
            this.acceleration = acceleration;
            this.deceleration = deceleration;

            // Short dashes start braking before they reach the configured speed cap.
            peakSpeed = Mathf.Min(maxSpeed, Mathf.Sqrt(2f * distance / (1f / acceleration + 1f / deceleration)));
            accelerationTime = peakSpeed / acceleration;
            float decelerationTime = peakSpeed / deceleration;
            accelerationDistance = 0.5f * peakSpeed * accelerationTime;
            float decelerationDistance = 0.5f * peakSpeed * decelerationTime;
            float cruiseTime = Mathf.Max(0f, distance - accelerationDistance - decelerationDistance) / peakSpeed;
            decelerationStart = accelerationTime + cruiseTime;
            Duration = decelerationStart + decelerationTime;
        }

        public float DistanceAt(float elapsed)
        {
            if (elapsed >= Duration)
                return Distance;
            if (elapsed < accelerationTime)
                return 0.5f * acceleration * elapsed * elapsed;
            if (elapsed < decelerationStart)
                return accelerationDistance + peakSpeed * (elapsed - accelerationTime);

            float remaining = Duration - elapsed;
            return Distance - 0.5f * deceleration * remaining * remaining;
        }
    }
}
