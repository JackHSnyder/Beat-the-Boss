// Collects Meta and external tracking points and publishes a live frame using universal bone IDs.
// PlayerAvatarDriver animates from that frame. PoseWorkbench and MoveDetection use it for matching.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine.XR;
using UnityEngine;
using static OVRPlugin;
using Bone = OVRPlugin.Bone;
using Hand = OVRPlugin.Hand;

namespace BeatTheBoss.Poses
{
    [DefaultExecutionOrder(100)]
    public sealed class PlayerPoseTracker : MonoBehaviour
    {
        public enum InputMode { Automatic, HandsAndHead, ControllersAndHead, ExternalOnly }

        [SerializeField] private OVRCameraRig cameraRig;
        [SerializeField] private OVRBody body;
        [SerializeField] private InputMode input;

        private static readonly string[] BodyIds = MakeIds("FullBody_", "Body/", (int)BoneId.FullBody_End);
        private readonly HandReader left = new(true);
        private readonly HandReader right = new(false);
        private Skeleton2 bodySkeleton;
        private SkeletonType bodySkeletonType = SkeletonType.None;
        private uint bodySkeletonRevision;

        [SerializeField] private MonoBehaviour[] additionalSources = Array.Empty<MonoBehaviour>();
        private readonly TrackingPointBuffer points = new();
        private readonly List<ITrackingPointSource> registeredSources = new();
        private Matrix4x4 playerFromTracking;
        private Quaternion playerFromTrackingRotation;

        public BoneMappings Mappings { get; private set; }
        public TrackingPointBuffer Points => points;
        public void RegisterSource(ITrackingPointSource source)
        {
            if (source != null && !registeredSources.Contains(source))
                registeredSources.Add(source);
        }

        public void UnregisterSource(ITrackingPointSource source) => registeredSources.Remove(source);

        private void Awake()
        {
            string path = Path.Combine(Application.persistentDataPath, "Tracking", "bone-mappings.json");
            try { Mappings = new BoneMappings(path); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            {
                Debug.LogWarning("Could not load tracking mappings. " + error.Message, this);
                Mappings = new BoneMappings(path, load: false);
            }
        }

        public TrackedPose Frame { get; } = new();
        public InputMode Input { get => input; set => input = value; }
        public Transform TrackingSpace => cameraRig != null && cameraRig.trackingSpace != null
            ? cameraRig.trackingSpace : transform;
        public Transform PlayerSpace => transform.parent != null ? transform.parent : transform;

        // Convert native coordinates at the source boundary. Matching sees only universal bones.
        private void LateUpdate()
        {
            points.Clear();
            Transform player = PlayerSpace;
            Transform tracking = TrackingSpace;
            playerFromTracking = player.worldToLocalMatrix * tracking.localToWorldMatrix;
            playerFromTrackingRotation = Quaternion.Inverse(player.rotation) * tracking.rotation;

            TrackingMode mode = TrackingMode.None;
            if (input != InputMode.ExternalOnly)
            {
                if (input == InputMode.Automatic)
                    mode = ReadBody();
                ReadDevices();
                if (mode == TrackingMode.None && points.Points.Count > 0)
                    mode = TrackingMode.Devices;
            }

            foreach (MonoBehaviour component in additionalSources)
                if (component != null && component.isActiveAndEnabled && component is ITrackingPointSource source)
                    source.ReadTrackingPoints(player, points);
            for (int i = 0; i < registeredSources.Count; i++)
                registeredSources[i].ReadTrackingPoints(player, points);
            if (mode == TrackingMode.None && points.Points.Count > 0)
                mode = TrackingMode.External;
            Mappings.BuildFrame(points, mode, Frame);
        }

        private void OnDisable()
        {
            Frame.Clear(TrackingMode.None);
            points.Clear();
        }

        private void AddPoint(string id, Pose pose, Quaternion neutral, bool orientation = true)
        {
            points.Add(id, new Pose(playerFromTracking.MultiplyPoint3x4(pose.position),
                playerFromTrackingRotation * pose.rotation), neutral, orientation);
        }

        private TrackingMode ReadBody()
        {
            if (body == null || !body.isActiveAndEnabled || body.BodyState is not BodyState state ||
                state.JointLocations == null)
                return TrackingMode.None;

            var joints = state.JointLocations;
            if (joints.Length < (int)BoneId.Body_End ||
                (!IsValid(joints[(int)BoneId.FullBody_Head]) && !IsValid(joints[(int)BoneId.FullBody_Hips])))
                return TrackingMode.None;

            // Joint poses change every frame. Refresh the cached parent indices when the SDK reports a skeleton change.
            SkeletonType type = joints.Length >= BodyIds.Length ? SkeletonType.FullBody : SkeletonType.Body;
            if (bodySkeletonType != type || bodySkeletonRevision != state.SkeletonChangedCount)
            {
                if (!GetSkeleton2(type, ref bodySkeleton) || bodySkeleton.Bones == null)
                    return TrackingMode.None;

                bodySkeletonType = type;
                bodySkeletonRevision = state.SkeletonChangedCount;
            }

            bool hasLegData = Array.FindIndex(joints, (int)BoneId.Body_End, IsValid) >= 0;
            foreach (Bone definition in bodySkeleton.Bones)
            {
                int index = (int)definition.Id;
                if ((uint)index >= BodyIds.Length || index >= joints.Length || !joints[index].PositionValid)
                    continue;

                // Meta body bind poses are expressed in tracking space.
                AddPoint(BodyIds[index], ToUnity(joints[index].Pose),
                    definition.Pose.Orientation.FromFlippedZQuatf(), joints[index].OrientationValid);
            }
            return hasLegData ? TrackingMode.FullBody : TrackingMode.UpperBody;
        }

        private void ReadDevices()
        {
            if (GetNodePositionTracked(Node.EyeCenter) && GetNodeOrientationTracked(Node.EyeCenter))
                AddPoint("Devices/Head", ToUnity(GetNodePose(Node.EyeCenter, Step.Render)), Quaternion.identity);
            else
                ReadXrNode(XRNode.Head, "XR/Head");

            // Submit every available source. The mapping decides which point drives a joint.
            if (input != InputMode.ControllersAndHead)
            {
                left.Read(AddPoint);
                right.Read(AddPoint);
            }
            if (input != InputMode.HandsAndHead)
            {
                ReadController(true);
                ReadController(false);
            }
        }

        private void ReadController(bool isLeft)
        {
            OVRInput.Controller controller = isLeft ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
            if (OVRInput.IsControllerConnected(controller) && OVRInput.GetControllerPositionTracked(controller) &&
                OVRInput.GetControllerOrientationTracked(controller))
            {
                AddPoint(isLeft ? "Devices/LeftController" : "Devices/RightController",
                    new Pose(OVRInput.GetLocalControllerPosition(controller), OVRInput.GetLocalControllerRotation(controller)),
                    Quaternion.identity);
            }
            else
                ReadXrNode(isLeft ? XRNode.LeftHand : XRNode.RightHand, isLeft ? "XR/LeftController" : "XR/RightController");
        }

        // Unity XR covers head and controllers on an active OpenXR runtime, including SteamVR.
        private void ReadXrNode(XRNode node, string id)
        {
            InputDevice device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) || !tracked ||
                !device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 position))
                return;
            bool oriented = device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rotation);
            AddPoint(id, new Pose(position, rotation), Quaternion.identity, oriented);
        }

        internal static Quaternion Heading(Quaternion rotation)
        {
            Vector3 forward = Vector3.ProjectOnPlane(rotation * Vector3.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.Cross(rotation * Vector3.right, Vector3.up);
            return Quaternion.LookRotation(forward, Vector3.up);
        }

        private static bool IsValid(BodyJointLocation joint) => joint.PositionValid && joint.OrientationValid;

        private static Pose ToUnity(Posef pose) =>
            new(pose.Position.FromFlippedZVector3f(), pose.Orientation.FromFlippedZQuatf());

        private static string[] MakeIds(string prefix, string path, int count)
        {
            var ids = new string[count];
            foreach (string name in Enum.GetNames(typeof(BoneId)))
            {
                if (!name.StartsWith(prefix, StringComparison.Ordinal) || name == prefix + "Start" ||
                    name == prefix + "MaxSkinnable")
                    continue;

                int index = (int)(BoneId)Enum.Parse(typeof(BoneId), name);
                if ((uint)index < count)
                    ids[index] = path + name.Substring(prefix.Length);
            }
            return ids;
        }

        private sealed class HandReader
        {
            private readonly bool isLeft;
            private readonly string[] xrIds;
            private readonly string[] legacyIds;
            private readonly Pose[] poses = new Pose[(int)BoneId.XRHand_End];
            private readonly Pose[] neutral = new Pose[(int)BoneId.XRHand_End];
            private readonly bool[] neutralResolved = new bool[(int)BoneId.XRHand_End];
            private readonly bool[] resolved = new bool[(int)BoneId.XRHand_End];
            private HandState state;
            private Skeleton2 skeleton;
            private SkeletonType skeletonType = SkeletonType.None;

            public HandReader(bool isLeft)
            {
                this.isLeft = isLeft;
                string side = isLeft ? "Left" : "Right";
                xrIds = MakeIds("XRHand_", side + "Hand/", (int)BoneId.XRHand_End);
                legacyIds = MakeIds("Hand_", side + "LegacyHand/", (int)BoneId.Hand_End);
            }

            public void Read(Action<string, Pose, Quaternion, bool> submit)
            {
                if (!GetHandState(Step.Render, isLeft ? Hand.HandLeft : Hand.HandRight, ref state) ||
                    (state.Status & HandStatus.HandTracked) == 0 || state.BoneRotations == null)
                    return;

                bool xr = HandSkeletonVersion == OVRHandSkeletonVersion.OpenXR &&
                    state.BonePositions?.Length == xrIds.Length;
                SkeletonType type = xr
                    ? (isLeft ? SkeletonType.XRHandLeft : SkeletonType.XRHandRight)
                    : (isLeft ? SkeletonType.HandLeft : SkeletonType.HandRight);
                if (skeletonType != type)
                {
                    if (!GetSkeleton2(type, ref skeleton) || skeleton.Bones == null)
                        return;
                    skeletonType = type;
                    Array.Clear(neutralResolved, 0, neutralResolved.Length);
                }

                string[] ids = xr ? xrIds : legacyIds;
                Pose root = ToUnity(state.RootPose);
                Array.Clear(resolved, 0, resolved.Length);
                for (int index = 0; index < skeleton.Bones.Length; index++)
                {
                    Bone definition = skeleton.Bones[index];
                    int id = (int)definition.Id;
                    if ((uint)id >= ids.Length)
                        continue;

                    if (xr)
                    {
                        poses[index] = new Pose(state.BonePositions[id].FromFlippedZVector3f(),
                            state.BoneRotations[id].FromFlippedZQuatf());
                        resolved[index] = true;
                    }
                    else
                    {
                        ResolveLegacy(index, root);
                    }
                }

                for (int index = 0; index < skeleton.Bones.Length; index++)
                {
                    if (!resolved[index])
                        continue;

                    Bone definition = skeleton.Bones[index];
                    int id = (int)definition.Id;
                    bool hasRotation = xr || id < (int)BoneId.Hand_MaxSkinnable;
                    ResolveNeutral(index, xr);
                    submit(ids[id], poses[index], neutral[index].rotation, hasRotation);
                }
            }

            private void ResolveNeutral(int index, bool xr)
            {
                if (neutralResolved[index])
                    return;
                Bone definition = skeleton.Bones[index];
                int parent = definition.ParentBoneIndex;
                Pose pose = xr ? ToUnity(definition.Pose) : new Pose(
                    definition.Pose.Position.FromFlippedXVector3f(), definition.Pose.Orientation.FromFlippedXQuatf());
                // Hand bind transforms are hierarchical. Express the rest axes in the same
                // common frame as the live joints before the mapping removes them.
                if (parent >= 0)
                {
                    ResolveNeutral(parent, xr);
                    pose = pose.GetTransformedBy(neutral[parent]);
                }
                neutral[index] = pose;
                neutralResolved[index] = true;
            }

            // Reconstruct legacy hand positions by chaining the scaled bind offsets and live rotations from the wrist.
            private void ResolveLegacy(int index, Pose root)
            {
                if (resolved[index])
                    return;

                Bone definition = skeleton.Bones[index];
                int id = (int)definition.Id;
                int parent = definition.ParentBoneIndex;
                if (parent < 0)
                {
                    poses[index] = root;
                }
                else
                {
                    ResolveLegacy(parent, root);
                    Quaternion rotation = id < (int)BoneId.Hand_MaxSkinnable && id < state.BoneRotations.Length
                        ? state.BoneRotations[id].FromFlippedXQuatf() : definition.Pose.Orientation.FromFlippedXQuatf();
                    var local = new Pose(definition.Pose.Position.FromFlippedXVector3f() * state.HandScale, rotation);
                    poses[index] = local.GetTransformedBy(poses[parent]);
                }
                resolved[index] = true;
            }
        }
    }
}
