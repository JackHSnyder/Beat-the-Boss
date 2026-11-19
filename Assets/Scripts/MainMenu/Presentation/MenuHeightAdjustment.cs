using System;
using UnityEngine;
using UnityEngine.XR;

// Shared posture detection for world-space menus. Height is measured in tracking space,
// so moving the observer around an elevated arena does not count as standing up.
[Serializable]
public sealed class MenuHeightAdjustment
{
    [SerializeField] private bool enabled = true;
    [SerializeField, Min(0f), Tooltip("Tracked eye height in metres below which the menu can enter seated mode.")]
    private float seatedThreshold = 1.35f;
    [SerializeField, Min(0f), Tooltip("Eye height to exceed before returning to standing mode. Keep above the seated threshold to prevent jitter.")]
    private float standingThreshold = 1.50f;
    [SerializeField, Min(0f), Tooltip("Seconds continuously beyond a threshold before changing menu height.")]
    private float dwellSeconds = 1.5f;
    [SerializeField, Min(0f), Tooltip("Seconds for the vertical slide after detecting a posture change.")]
    private float slideSeconds = .4f;
    [SerializeField, Tooltip("Standing eye height used by menus with fixed scene positions.")]
    private float referenceEyeHeight = 1.65f;

    private bool seated;
    private float dwell, baseline = float.NaN, offset, from, target, elapsed;
    public float Offset => offset;

    public float Tick(Camera camera)
    {
        if (!enabled || !TryReadHeight(camera, out float height))
        {
            dwell = 0;
            return 0;
        }
        return Advance(height, Time.unscaledDeltaTime);
    }

    // Eye-relative menus already accommodate the current posture when recentered.
    // Rebase rather than adding a second seated offset to that placement.
    public void Recenter(Camera camera)
    {
        if (!TryReadHeight(camera, out float height)) return;
        baseline = height;
        seated = height < seatedThreshold;
        offset = from = target = elapsed = dwell = 0;
    }

    private static bool TryReadHeight(Camera camera, out float height)
    {
        height = 0;
        if (camera == null || !XRSettings.isDeviceActive) return false;
        var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
        bool tracked = head.TryGetFeatureValue(CommonUsages.isTracked, out bool valid) && valid;
        tracked |= OVRPlugin.GetNodePositionTracked(OVRPlugin.Node.EyeCenter);
        if (!tracked) return false;
        var rig = camera.GetComponentInParent<OVRCameraRig>();
        if (rig == null || rig.trackingSpace == null) return false;
        height = rig.trackingSpace.InverseTransformPoint(camera.transform.position).y;
        return float.IsFinite(height) && height > .1f;
    }

    internal float Advance(float height, float deltaTime)
    {
        if (float.IsNaN(baseline)) baseline = referenceEyeHeight;
        float previous = offset;
        bool crossed = seated ? height > Mathf.Max(standingThreshold, seatedThreshold + .01f)
            : height < seatedThreshold;
        dwell = crossed ? dwell + deltaTime : 0;
        if (crossed && dwell >= dwellSeconds)
        {
            seated = !seated;
            dwell = 0;
            from = offset;
            target = height - baseline;
            elapsed = 0;
        }
        elapsed += deltaTime;
        float progress = slideSeconds <= 0 || SceneHandler.ReducedTransitions ? 1 : Mathf.Clamp01(elapsed / slideSeconds);
        offset = Mathf.Lerp(from, target, Mathf.SmoothStep(0, 1, progress));
        return offset - previous;
    }
}
