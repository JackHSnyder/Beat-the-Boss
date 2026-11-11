using System.Collections.Generic;
using UnityEngine;
using BoneId = OVRPlugin.BoneId;

public class Player : MonoBehaviour {
    public static Player instance;

    [System.NonSerialized]
    public BodyPose playerBody;

    public OVRBody ovrBody;

    private static readonly Dictionary<BoneEnum, BoneId> boneMappings = new Dictionary<BoneEnum, BoneId> {
        { BoneEnum.Head, BoneId.FullBody_Head },
        { BoneEnum.SpineMiddle, BoneId.FullBody_SpineMiddle },
        { BoneEnum.SpineLower, BoneId.FullBody_SpineLower },
        { BoneEnum.LeftShoulder, BoneId.FullBody_LeftArmUpper },
        { BoneEnum.RightShoulder, BoneId.FullBody_RightArmUpper },
        { BoneEnum.LeftElbow, BoneId.FullBody_LeftArmLower },
        { BoneEnum.RightElbow, BoneId.FullBody_RightArmLower },
        { BoneEnum.LeftWrist, BoneId.FullBody_LeftHandWrist },
        { BoneEnum.RightWrist, BoneId.FullBody_RightHandWrist },
        { BoneEnum.LeftThumbMetacarpal, BoneId.FullBody_LeftHandThumbMetacarpal },
        { BoneEnum.RightThumbMetacarpal, BoneId.FullBody_RightHandThumbMetacarpal },
        { BoneEnum.LeftThumbProximal, BoneId.FullBody_LeftHandThumbProximal },
        { BoneEnum.RightThumbProximal, BoneId.FullBody_RightHandThumbProximal },
        { BoneEnum.LeftThumbDistal, BoneId.FullBody_LeftHandThumbDistal },
        { BoneEnum.RightThumbDistal, BoneId.FullBody_RightHandThumbDistal },
        { BoneEnum.LeftIndexMetacarpal, BoneId.FullBody_LeftHandIndexMetacarpal },
        { BoneEnum.RightIndexMetacarpal, BoneId.FullBody_RightHandIndexMetacarpal },
        { BoneEnum.LeftIndexProximal, BoneId.FullBody_LeftHandIndexProximal },
        { BoneEnum.RightIndexProximal, BoneId.FullBody_RightHandIndexProximal },
        { BoneEnum.LeftIndexIntermediate, BoneId.FullBody_LeftHandIndexIntermediate },
        { BoneEnum.RightIndexIntermediate, BoneId.FullBody_RightHandIndexIntermediate },
        { BoneEnum.LeftIndexDistal, BoneId.FullBody_LeftHandIndexDistal },
        { BoneEnum.RightIndexDistal, BoneId.FullBody_RightHandIndexDistal },
        { BoneEnum.LeftMiddleMetacarpal, BoneId.FullBody_LeftHandMiddleMetacarpal },
        { BoneEnum.RightMiddleMetacarpal, BoneId.FullBody_RightHandMiddleMetacarpal },
        { BoneEnum.LeftMiddleProximal, BoneId.FullBody_LeftHandMiddleProximal },
        { BoneEnum.RightMiddleProximal, BoneId.FullBody_RightHandMiddleProximal },
        { BoneEnum.LeftMiddleIntermediate, BoneId.FullBody_LeftHandMiddleIntermediate },
        { BoneEnum.RightMiddleIntermediate, BoneId.FullBody_RightHandMiddleIntermediate },
        { BoneEnum.LeftMiddleDistal, BoneId.FullBody_LeftHandMiddleDistal },
        { BoneEnum.RightMiddleDistal, BoneId.FullBody_RightHandMiddleDistal },
        { BoneEnum.LeftRingMetacarpal, BoneId.FullBody_LeftHandRingMetacarpal },
        { BoneEnum.RightRingMetacarpal, BoneId.FullBody_RightHandRingMetacarpal },
        { BoneEnum.LeftRingProximal, BoneId.FullBody_LeftHandRingProximal },
        { BoneEnum.RightRingProximal, BoneId.FullBody_RightHandRingProximal },
        { BoneEnum.LeftRingIntermediate, BoneId.FullBody_LeftHandRingIntermediate },
        { BoneEnum.RightRingIntermediate, BoneId.FullBody_RightHandRingIntermediate },
        { BoneEnum.LeftRingDistal, BoneId.FullBody_LeftHandRingDistal },
        { BoneEnum.RightRingDistal, BoneId.FullBody_RightHandRingDistal },
        { BoneEnum.LeftLittleMetacarpal, BoneId.FullBody_LeftHandLittleMetacarpal },
        { BoneEnum.RightLittleMetacarpal, BoneId.FullBody_RightHandLittleMetacarpal },
        { BoneEnum.LeftLittleProximal, BoneId.FullBody_LeftHandLittleProximal },
        { BoneEnum.RightLittleProximal, BoneId.FullBody_RightHandLittleProximal },
        { BoneEnum.LeftLittleIntermediate, BoneId.FullBody_LeftHandLittleIntermediate },
        { BoneEnum.RightLittleIntermediate, BoneId.FullBody_RightHandLittleIntermediate },
        { BoneEnum.LeftLittleDistal, BoneId.FullBody_LeftHandLittleDistal },
        { BoneEnum.RightLittleDistal, BoneId.FullBody_RightHandLittleDistal },
        { BoneEnum.LeftUpperLeg, BoneId.FullBody_LeftUpperLeg },
        { BoneEnum.RightUpperLeg, BoneId.FullBody_RightUpperLeg },
        { BoneEnum.LeftLowerLeg, BoneId.FullBody_LeftLowerLeg },
        { BoneEnum.RightLowerLeg, BoneId.FullBody_RightLowerLeg },
        { BoneEnum.LeftFootAnkle, BoneId.FullBody_LeftFootAnkle },
        { BoneEnum.RightFootAnkle, BoneId.FullBody_RightFootAnkle },
        { BoneEnum.LeftFootBall, BoneId.FullBody_LeftFootBall },
        { BoneEnum.RightFootBall, BoneId.FullBody_RightFootBall }
    };

    private Transform trackingSpace;
    private OVRPlugin.Skeleton2 skeleton;
    private OVRPlugin.SkeletonType skeletonType = OVRPlugin.SkeletonType.None;
    private uint skeletonChangedCount;

    void Awake() {
        if (instance == null) {
            instance = this;
            playerBody = new BodyPose();
            foreach (BoneEnum bone in boneMappings.Keys) {
                playerBody.getBones().Add(bone, new BodyBone { pose = Pose.identity });
            }
        } else {
            Destroy(gameObject);
        }
    }

    void Start() {
        OVRCameraRig cameraRig = GetComponentInChildren<OVRCameraRig>();
        trackingSpace = cameraRig != null && cameraRig.trackingSpace != null ? cameraRig.trackingSpace : transform;

        if (ovrBody == null) {
            Debug.LogError("OVRBody reference is not assigned in the Player script.");
        }
    }

    void LateUpdate() {
        OVRPlugin.BodyJointLocation[] joints = null;
        if (ovrBody != null && ovrBody.isActiveAndEnabled &&
            ovrBody.BodyState is OVRPlugin.BodyState state &&
            state.JointLocations != null && UpdateSkeleton(state)) {
            joints = state.JointLocations;
        }

        var bones = playerBody.getBones();
        foreach (var mapping in boneMappings) {
            BodyBone bone = bones[mapping.Key];
            bone.enabled = false;

            int jointIndex = (int)mapping.Value;
            if (joints == null || jointIndex >= joints.Length || jointIndex >= skeleton.Bones.Length) {
                continue;
            }

            OVRPlugin.BodyJointLocation joint = joints[jointIndex];
            if (!joint.PositionValid || !joint.OrientationValid) {
                continue;
            }

            Quaternion rotation = joint.Pose.Orientation.FromFlippedZQuatf();
            int parentIndex = skeleton.Bones[jointIndex].ParentBoneIndex;
            if (parentIndex >= 0) {
                if (parentIndex >= skeleton.Bones.Length) {
                    continue;
                }

                int parentJointIndex = (int)skeleton.Bones[parentIndex].Id;
                if (parentJointIndex < 0 || parentJointIndex >= joints.Length || !joints[parentJointIndex].OrientationValid) {
                    continue;
                }

                Quaternion parentRotation = joints[parentJointIndex].Pose.Orientation.FromFlippedZQuatf();
                rotation = Quaternion.Inverse(parentRotation) * rotation;
            }

            Vector3 position = joint.Pose.Position.FromFlippedZVector3f();
            bone.pose = new Pose(transform.InverseTransformPoint(trackingSpace.TransformPoint(position)), rotation);
            bone.enabled = true;
        }
    }

    private bool UpdateSkeleton(OVRPlugin.BodyState state) {
        OVRPlugin.SkeletonType requestedType = ovrBody.ProvidedSkeletonType == OVRPlugin.BodyJointSet.FullBody
            ? OVRPlugin.SkeletonType.FullBody
            : OVRPlugin.SkeletonType.Body;

        if (skeletonType == requestedType && skeletonChangedCount == state.SkeletonChangedCount) {
            return true;
        }

        if (!OVRPlugin.GetSkeleton2(requestedType, ref skeleton) || skeleton.Bones == null) {
            return false;
        }

        skeletonType = requestedType;
        skeletonChangedCount = state.SkeletonChangedCount;
        return true;
    }
}
