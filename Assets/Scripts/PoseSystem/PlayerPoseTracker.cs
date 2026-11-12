using System;
using UnityEngine;
using static OVRPlugin;

namespace BeatTheBoss.Poses
{
    [DefaultExecutionOrder(100)]
    public sealed class PlayerPoseTracker : MonoBehaviour
    {
        public enum InputMode { Automatic, HandsAndHead, ControllersAndHead }

        [SerializeField] private OVRCameraRig cameraRig;
        [SerializeField] private OVRBody body;
        [SerializeField] private InputMode input;

        private static readonly string[] BodyIds = MakeIds("FullBody_", "Body/", (int)BoneId.FullBody_End);
        private static readonly PoseBranches[] BodyBranches = MakeBodyBranches();
        private readonly HandReader left = new(true);
        private readonly HandReader right = new(false);
        private Skeleton2 bodySkeleton;
        private SkeletonType bodySkeletonType = SkeletonType.None;
        private uint bodySkeletonRevision;

        public TrackedPose Frame { get; } = new();
        public InputMode Input { get => input; set => input = value; }
        public Transform TrackingSpace => cameraRig != null && cameraRig.trackingSpace != null
            ? cameraRig.trackingSpace : transform;

        private void LateUpdate()
        {
            Frame.Clear(TrackingMode.None);
            if (input == InputMode.Automatic && ReadBody())
                return;

            bodySkeletonType = SkeletonType.None;
            ReadDevices();
            if (Frame.Bones.Count == 0)
                Frame.Clear(TrackingMode.None);
        }

        private void OnDisable() => Frame.Clear(TrackingMode.None);

        private bool ReadBody()
        {
            if (body == null || !body.isActiveAndEnabled || body.BodyState is not BodyState state ||
                state.JointLocations == null)
                return false;

            var joints = state.JointLocations;
            if (joints.Length < (int)BoneId.Body_End ||
                (!IsValid(joints[(int)BoneId.FullBody_Head]) && !IsValid(joints[(int)BoneId.FullBody_Hips])))
                return false;

            SkeletonType type = joints.Length >= BodyIds.Length ? SkeletonType.FullBody : SkeletonType.Body;
            if (bodySkeletonType != type || bodySkeletonRevision != state.SkeletonChangedCount)
            {
                if (!GetSkeleton2(type, ref bodySkeleton) || bodySkeleton.Bones == null)
                    return false;

                bodySkeletonType = type;
                bodySkeletonRevision = state.SkeletonChangedCount;
            }

            bool hasLegData = Array.FindIndex(joints, (int)BoneId.Body_End, IsValid) >= 0;
            Frame.Clear(hasLegData ? TrackingMode.FullBody : TrackingMode.UpperBody);
            foreach (Bone definition in bodySkeleton.Bones)
            {
                int index = (int)definition.Id;
                if ((uint)index >= BodyIds.Length || index >= joints.Length || !IsValid(joints[index]))
                    continue;

                Pose pose = ToUnity(joints[index].Pose);
                int parent = definition.ParentBoneIndex;
                int parentId = (uint)parent < bodySkeleton.Bones.Length ? (int)bodySkeleton.Bones[parent].Id : -1;
                bool localValid = parent < 0 || ((uint)parentId < joints.Length &&
                    parentId < BodyIds.Length && joints[parentId].OrientationValid);
                Quaternion local = Quaternion.identity;
                if (localValid)
                {
                    Quaternion reference = parent < 0
                        ? Heading(pose.rotation)
                        : joints[parentId].Pose.Orientation.FromFlippedZQuatf();
                    local = Quaternion.Inverse(reference) * pose.rotation;
                }

                Frame.Add(new TrackedBone(BodyIds[index], (uint)parentId < BodyIds.Length ? BodyIds[parentId] : null,
                    BodyBranches[index], pose, local, localValid));
            }
            return Frame.Bones.Count > 0;
        }

        private void ReadDevices()
        {
            Frame.Clear(TrackingMode.Devices);
            Quaternion heading = Quaternion.identity;
            if (GetNodePositionTracked(Node.EyeCenter) && GetNodeOrientationTracked(Node.EyeCenter))
            {
                Pose head = ToUnity(GetNodePose(Node.EyeCenter, Step.Render));
                heading = Heading(head.rotation);
                AddDevice("Devices/Head", PoseBranches.None, head, heading);
            }

            bool hasLeft = input != InputMode.ControllersAndHead && left.Read(Frame, heading);
            bool hasRight = input != InputMode.ControllersAndHead && right.Read(Frame, heading);
            if (input == InputMode.HandsAndHead)
                return;

            if (!hasLeft)
                ReadController(true, heading);
            if (!hasRight)
                ReadController(false, heading);
        }

        private void ReadController(bool isLeft, Quaternion heading)
        {
            OVRInput.Controller controller = isLeft ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
            if (!OVRInput.IsControllerConnected(controller) ||
                !OVRInput.GetControllerPositionTracked(controller) ||
                !OVRInput.GetControllerOrientationTracked(controller))
                return;

            var pose = new Pose(OVRInput.GetLocalControllerPosition(controller),
                OVRInput.GetLocalControllerRotation(controller));
            AddDevice(isLeft ? "Devices/LeftController" : "Devices/RightController",
                isLeft ? PoseBranches.LeftHand : PoseBranches.RightHand, pose, heading);
        }

        private void AddDevice(string id, PoseBranches branch, Pose pose, Quaternion heading) =>
            Frame.Add(new TrackedBone(id, null, branch, pose, Quaternion.Inverse(heading) * pose.rotation));

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

        private static PoseBranches[] MakeBodyBranches()
        {
            var branches = new PoseBranches[BodyIds.Length];
            for (int index = (int)BoneId.FullBody_LeftHandPalm; index <= (int)BoneId.FullBody_LeftHandLittleTip; index++)
                branches[index] = PoseBranches.LeftHand;
            for (int index = (int)BoneId.FullBody_RightHandPalm; index <= (int)BoneId.FullBody_RightHandLittleTip; index++)
                branches[index] = PoseBranches.RightHand;
            return branches;
        }

        private sealed class HandReader
        {
            private readonly bool isLeft;
            private readonly PoseBranches branch;
            private readonly string[] xrIds;
            private readonly string[] legacyIds;
            private readonly Pose[] poses = new Pose[(int)BoneId.XRHand_End];
            private readonly bool[] resolved = new bool[(int)BoneId.XRHand_End];
            private HandState state;
            private Skeleton2 skeleton;
            private SkeletonType skeletonType = SkeletonType.None;

            public HandReader(bool isLeft)
            {
                this.isLeft = isLeft;
                branch = isLeft ? PoseBranches.LeftHand : PoseBranches.RightHand;
                string side = isLeft ? "Left" : "Right";
                xrIds = MakeIds("XRHand_", side + "Hand/", (int)BoneId.XRHand_End);
                legacyIds = MakeIds("Hand_", side + "LegacyHand/", (int)BoneId.Hand_End);
            }

            public bool Read(TrackedPose frame, Quaternion heading)
            {
                if (!GetHandState(Step.Render, isLeft ? Hand.HandLeft : Hand.HandRight, ref state) ||
                    (state.Status & HandStatus.HandTracked) == 0 || state.BoneRotations == null)
                    return false;

                bool xr = HandSkeletonVersion == OVRHandSkeletonVersion.OpenXR &&
                    state.BonePositions?.Length == xrIds.Length;
                SkeletonType type = xr
                    ? (isLeft ? SkeletonType.XRHandLeft : SkeletonType.XRHandRight)
                    : (isLeft ? SkeletonType.HandLeft : SkeletonType.HandRight);
                if (skeletonType != type)
                {
                    if (!GetSkeleton2(type, ref skeleton) || skeleton.Bones == null)
                        return false;
                    skeletonType = type;
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

                bool found = false;
                for (int index = 0; index < skeleton.Bones.Length; index++)
                {
                    if (!resolved[index])
                        continue;

                    Bone definition = skeleton.Bones[index];
                    int id = (int)definition.Id;
                    int parent = definition.ParentBoneIndex;
                    Quaternion reference = parent < 0 ? heading : poses[parent].rotation;
                    bool hasRotation = xr || id < (int)BoneId.Hand_MaxSkinnable;
                    frame.Add(new TrackedBone(ids[id], parent < 0 ? null : ids[(int)skeleton.Bones[parent].Id],
                        branch, poses[index], Quaternion.Inverse(reference) * poses[index].rotation, hasRotation));
                    found = true;
                }
                return found;
            }

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
