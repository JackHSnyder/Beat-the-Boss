// Drives the live avatar using Meta retargeting or the universal skeleton, depending on the tracking input.
// The Player prefab runs it alongside PlayerPoseTracker. PoseWorkbench pauses it during model tests.

using Meta.XR.Movement.Retargeting;
using Oculus.Interaction;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    [DefaultExecutionOrder(150)]
    public sealed class PlayerAvatarDriver : MonoBehaviour
    {
        [SerializeField] private PlayerPoseTracker tracker;
        [SerializeField] private CharacterRetargeter avatar;
        [SerializeField] private bool hideDefaultHandVisuals;

        private bool retargeterWasEnabled;
        private bool manualControl;

        internal AvatarPoseRig Rig { get; private set; }

        private void Awake()
        {
            // HandVisual manages its renderer as tracking changes. Use its visibility override so the rig stays active.
            if (hideDefaultHandVisuals)
            {
                foreach (HandVisual visual in GetComponentsInChildren<HandVisual>(true))
                    visual.ForceOffVisibility = true;
            }

            if (tracker == null || avatar == null)
            {
                Debug.LogError("Assign the player tracker and character retargeter.", this);
                enabled = false;
                return;
            }

            Rig = new AvatarPoseRig(avatar);
            retargeterWasEnabled = avatar.enabled;
        }

        private void OnEnable() => UpdateRetargeter();
        private void Update() => UpdateRetargeter();

        private void OnDisable()
        {
            if (Rig != null)
                avatar.enabled = retargeterWasEnabled;
        }

        internal void SetManualControl(bool active)
        {
            manualControl = active;
            if (isActiveAndEnabled)
                UpdateRetargeter();
        }

        private void UpdateRetargeter()
        {
            // Explicit tracking modes use the universal skeleton driver.
            avatar.enabled = retargeterWasEnabled && !manualControl &&
                tracker.Input == PlayerPoseTracker.InputMode.Automatic;
        }

        private void LateUpdate()
        {
            if (manualControl)
                return;

            if (avatar.isActiveAndEnabled && avatar.RetargeterValid &&
                tracker.Frame.Mode is TrackingMode.FullBody or TrackingMode.UpperBody)
            {
                Rig.RememberRetargetedScale();
                Rig.ApplyCustomMappings(tracker.Frame, tracker.PlayerSpace, tracker.Mappings);
            }
            else
                Rig.ApplyLive(tracker.Frame, tracker.PlayerSpace);
        }
    }
}
