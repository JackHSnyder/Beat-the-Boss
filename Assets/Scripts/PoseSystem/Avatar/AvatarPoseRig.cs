// Maps universal bone motion onto the avatar rig while preserving its bind transforms.
// PlayerAvatarDriver uses it for live animation. PoseWorkbench uses it for model tests and saved pose previews.

using System;
using System.Collections.Generic;
using Meta.XR.Movement.Retargeting;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    // Maps the universal skeleton onto a rig's own bind transforms.
    internal sealed class AvatarPoseRig
    {
        private readonly Dictionary<string, Transform> joints = new(StringComparer.Ordinal);
        private readonly Dictionary<string, RestJoint> rest = new(StringComparer.Ordinal);
        private readonly List<RigBinding> bindings = new();
        private readonly Dictionary<string, Quaternion> universalRotations = new(StringComparer.Ordinal);
        private readonly TrackingPointBuffer modelPoints = new();
        private readonly BoneMappings modelMapping = new("", load: false);
        private readonly Vector3 restScale;
        private Vector3 previewScale;
        private Vector3 previewHeadScale;
        private bool hasRetargetedHeadScale;

        private struct RigBinding
        {
            public string name;
            public string from;
            public string to;
            public float blend;
        }

        private struct RestJoint
        {
            public Pose localPose;
            public Vector3 scale;
            public Quaternion rootRotation;
        }

        public Transform Root { get; }

        public AvatarPoseRig(CharacterRetargeter avatar)
        {
            Root = avatar.transform;
            restScale = Root.localScale;
            previewScale = restScale;
            foreach (var pair in avatar.JointPairs)
            {
                Transform joint = pair.Joint;
                if (joint == null)
                    continue;
                joints.Add(joint.name, joint);
                rest.Add(joint.name, new RestJoint
                {
                    localPose = new Pose(joint.localPosition, joint.localRotation),
                    scale = joint.localScale,
                    rootRotation = Quaternion.Inverse(Root.rotation) * joint.rotation
                });
            }
            foreach (var pair in BoneMappings.Avatar)
                if (joints.ContainsKey(pair.Key))
                    bindings.Add(new RigBinding { name = pair.Key, from = pair.Value });

            // Distribute the saved torso bend across the model's extra chest joint.
            AddHelper("Chest", "Spine", "Chest", 0.55f);
            // Set parent transforms before descendants, even where rig and universal hierarchies differ.
            bindings.Sort((a, b) => Depth(joints[a.name]).CompareTo(Depth(joints[b.name])));
        }

        private void AddHelper(string name, string from, string to, float blend)
        {
            if (!joints.ContainsKey(name))
                return;
            bindings.RemoveAll(binding => binding.name == name);
            bindings.Add(new RigBinding { name = name, from = from, to = to, blend = blend });
        }

        public void RememberRetargetedScale()
        {
            Vector3 scale = Root.localScale;
            if (IsUsableScale(scale))
                previewScale = scale;
            if (joints.TryGetValue("Head", out Transform head) && IsUsableScale(head.localScale))
            {
                previewHeadScale = head.localScale;
                hasRetargetedHeadScale = true;
            }
        }

        private static bool IsUsableScale(Vector3 scale) => scale.x > 0 && scale.y > 0 && scale.z > 0 &&
            float.IsFinite(scale.x) && float.IsFinite(scale.y) && float.IsFinite(scale.z);

        public void Reset() => ResetRig(Root, joints);

        public void SetTestBend(float degrees)
        {
            Reset();
            if (joints.TryGetValue("Left_LowerArm", out Transform elbow))
                elbow.localRotation *= Quaternion.AngleAxis(degrees, Vector3.forward);
        }

        public void ReadModelTest(TrackedPose frame, Transform player)
        {
            modelPoints.Clear();
            foreach (var pair in BoneMappings.Avatar)
            {
                if (joints.TryGetValue(pair.Key, out Transform joint))
                    modelPoints.Add(pair.Value, new Pose(player.InverseTransformPoint(joint.position),
                        Quaternion.Inverse(player.rotation) * joint.rotation), rest[pair.Key].rootRotation);
            }
            modelMapping.BuildFrame(modelPoints, TrackingMode.ModelTest, frame);
        }

        public void ApplyLive(TrackedPose frame, Transform player)
        {
            Reset();
            bool hasHips = frame.TryGet("Hips", out TrackedBone hips);
            bool hasHead = frame.TryGet("Head", out TrackedBone head);
            joints.TryGetValue("Skeleton", out Transform skeleton);
            if (skeleton != null && (!hasHips || !hips.HasTrackingRotation) && hasHead)
                AlignYaw(skeleton, skeleton, head, player);

            if (hasHips && joints.TryGetValue("Hips", out Transform hipJoint))
                hipJoint.position = player.TransformPoint(hips.PlayerPose.position);
            else if (hasHead && skeleton != null && joints.TryGetValue("Head", out Transform headJoint))
                skeleton.position += player.TransformPoint(head.PlayerPose.position) - headJoint.position;

            universalRotations.Clear();
            foreach (TrackedBone bone in frame.Bones.Values)
                if (bone.HasTrackingRotation)
                    universalRotations[bone.Id] = bone.PlayerPose.rotation;
            if (!universalRotations.ContainsKey("LeftThumbMetacarpal"))
                InferThumbMetacarpal("LeftWrist", "LeftThumbProximal", "LeftThumbMetacarpal");
            if (!universalRotations.ContainsKey("RightThumbMetacarpal"))
                InferThumbMetacarpal("RightWrist", "RightThumbProximal", "RightThumbMetacarpal");
            ApplyRotations(player.rotation, joints, includeHelpers: true);
            AimUntrackedArm("Left", frame, player);
            AimUntrackedArm("Right", frame, player);
        }

        public void PlaceRetargetedBody(TrackedPose frame, Transform player)
        {
            if (!joints.TryGetValue("Skeleton", out Transform skeleton))
                return;

            joints.TryGetValue("Head", out Transform headJoint);
            joints.TryGetValue("Hips", out Transform hipJoint);
            bool hasHead = frame.TryGet("Head", out TrackedBone head) && headJoint != null;
            bool hasHips = frame.TryGet("Hips", out TrackedBone hips) && hipJoint != null;

            // Match the tracked head's world yaw, then place the body at its tracked anchor.
            bool aligned = hasHead && AlignYaw(skeleton, headJoint, head, player);
            if (!aligned && hasHips)
                AlignYaw(skeleton, hipJoint, hips, player);

            if (hasHips)
                skeleton.position += player.TransformPoint(hips.PlayerPose.position) - hipJoint.position;
            else if (hasHead)
                skeleton.position += player.TransformPoint(head.PlayerPose.position) - headJoint.position;
        }

        private bool AlignYaw(Transform skeleton, Transform joint, TrackedBone tracked, Transform player)
        {
            if (!tracked.HasTrackingRotation)
                return false;

            // Remove the joint's bind-pose axes to compare the avatar and tracking forward vectors.
            Quaternion avatarFacing = joint.rotation * Quaternion.Inverse(rest[joint.name].rootRotation);
            Quaternion trackedFacing = player.rotation * tracked.PlayerPose.rotation;
            Vector3 avatarForward = Vector3.ProjectOnPlane(avatarFacing * Vector3.forward, Vector3.up);
            Vector3 trackedForward = Vector3.ProjectOnPlane(trackedFacing * Vector3.forward, Vector3.up);
            if (avatarForward.sqrMagnitude < 0.0001f || trackedForward.sqrMagnitude < 0.0001f)
                return false;

            float yaw = Vector3.SignedAngle(avatarForward, trackedForward, Vector3.up);
            skeleton.rotation = Quaternion.AngleAxis(yaw, Vector3.up) * skeleton.rotation;
            return true;
        }

        public void ApplyCustomMappings(TrackedPose frame, Transform player, BoneMappings mappings)
        {
            // Keep the SDK's detailed pose, then apply explicitly assigned tracking points.
            universalRotations.Clear();
            foreach (TrackedBone bone in frame.Bones.Values)
                if (bone.HasTrackingRotation && mappings.HasOverride(bone.SourcePoint))
                    universalRotations[bone.Id] = bone.PlayerPose.rotation;

            if (universalRotations.Count == 0)
                return;
            ApplyRotations(player.rotation, joints, includeHelpers: false);
            if (frame.TryGet("Hips", out TrackedBone hips) && mappings.HasOverride(hips.SourcePoint) &&
                joints.TryGetValue("Hips", out Transform hipJoint))
                hipJoint.position = player.TransformPoint(hips.PlayerPose.position);
            AimCustomWrist("Left", frame, player, mappings);
            AimCustomWrist("Right", frame, player, mappings);
        }

        private void AimCustomWrist(string side, TrackedPose frame, Transform player, BoneMappings mappings)
        {
            if (frame.TryGet(side + "Wrist", out TrackedBone wrist) && mappings.HasOverride(wrist.SourcePoint))
                AimArm(side, player.TransformPoint(wrist.PlayerPose.position), player, joints);
        }

        private void AimUntrackedArm(string side, TrackedPose frame, Transform player)
        {
            if (!frame.TryGet(side + "Forearm", out _) && frame.TryGet(side + "Wrist", out TrackedBone wrist))
                AimArm(side, player.TransformPoint(wrist.PlayerPose.position), player, joints);
        }

        private static void AimArm(string side, Vector3 target, Transform reference, IReadOnlyDictionary<string, Transform> bones)
        {
            if (!bones.TryGetValue(side + "_UpperArm", out Transform upper) ||
                !bones.TryGetValue(side + "_LowerArm", out Transform lower) ||
                !bones.TryGetValue(side + "_Hand", out Transform hand))
                return;

            // A controller can place a wrist even though it cannot measure an elbow.
            Quaternion orientation = hand.rotation;
            Vector3 offset = target - upper.position;
            float a = Vector3.Distance(upper.position, lower.position);
            float b = Vector3.Distance(lower.position, hand.position);
            if (offset.sqrMagnitude < 0.00001f || Mathf.Min(a, b) < 0.001f)
                return;
            Vector3 direction = offset.normalized;
            float reach = Mathf.Clamp(offset.magnitude, Mathf.Abs(a - b) + 0.001f, a + b - 0.001f);
            float along = (a * a - b * b + reach * reach) / (2 * reach);
            Vector3 pole = Vector3.ProjectOnPlane(-reference.up - reference.forward * 0.3f, direction).normalized;
            Vector3 elbow = upper.position + direction * along + pole * Mathf.Sqrt(Mathf.Max(0, a * a - along * along));
            upper.rotation = Quaternion.FromToRotation(lower.position - upper.position, elbow - upper.position) * upper.rotation;
            lower.rotation = Quaternion.FromToRotation(hand.position - lower.position,
                upper.position + direction * reach - lower.position) * lower.rotation;
            hand.rotation = orientation;
        }

        public void ApplyPreview(SavedPose pose, Transform clone, IReadOnlyDictionary<string, Transform> cloneBones)
        {
            ResetRig(clone, cloneBones);
            clone.localScale = previewScale;
            if (hasRetargetedHeadScale && cloneBones.TryGetValue("Head", out Transform scaledHead))
                scaledHead.localScale = previewHeadScale;
            var locals = new Dictionary<string, PoseJoint>(StringComparer.Ordinal);
            foreach (PoseJoint joint in pose.joints)
                locals[joint.id] = joint;

            // Rebuild player-space orientations from the same parent table used by capture.
            universalRotations.Clear();
            foreach (SkeletonBone bone in UniversalSkeleton.Bones)
            {
                Quaternion parent = Quaternion.identity;
                if (bone.ParentId != null)
                    parent = universalRotations[bone.ParentId];
                Quaternion local = locals.TryGetValue(bone.Id, out PoseJoint joint) ? joint.localRotation : Quaternion.identity;
                universalRotations[bone.Id] = parent * local;
            }
            if (!locals.ContainsKey("LeftThumbMetacarpal") && locals.ContainsKey("LeftThumbProximal"))
                InferThumbMetacarpal("LeftWrist", "LeftThumbProximal", "LeftThumbMetacarpal");
            if (!locals.ContainsKey("RightThumbMetacarpal") && locals.ContainsKey("RightThumbProximal"))
                InferThumbMetacarpal("RightWrist", "RightThumbProximal", "RightThumbMetacarpal");
            ApplyRotations(clone.rotation, cloneBones, includeHelpers: true);
            // Anchors are saved in player space. Changing the model's scale changes its
            // proportions in the preview, while the captured anchors keep their distances.
            Transform placement = clone.parent != null ? clone.parent : clone;
            if (locals.TryGetValue("Hips", out PoseJoint hips) && hips.hasPosition && cloneBones.TryGetValue("Hips", out Transform hip))
                hip.position = placement.TransformPoint(hips.position);
            else if (locals.TryGetValue("Head", out PoseJoint head) && head.hasPosition &&
                cloneBones.TryGetValue("Head", out Transform modelHead) && cloneBones.TryGetValue("Skeleton", out Transform skeleton))
                skeleton.position += placement.TransformPoint(head.position) - modelHead.position;

            foreach (string side in new[] { "Left", "Right" })
                if (!locals.ContainsKey(side + "Forearm") && locals.TryGetValue(side + "Wrist", out PoseJoint wrist) && wrist.hasPosition)
                    AimArm(side, placement.TransformPoint(wrist.position), placement, cloneBones);
        }

        // Keep the old visual approximation for captures or devices without a thumb-base sample.
        private void InferThumbMetacarpal(string wristId, string proximalId, string metacarpalId)
        {
            if (universalRotations.TryGetValue(wristId, out Quaternion wrist) &&
                universalRotations.TryGetValue(proximalId, out Quaternion proximal))
                universalRotations[metacarpalId] = Quaternion.Slerp(wrist, proximal, 0.45f);
        }

        private void ApplyRotations(Quaternion rootRotation, IReadOnlyDictionary<string, Transform> target, bool includeHelpers)
        {
            foreach (var pair in bindings)
            {
                if (!target.TryGetValue(pair.name, out Transform joint) ||
                    !universalRotations.TryGetValue(pair.from, out Quaternion rotation))
                    continue;
                if (pair.to != null)
                {
                    if (!includeHelpers || !universalRotations.TryGetValue(pair.to, out Quaternion end))
                        continue;
                    rotation = Quaternion.Slerp(rotation, end, pair.blend);
                }
                joint.rotation = rootRotation * rotation * rest[pair.name].rootRotation;
            }
        }

        private static int Depth(Transform joint)
        {
            int depth = 0;
            for (Transform parent = joint.parent; parent != null; parent = parent.parent)
                depth++;
            return depth;
        }

        private void ResetRig(Transform root, IReadOnlyDictionary<string, Transform> target)
        {
            root.localScale = restScale;
            foreach (var pair in rest)
            {
                if (target.TryGetValue(pair.Key, out Transform joint))
                {
                    joint.SetLocalPositionAndRotation(pair.Value.localPose.position, pair.Value.localPose.rotation);
                    joint.localScale = pair.Value.scale;
                }
            }
        }

        public Dictionary<string, Transform> MapClone(Transform clone)
        {
            var bones = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (Transform bone in clone.GetComponentsInChildren<Transform>(true))
                if (joints.ContainsKey(bone.name))
                    bones.Add(bone.name, bone);
            return bones;
        }
    }
}
