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
        [Header("First-person chest clearance")]
        [SerializeField] private bool adjustBodyWhenLookingDown=true;
        [SerializeField,Range(0,80)] private float lookDownStartAngle=20;
        [SerializeField,Range(1,89)] private float lookDownFullAngle=70;
        [SerializeField,Range(0,.3f),Tooltip("Maximum upper-body retreat in metres. Feet and tracked hands stay anchored.")]
        private float lookDownBackoff=.12f;
        [SerializeField,Range(0,20)] private float lookDownLeanDegrees=8;
        [SerializeField,Min(.01f)] private float lookDownResponseSeconds=.12f;

        private bool retargeterWasEnabled;
        private bool manualControl;
        private HandJoint[] handJoints;
        private CharacterRetargeter originalAvatar;
        private GameObject equippedVisual;
        private LocalAvatarHeadVisibility headVisibility;
        private System.Func<string,bool> canAlignTrackedBone;
        private OVRCameraRig cameraRig;
        private float lookDownWeight;
        private readonly StableBodyHeading bodyHeading=new();

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

            originalAvatar = avatar;
            cameraRig=GetComponentInChildren<OVRCameraRig>(true);
            canAlignTrackedBone=CanAlignTrackedBone;
            headVisibility=gameObject.AddComponent<LocalAvatarHeadVisibility>();
            Rig = new AvatarPoseRig(avatar);
            retargeterWasEnabled = avatar.enabled;
            handJoints = System.Array.FindAll(GetComponentsInChildren<HandJoint>(true), joint =>
                joint.transform.parent != null && joint.transform.parent.TryGetComponent<PokeInteractor>(out _));
            ConfigureHeadVisibility();
            if (DownloadedAssetLibrary.Equipped != DownloadedAssetLibrary.DefaultCharacter)
                try { EquipCharacter(DownloadedAssetLibrary.Equipped); }
                catch (System.Exception e) when (AnimationStore.IsFileError(e))
                { Debug.LogWarning("Using StylizedCharacter: " + e.Message, this); }
        }

        internal void EquipCharacter(string id)
        {
            if (originalAvatar == null) return; // Inactive scene objects initialize from the preference in Awake.
            Rig?.RestoreHandPose();
            Rig?.RestoreBodyPosture();
            Rig?.Reset(); // Preserve a neutral bind pose when the default model is equipped again later.
            bool visible = System.Array.Exists(avatar.GetComponentsInChildren<Renderer>(true), r=>r.enabled);
            bool wasActive=originalAvatar.gameObject.activeSelf;
            CharacterRetargeter replacement=originalAvatar;
            GameObject instance=null;
            if (id != DownloadedAssetLibrary.DefaultCharacter)
            {
                GameObject prefab=DownloadedAssetLibrary.ResolveCharacter(id);
                var holder=new GameObject("Equipped player character"); holder.SetActive(false);
                holder.transform.SetParent(originalAvatar.transform.parent,false);
                instance=Instantiate(prefab,holder.transform,false);
                replacement=instance.GetComponentInChildren<CharacterRetargeter>(true);
                replacement.transform.SetLocalPositionAndRotation(originalAvatar.transform.localPosition,originalAvatar.transform.localRotation);
                replacement.transform.localScale=originalAvatar.transform.localScale;
                instance=holder;
            }
            avatar.enabled=false;
            if (equippedVisual!=null) { equippedVisual.SetActive(false); Destroy(equippedVisual); }
            originalAvatar.gameObject.SetActive(id==DownloadedAssetLibrary.DefaultCharacter);
            avatar=replacement; equippedVisual=instance;
            var originalRenderer=originalAvatar.GetComponentInChildren<Renderer>(true);
            foreach (Renderer renderer in avatar.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled=visible;
                if(originalRenderer!=null)renderer.gameObject.layer=originalRenderer.gameObject.layer;
            }
            Rig=new AvatarPoseRig(avatar);
            ConfigureHeadVisibility();
            if (instance!=null) instance.SetActive(wasActive || id!=DownloadedAssetLibrary.DefaultCharacter);
            UpdateRetargeter();
        }
        private void ConfigureHeadVisibility()
        {
            var mapping=avatar.GetComponent<CharacterBoneMap>();
            headVisibility.Configure(mapping!=null?mapping.firstPersonHiddenRenderers:null,cameraRig);
        }

        private void OnEnable() => UpdateRetargeter();

        private void Update()
        {
            Rig.RestoreHandPose();
            Rig.RestoreBodyPosture();
            UpdateRetargeter();
        }

        private void OnDisable()
        {
            if (Rig != null)
            {
                Rig.RestoreHandPose();
                Rig.RestoreBodyPosture();
                avatar.enabled = retargeterWasEnabled;
            }
        }

        internal void SetManualControl(bool active)
        {
            Rig?.RestoreHandPose();
            Rig?.RestoreBodyPosture();
            if(active)lookDownWeight=0;
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

            if(cameraRig!=null && cameraRig.centerEyeAnchor!=null)
            {
                var space=tracker.PlayerSpace;
                bodyHeading.Update(cameraRig.centerEyeAnchor.rotation,space.rotation,space.up,Time.unscaledDeltaTime);
                Rig.ConfigureBodyStability(bodyHeading.Holding,cameraRig.centerEyeAnchor.position,Time.unscaledDeltaTime);
            }

            if (avatar.isActiveAndEnabled && avatar.RetargeterValid &&
                tracker.Frame.Mode is TrackingMode.FullBody or TrackingMode.UpperBody)
            {
                Rig.RememberRetargetedScale();
                Rig.PlaceRetargetedBody(tracker.Frame, tracker.PlayerSpace);
                Rig.ApplyCustomMappings(tracker.Frame, tracker.PlayerSpace, tracker.Mappings);
            }
            else
                Rig.ApplyLive(tracker.Frame, tracker.PlayerSpace);

            ApplyLookDownPosture();

            if (tracker.Input is PlayerPoseTracker.InputMode.Automatic or PlayerPoseTracker.InputMode.HandsAndHead)
                AlignHands();
        }
        private void ApplyLookDownPosture()
        {
            if(cameraRig==null || cameraRig.centerEyeAnchor==null)return;
            Vector3 up=tracker.PlayerSpace.up,look=cameraRig.centerEyeAnchor.forward;
            float target=adjustBodyWhenLookingDown?LookDownAmount(look,up,lookDownStartAngle,lookDownFullAngle):0;
            if(bodyHeading.Holding)target=lookDownWeight;
            lookDownWeight=Mathf.Lerp(lookDownWeight,target,1-Mathf.Exp(-Time.unscaledDeltaTime/Mathf.Max(.01f,lookDownResponseSeconds)));
            Rig.ApplyBodyPosture(bodyHeading.Forward,up,lookDownBackoff*lookDownWeight,lookDownLeanDegrees*lookDownWeight);
        }
        internal static float LookDownAmount(Vector3 look,Vector3 up,float start,float full)
        {
            float pitch=Mathf.Atan2(-Vector3.Dot(look,up),Vector3.ProjectOnPlane(look,up).magnitude)*Mathf.Rad2Deg;
            return Mathf.SmoothStep(0,1,Mathf.InverseLerp(start,Mathf.Max(start+1,full),pitch));
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
                // A valid ISDK/OpenXR hand can exist even when the legacy OVR tracking-point reader has no sample.
                if (!Rig.UsesTrackedFingerPositions && !points.ContainsKey(side + "Hand/Wrist") && !points.ContainsKey(side + "LegacyHand/WristRoot"))
                    continue;
                if (HasCustomMapping(side + "Wrist"))
                    continue;

                Vector3? pokePoint = joint.isActiveAndEnabled ? joint.transform.position : null;
                bool alignIndex = !HasCustomMapping(side + "IndexProximal") &&
                    !HasCustomMapping(side + "IndexIntermediate") && !HasCustomMapping(side + "IndexDistal");
                Rig.AlignTrackedHand(side, hand, pokePoint, tracker.PlayerSpace, alignIndex,canAlignTrackedBone);
            }
        }

        private bool HasCustomMapping(string bone) => tracker.Frame.TryGet(bone, out TrackedBone tracked) &&
            tracker.Mappings.HasOverride(tracked.SourcePoint);
        private bool CanAlignTrackedBone(string bone) => !HasCustomMapping(bone);
    }
}
