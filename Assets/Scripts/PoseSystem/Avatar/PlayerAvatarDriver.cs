// Drives the live avatar using Meta retargeting or the universal skeleton, depending on the tracking input.
// The Player prefab runs it alongside PlayerPoseTracker. PoseWorkbench pauses it during model tests.

using Meta.XR.Movement.Retargeting;
using Oculus.Interaction;
using Oculus.Interaction.Input;
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
        private HandJoint[] handJoints;

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
            handJoints = System.Array.FindAll(GetComponentsInChildren<HandJoint>(true), joint =>
                joint.transform.parent != null && joint.transform.parent.TryGetComponent<PokeInteractor>(out _));
        }

        private void OnEnable() => UpdateRetargeter();

        private void Update()
        {
            Rig.RestoreHandPose();
            UpdateRetargeter();
        }

        private void OnDisable()
        {
            if (Rig != null)
            {
                Rig.RestoreHandPose();
                avatar.enabled = retargeterWasEnabled;
            }
        }

        internal void SetManualControl(bool active)
        {
            Rig?.RestoreHandPose();
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
                Rig.PlaceRetargetedBody(tracker.Frame, tracker.PlayerSpace);
                Rig.ApplyCustomMappings(tracker.Frame, tracker.PlayerSpace, tracker.Mappings);
            }
            else
                Rig.ApplyLive(tracker.Frame, tracker.PlayerSpace);

            if (tracker.Input is PlayerPoseTracker.InputMode.Automatic or PlayerPoseTracker.InputMode.HandsAndHead)
                AlignHands();
        }

        private void AlignHands()
        {
            foreach (HandJoint joint in handJoints)
            {
                IHand hand = joint.Hand;
                if (hand == null || !hand.IsConnected || !hand.IsTrackedDataValid)
                    continue;

                string side = hand.Handedness == Handedness.Left ? "Left" : "Right";
                var points = tracker.Points.Points;
                if (!points.ContainsKey(side + "Hand/Wrist") && !points.ContainsKey(side + "LegacyHand/WristRoot"))
                    continue;
                if (HasCustomMapping(side + "Wrist"))
                    continue;

                Vector3? pokePoint = joint.isActiveAndEnabled ? joint.transform.position : null;
                bool alignIndex = !HasCustomMapping(side + "IndexProximal") &&
                    !HasCustomMapping(side + "IndexIntermediate") && !HasCustomMapping(side + "IndexDistal");
                Rig.AlignTrackedHand(side, hand, pokePoint, tracker.PlayerSpace, alignIndex);
            }
        }

        private bool HasCustomMapping(string bone) => tracker.Frame.TryGet(bone, out TrackedBone tracked) &&
            tracker.Mappings.HasOverride(tracked.SourcePoint);
    }
}
