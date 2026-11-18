// Maps universal bone motion onto the avatar rig while preserving its bind transforms.
// PlayerAvatarDriver uses it for live animation. PoseWorkbench uses it for model tests and saved pose previews.

using System;
using System.Collections.Generic;
using Meta.XR.Movement.Retargeting;
using Oculus.Interaction.Input;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    // Maps the universal skeleton onto a rig's own bind transforms.
    internal sealed class AvatarPoseRig
    {
        internal const string RightIndexProximalJoint = "Right_IndexProximal";

        private readonly Dictionary<string, Transform> joints = new(StringComparer.Ordinal);
        private readonly Dictionary<string, RestJoint> rest = new(StringComparer.Ordinal);
        private readonly List<RigBinding> bindings = new();
        private readonly Dictionary<string, Quaternion> universalRotations = new(StringComparer.Ordinal);
        private readonly Dictionary<string, PoseJoint> previewLocals = new(StringComparer.Ordinal);
        private readonly Dictionary<Transform, Pose> uncorrectedHandPose = new();
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

        internal bool TryGetJoint(string name, out Transform joint) => joints.TryGetValue(name, out joint);

        internal void AlignTrackedHand(string side, IHand tracking, Vector3? pokePoint, Transform player, bool alignIndex)
        {
            if (!joints.TryGetValue(side + "_Hand", out Transform hand) ||
                !tracking.GetRootPose(out Pose wrist))
                return;

            if (joints.TryGetValue(side + "_UpperArm", out Transform upperArm))
                RememberHandJoint(upperArm);
            if (joints.TryGetValue(side + "_LowerArm", out Transform lowerArm))
                RememberHandJoint(lowerArm);
            RememberHandJoint(hand);

            // Keep the tracked elbow bend where possible and use the actual wrist even past the avatar's reach.
            AimArm(side, wrist.position, player, joints, preserveElbow: true);
            hand.position = wrist.position;

            if (!joints.TryGetValue(side + "_IndexProximal", out Transform proximal) ||
                !tracking.GetJointPose(HandJointId.HandIndex1, out Pose knuckle))
                return;

            if (joints.TryGetValue(side + "_PinkyProximal", out Transform little) &&
                tracking.GetJointPose(HandJointId.HandPinky1, out Pose trackedLittle) &&
                TryGetPalmRotation(hand.position, proximal.position, little.position, out Quaternion modelPalm) &&
                TryGetPalmRotation(wrist.position, knuckle.position, trackedLittle.position, out Quaternion trackedPalm))
                hand.rotation = trackedPalm * Quaternion.Inverse(modelPalm) * hand.rotation;

            if (!alignIndex ||
                !joints.TryGetValue(side + "_IndexIntermediate", out Transform intermediate) ||
                !joints.TryGetValue(side + "_IndexDistal", out Transform distal) ||
                !tracking.GetJointPose(HandJointId.HandIndex2, out Pose middle) ||
                !tracking.GetJointPose(HandJointId.HandIndex3, out Pose end))
                return;

            Transform tip = distal.Find(side + "_IndexDistalEnd");
            if (tip == null)
                return;
            if (!pokePoint.HasValue)
            {
                if (!tracking.GetJointPose(HandJointId.HandIndexTip, out Pose trackedTip))
                    return;
                pokePoint = trackedTip.position;
            }

            RememberHandJoint(proximal);
            RememberHandJoint(intermediate);
            RememberHandJoint(distal);
            AimFingerSegment(proximal, intermediate, knuckle.position, middle.position);
            AimFingerSegment(intermediate, distal, middle.position, end.position);
            AimFingerSegment(distal, tip, end.position, pokePoint.Value);
            // Move the skinned distal bone so its visible tip follows the same point as the poke interactor.
            distal.position += pokePoint.Value - tip.position;
        }

        private void RememberHandJoint(Transform joint) =>
            uncorrectedHandPose.TryAdd(joint, new Pose(joint.localPosition, joint.localRotation));

        internal void RestoreHandPose()
        {
            // Meta only writes rotations on some joints. Remove last frame's positional correction before retargeting.
            foreach (var pair in uncorrectedHandPose)
                if (pair.Key != null)
                    pair.Key.SetLocalPositionAndRotation(pair.Value.position, pair.Value.rotation);
            uncorrectedHandPose.Clear();
        }

        private static bool TryGetPalmRotation(Vector3 wrist, Vector3 index, Vector3 little, out Quaternion rotation)
        {
            Vector3 forward = (index + little) * 0.5f - wrist;
            Vector3 normal = Vector3.Cross(index - little, forward);
            rotation = Quaternion.identity;
            if (forward.sqrMagnitude < 1e-8f || normal.sqrMagnitude < 1e-12f)
                return false;
            rotation = Quaternion.LookRotation(forward, normal);
            return true;
        }

        private static void AimFingerSegment(Transform joint, Transform child, Vector3 start, Vector3 end)
        {
            joint.position = start;
            Vector3 current = child.position - start;
            Vector3 target = end - start;
            if (current.sqrMagnitude > 1e-8f && target.sqrMagnitude > 1e-8f)
                joint.rotation = Quaternion.FromToRotation(current, target) * joint.rotation;
        }

        internal bool TryGetRightIndexRay(out Ray ray)
        {
            ray = default;
            if (!joints.TryGetValue(RightIndexProximalJoint, out Transform proximal) ||
                !rest.TryGetValue("Right_IndexIntermediate", out RestJoint intermediateRest) ||
                !joints.TryGetValue("Right_IndexDistal", out Transform distal))
                return false;

            Transform fingertip = distal.Find("Right_IndexDistalEnd");
            if (fingertip == null)
                return false;

            // The bind offset points from the main knuckle along the first finger segment.
            Vector3 direction = proximal.rotation * intermediateRest.localPose.position;
            if (direction.sqrMagnitude < 0.00000001f)
                return false;

            ray = new Ray(fingertip.position, direction.normalized);
            return true;
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

        private static void AimArm(string side, Vector3 target, Transform reference,
            IReadOnlyDictionary<string, Transform> bones, bool preserveElbow = false)
        {
            if (!bones.TryGetValue(side + "_UpperArm", out Transform upper) ||
                !bones.TryGetValue(side + "_LowerArm", out Transform lower) ||
                !bones.TryGetValue(side + "_Hand", out Transform hand))
                return;

            Quaternion orientation = hand.rotation;
            Vector3 offset = target - upper.position;
            float a = Vector3.Distance(upper.position, lower.position);
            float b = Vector3.Distance(lower.position, hand.position);
            if (offset.sqrMagnitude < 0.00001f || Mathf.Min(a, b) < 0.001f)
                return;
            Vector3 direction = offset.normalized;
            float reach = Mathf.Clamp(offset.magnitude, Mathf.Abs(a - b) + 0.001f, a + b - 0.001f);
            float along = (a * a - b * b + reach * reach) / (2 * reach);
            Vector3 bend = preserveElbow ? Vector3.ProjectOnPlane(lower.position - upper.position, direction) : Vector3.zero;
            if (bend.sqrMagnitude < 1e-6f)
                bend = Vector3.ProjectOnPlane(-reference.up - reference.forward * 0.3f, direction);
            Vector3 elbow = upper.position + direction * along +
                bend.normalized * Mathf.Sqrt(Mathf.Max(0, a * a - along * along));
            upper.rotation = Quaternion.FromToRotation(lower.position - upper.position, elbow - upper.position) * upper.rotation;
            lower.rotation = Quaternion.FromToRotation(hand.position - lower.position,
                upper.position + direction * reach - lower.position) * lower.rotation;
            hand.rotation = orientation;
        }

        public void ApplyPreview(IReadOnlyList<PoseJoint> pose, Transform clone,
            IReadOnlyDictionary<string, Transform> cloneBones, MoveAnimation animation = null)
        {
            ResetRig(clone, cloneBones);
            clone.localScale = previewScale;
            if (hasRetargetedHeadScale && cloneBones.TryGetValue("Head", out Transform scaledHead))
                scaledHead.localScale = previewHeadScale;
            var locals = previewLocals;
            locals.Clear();
            for (int i = 0; i < pose.Count; i++)
                locals[pose[i].id] = pose[i];

            // Rebuild player-space orientations from the same parent table used by capture.
            universalRotations.Clear();
            foreach (SkeletonBone bone in UniversalSkeleton.Bones)
            {
                Quaternion parent = Quaternion.identity;
                if (bone.ParentId != null)
                    parent = universalRotations[bone.ParentId];
                Quaternion local = animation?.excludedBones?.Contains(bone.Id) != true &&
                    locals.TryGetValue(bone.Id, out PoseJoint joint) ? joint.localRotation : Quaternion.identity;
                universalRotations[bone.Id] = parent * local;
            }
            if (!locals.ContainsKey("LeftThumbMetacarpal") && locals.ContainsKey("LeftThumbProximal"))
                InferThumbMetacarpal("LeftWrist", "LeftThumbProximal", "LeftThumbMetacarpal");
            if (!locals.ContainsKey("RightThumbMetacarpal") && locals.ContainsKey("RightThumbProximal"))
                InferThumbMetacarpal("RightWrist", "RightThumbProximal", "RightThumbMetacarpal");
            ApplyRotations(clone.rotation, cloneBones, includeHelpers: true, animation);
            // Anchors are saved in player space. Changing the model's scale changes its
            // proportions in the preview, while the captured anchors keep their distances.
            Transform placement = clone.parent != null ? clone.parent : clone;
            if (IsEnabled("Hips", animation) && locals.TryGetValue("Hips", out PoseJoint hips) &&
                hips.hasPosition && cloneBones.TryGetValue("Hips", out Transform hip))
                hip.position = placement.TransformPoint(hips.position);
            else if (animation?.excludedBones?.Count is not > 0 && IsEnabled("Head", animation) &&
                locals.TryGetValue("Head", out PoseJoint head) && head.hasPosition &&
                cloneBones.TryGetValue("Head", out Transform modelHead) && cloneBones.TryGetValue("Skeleton", out Transform skeleton))
                skeleton.position += placement.TransformPoint(head.position) - modelHead.position;

            PreviewArm("Left", placement, cloneBones, animation);
            PreviewArm("Right", placement, cloneBones, animation);
        }

        private void PreviewArm(string side, Transform placement, IReadOnlyDictionary<string, Transform> bones,
            MoveAnimation animation)
        {
            string forearm = side + "Forearm", wristId = side + "Wrist";
            if (IsEnabled(side + "UpperArm", animation) && IsEnabled(forearm, animation) && IsEnabled(wristId, animation) &&
                !previewLocals.ContainsKey(forearm) &&
                previewLocals.TryGetValue(wristId, out PoseJoint wrist) && wrist.hasPosition)
                AimArm(side, placement.TransformPoint(wrist.position), placement, bones);
        }

        private static bool IsEnabled(string id, MoveAnimation animation) => animation?.IncludesBone(id) != false;

        // Keep the old visual approximation for captures or devices without a thumb-base sample.
        private void InferThumbMetacarpal(string wristId, string proximalId, string metacarpalId)
        {
            if (universalRotations.TryGetValue(wristId, out Quaternion wrist) &&
                universalRotations.TryGetValue(proximalId, out Quaternion proximal))
                universalRotations[metacarpalId] = Quaternion.Slerp(wrist, proximal, 0.45f);
        }

        private void ApplyRotations(Quaternion rootRotation, IReadOnlyDictionary<string, Transform> target,
            bool includeHelpers, MoveAnimation animation = null)
        {
            foreach (var pair in bindings)
            {
                if (!IsEnabled(pair.from, animation) || !target.TryGetValue(pair.name, out Transform joint) ||
                    !universalRotations.TryGetValue(pair.from, out Quaternion rotation))
                    continue;
                if (pair.to != null)
                {
                    if (!includeHelpers || !IsEnabled(pair.to, animation) ||
                        !universalRotations.TryGetValue(pair.to, out Quaternion end))
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
