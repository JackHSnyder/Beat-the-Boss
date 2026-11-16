using BeatTheBoss.Poses;
using Oculus.Interaction.Locomotion;
using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(LineRenderer))]
public class MoveCircle : MonoBehaviour
{
    [Min(0f)] public float radius = 3f;
    [SerializeField, Min(3)] private int segments = 96;
    [SerializeField, Min(0f)] private float moveDistance = 1f;
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
    }

    private void HandleLoadoutMove(MoveSlot slot, string moveName)
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

    // Positive direction moves from local +X toward +Z. Negative reverses it.
    public void MovePlayerAlongCircle(float direction)
    {
        if (playerRig == null || playerRig.centerEyeAnchor == null)
            return;

        MovePlayerAlongCircle(direction, playerRig.centerEyeAnchor.position);
    }

    public void MovePlayerAlongCircle(float direction, Vector3 closestPoint)
    {
        float worldRadius = transform.TransformVector(Vector3.right * radius).magnitude;
        if (direction == 0f || moveDistance <= 0f || radius <= 0f || worldRadius <= 0f)
            return;

        float angleOffset = Mathf.Sign(direction) * moveDistance / worldRadius * Mathf.Rad2Deg;
        Vector3 destination = GetWorldPoint(GetAngle(closestPoint) + angleOffset);
        destination.y += landingHeightOffset;
        // Increasing the circle's angle turns from +X toward +Z, opposite positive Unity yaw.
        Quaternion turn = Quaternion.AngleAxis(-angleOffset, Vector3.up);

        if (locomotor != null)
        {
            locomotor.HandleLocomotionEvent(new LocomotionEvent(
                0, new Pose(destination, turn),
                LocomotionEvent.TranslationType.Absolute, LocomotionEvent.RotationType.Relative));
        }
        else if (playerRoot != null && playerRig != null &&
                 playerRig.centerEyeAnchor != null && playerRig.trackingSpace != null)
        {
            Vector3 standingPosition = playerRig.centerEyeAnchor.position;
            standingPosition.y = playerRig.trackingSpace.position.y;
            // Pivot around the tracked standing point so the turn does not shift the landing point.
            Vector3 rootOffset = playerRoot.position - standingPosition;
            playerRoot.SetPositionAndRotation(destination + turn * rootOffset, turn * playerRoot.rotation);
        }
    }

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
}
