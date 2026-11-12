using System;
using System.Collections.Generic;
using Meta.XR.Movement.Retargeting;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    // The model is an output of tracking. Its bone axes are specific to this rig.
    internal sealed class AvatarPoseRig
    {
        private readonly CharacterRetargeter avatar;
        private readonly Dictionary<string, Transform> joints = new(StringComparer.Ordinal);
        private readonly AvatarSnapshot neutral;
        private readonly Arm left;
        private readonly Arm right;
        private readonly Transform skeleton;
        private readonly Transform head;
        private readonly Quaternion headRest;
        private readonly Quaternion skeletonRest;

        public string Id => avatar.ConfigAsset != null ? avatar.ConfigAsset.name : avatar.name;
        public Transform Root => avatar.transform;

        public AvatarPoseRig(CharacterRetargeter avatar)
        {
            this.avatar = avatar;
            foreach (var pair in avatar.JointPairs)
            {
                if (pair.Joint != null)
                    joints.Add(pair.Joint.name, pair.Joint);
            }
            neutral = Capture();
            joints.TryGetValue("Skeleton", out skeleton);
            joints.TryGetValue("Head", out head);
            if (head != null)
                headRest = Quaternion.Inverse(Root.rotation) * head.rotation;
            if (skeleton != null)
                skeletonRest = Quaternion.Inverse(Root.rotation) * skeleton.rotation;
            left = new Arm("Left", joints, Root);
            right = new Arm("Right", joints, Root);
        }

        public AvatarSnapshot Capture()
        {
            var snapshot = new AvatarSnapshot { rig = Id, rootScale = Root.localScale, joints = new AvatarJoint[joints.Count] };
            int index = 0;
            foreach (var entry in joints)
            {
                Transform joint = entry.Value;
                snapshot.joints[index++] = new AvatarJoint
                {
                    name = entry.Key, localPosition = joint.localPosition,
                    localRotation = joint.localRotation, localScale = joint.localScale
                };
            }
            return snapshot;
        }

        public void ReadModelTest(TrackedPose frame)
        {
            frame.Clear(TrackingMode.ModelTest);
            foreach (var entry in joints)
            {
                string name = entry.Key;
                Transform joint = entry.Value;
                string parent = joint.parent != null && joints.ContainsKey(joint.parent.name)
                    ? "Model/" + joint.parent.name : null;
                PoseBranches branch = left.Contains(joint) ? PoseBranches.LeftHand :
                    right.Contains(joint) ? PoseBranches.RightHand : PoseBranches.None;
                frame.Add(new TrackedBone("Model/" + name, parent, branch,
                    new Pose(Root.InverseTransformPoint(joint.position), Quaternion.Inverse(Root.rotation) * joint.rotation),
                    joint.localRotation));
            }
        }

        public void Reset() => Apply(neutral, Root, joints);

        public void SetTestBend(float degrees)
        {
            Reset();
            if (joints.TryGetValue("Left_LowerArm", out Transform elbow))
                elbow.localRotation *= Quaternion.AngleAxis(degrees, Vector3.forward);
        }

        public void ApplyDevices(TrackedPose frame, Transform space)
        {
            Reset();
            if (head != null && frame.TryGet("Devices/Head", out TrackedBone trackedHead))
            {
                Quaternion orientation = trackedHead.TrackingPose.rotation;
                Vector3 target = space.TransformPoint(trackedHead.TrackingPose.position);
                if (skeleton != null)
                {
                    skeleton.rotation = space.rotation * PlayerPoseTracker.Heading(orientation) * skeletonRest;
                    skeleton.position += target - head.position;
                }
                head.rotation = space.rotation * orientation * headRest;
            }
            left.Apply(frame, space);
            right.Apply(frame, space);
        }

        public static void Apply(AvatarSnapshot snapshot, Transform root, IReadOnlyDictionary<string, Transform> bones)
        {
            root.localScale = snapshot.rootScale;
            foreach (AvatarJoint joint in snapshot.joints)
            {
                if (bones.TryGetValue(joint.name, out Transform bone))
                {
                    bone.SetLocalPositionAndRotation(joint.localPosition, joint.localRotation);
                    bone.localScale = joint.localScale;
                }
            }
        }

        public Dictionary<string, Transform> MapClone(Transform clone)
        {
            var bones = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (Transform bone in clone.GetComponentsInChildren<Transform>(true))
            {
                if (joints.ContainsKey(bone.name))
                    bones.Add(bone.name, bone);
            }
            return bones;
        }

        private sealed class Arm
        {
            private static readonly string[] Fingers = { "Thumb", "Index", "Middle", "Ring", "Little" };
            private static readonly string[] Segments = { "Proximal", "Intermediate", "Distal", "Tip" };
            private readonly string side;
            private readonly Transform upper;
            private readonly Transform lower;
            private readonly Transform hand;
            private readonly Quaternion controllerOffset;
            private readonly Quaternion palmOffset;
            private readonly Transform[,] fingers = new Transform[5, 4];
            private readonly Vector3[,] segmentAxes = new Vector3[5, 3];
            private readonly string[,] jointIds = new string[5, 4];
            private readonly string[,] legacyJointIds = new string[5, 4];

            public Arm(string side, Dictionary<string, Transform> joints, Transform root)
            {
                this.side = side;
                joints.TryGetValue(side + "_UpperArm", out upper);
                joints.TryGetValue(side + "_LowerArm", out lower);
                joints.TryGetValue(side + "_Hand", out hand);
                if (hand == null)
                    return;

                controllerOffset = Quaternion.Inverse(root.rotation) * hand.rotation;
                for (int finger = 0; finger < Fingers.Length; finger++)
                {
                    string modelName = finger == 4 ? "Pinky" : Fingers[finger];
                    for (int segment = 0; segment < Segments.Length; segment++)
                    {
                        joints.TryGetValue(side + "_" + modelName + Segments[segment], out fingers[finger, segment]);
                        string suffix = finger == 0 && segment == 0 ? "Metacarpal" :
                            finger == 0 && segment == 1 ? "Proximal" : Segments[segment];
                        jointIds[finger, segment] = side + "Hand/" + Fingers[finger] + suffix;
                        legacyJointIds[finger, segment] = side + "LegacyHand/" + modelName +
                            (segment == 3 ? "Tip" : (segment + 1).ToString());
                    }
                    for (int segment = 0; segment < 3; segment++)
                    {
                        Transform bone = fingers[finger, segment];
                        Transform next = fingers[finger, segment + 1];
                        if (bone != null)
                        {
                            Vector3 direction = next != null ? next.position - bone.position : bone.position - bone.parent.position;
                            segmentAxes[finger, segment] = bone.InverseTransformDirection(direction);
                        }
                    }
                }

                if (fingers[1, 0] != null && fingers[2, 0] != null && fingers[4, 0] != null)
                {
                    Quaternion palm = Palm(hand.position, fingers[1, 0].position, fingers[2, 0].position, fingers[4, 0].position);
                    palmOffset = Quaternion.Inverse(palm) * hand.rotation;
                }
            }

            public bool Contains(Transform joint) => hand != null && (joint == hand || joint.IsChildOf(hand));

            public void Apply(TrackedPose frame, Transform space)
            {
                if (upper == null || lower == null || hand == null)
                    return;

                bool xrHand = frame.TryGet(side + "Hand/Wrist", out TrackedBone wrist);
                bool legacyHand = !xrHand && frame.TryGet(side + "LegacyHand/WristRoot", out wrist);
                if (!xrHand && !legacyHand && !frame.TryGet("Devices/" + side + "Controller", out wrist))
                    return;

                Vector3 target = space.TransformPoint(wrist.TrackingPose.position);
                Reach(target, space.up, space.forward);

                string[,] fingerIds = xrHand ? jointIds : legacyJointIds;
                if ((xrHand || legacyHand) && frame.TryGet(fingerIds[1, 0], out TrackedBone index) &&
                    frame.TryGet(fingerIds[2, 0], out TrackedBone middle) &&
                    frame.TryGet(fingerIds[4, 0], out TrackedBone pinky))
                {
                    Quaternion palm = Palm(wrist.TrackingPose.position, index.TrackingPose.position,
                        middle.TrackingPose.position, pinky.TrackingPose.position);
                    hand.rotation = space.rotation * palm * palmOffset;
                    for (int finger = 0; finger < Fingers.Length; finger++)
                    {
                        for (int segment = 0; segment < 3; segment++)
                        {
                            Transform bone = fingers[finger, segment];
                            if (bone != null && frame.TryGet(fingerIds[finger, segment], out TrackedBone from) &&
                                frame.TryGet(fingerIds[finger, segment + 1], out TrackedBone to))
                            {
                                Aim(bone, bone.TransformDirection(segmentAxes[finger, segment]),
                                    space.TransformDirection(to.TrackingPose.position - from.TrackingPose.position));
                            }
                        }
                    }
                }
                else
                {
                    hand.rotation = space.rotation * wrist.TrackingPose.rotation * controllerOffset;
                }
            }

            private void Reach(Vector3 target, Vector3 up, Vector3 forward)
            {
                Vector3 fromShoulder = target - upper.position;
                if (fromShoulder.sqrMagnitude < 0.00001f)
                    return;

                float upperLength = Vector3.Distance(upper.position, lower.position);
                float lowerLength = Vector3.Distance(lower.position, hand.position);
                if (Mathf.Min(upperLength, lowerLength) < 0.001f)
                    return;

                Vector3 direction = fromShoulder.normalized;
                float reach = Mathf.Clamp(fromShoulder.magnitude,
                    Mathf.Abs(upperLength - lowerLength) + 0.001f, upperLength + lowerLength - 0.001f);
                float along = (upperLength * upperLength - lowerLength * lowerLength + reach * reach) / (2 * reach);
                float bend = Mathf.Sqrt(Mathf.Max(0, upperLength * upperLength - along * along));
                Vector3 pole = Vector3.ProjectOnPlane(-up - forward * 0.3f, direction).normalized;
                Vector3 elbow = upper.position + direction * along + pole * bend;
                Aim(upper, lower.position - upper.position, elbow - upper.position);
                Aim(lower, hand.position - lower.position, upper.position + direction * reach - lower.position);
            }

            private static void Aim(Transform bone, Vector3 from, Vector3 to)
            {
                if (from.sqrMagnitude > 0.00000001f && to.sqrMagnitude > 0.00000001f)
                    bone.rotation = Quaternion.FromToRotation(from, to) * bone.rotation;
            }

            private static Quaternion Palm(Vector3 wrist, Vector3 index, Vector3 middle, Vector3 pinky)
            {
                Vector3 forward = middle - wrist;
                Vector3 normal = Vector3.Cross(index - pinky, forward);
                return normal.sqrMagnitude > 0.00000001f ? Quaternion.LookRotation(forward, normal) : Quaternion.identity;
            }
        }
    }
}
