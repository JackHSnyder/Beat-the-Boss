using System;
using System.Collections.Generic;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    // A rig is calibrated in its neutral pose; no humanoid Avatar or tracking SDK is required.
    public sealed class BossRig : MonoBehaviour
    {
        [Serializable] public sealed class Binding
        {
            public string boneId;
            public Transform joint;
            [NonSerialized] public Quaternion bindRotation;
            [NonSerialized] public Quaternion localRotation;
            [NonSerialized] public Vector3 localPosition;
            [NonSerialized] public Quaternion parentBindRotation;
            [NonSerialized] public Binding mappedParent;
        }
        public Transform rigRoot;
        public List<Binding> bones = new();
        private readonly Dictionary<string, Quaternion> rotations = new();
        private readonly Dictionary<string, PoseJoint> samples = new();
        private readonly Dictionary<Transform, Pose> previous = new();
        private bool initialized;

        internal static void DisableLiveDrivers(GameObject visual)
        {
            foreach (MonoBehaviour behaviour in visual.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
            foreach (Animator animator in visual.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
            foreach (Collider collider in visual.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            // Disabled behaviours still receive Awake when their object first activates.
            // Tony's standalone face retargeter expects a live tracking provider, which
            // boss/preview instances deliberately do not have. Keep that nonvisual object inactive.
            foreach (var face in visual.GetComponentsInChildren<Meta.XR.Movement.FaceTracking.Samples.FaceRetargeterComponent>(true))
                if (face.transform != visual.transform && face.GetComponentInChildren<Renderer>(true) == null)
                    face.gameObject.SetActive(false);
        }

        internal void Rebind(Transform root)
        {
            rigRoot=root; initialized=false; previous.Clear();
            MapStylized(); Initialize();
        }

        [ContextMenu("Map stylized character bones")]
        public void MapStylized()
        {
            if (rigRoot == null) rigRoot = transform;
            bones.Clear();
            var mapping=rigRoot.GetComponentInChildren<CharacterBoneMap>(true);
            foreach (Transform joint in rigRoot.GetComponentsInChildren<Transform>(true))
                if (BoneMappings.Avatar.TryGetValue(mapping!=null?mapping.NameOf(joint):joint.name, out string id))
                    bones.Add(new Binding { boneId = id, joint = joint });
        }

        public void Initialize()
        {
            if (initialized) return;
            if (rigRoot == null) rigRoot = transform;
            if (bones.Count == 0) MapStylized();
            bones.RemoveAll(b => b == null || b.joint == null || !UniversalSkeleton.TryGet(b.boneId, out _));
            bones.Sort((a, b) => Depth(a.joint).CompareTo(Depth(b.joint)));
            CalibrateArmRestPose();
            foreach (Binding b in bones)
            {
                b.bindRotation = Quaternion.Inverse(transform.rotation) * b.joint.rotation;
                b.localRotation = b.joint.localRotation;
                b.localPosition = b.joint.localPosition;
                b.parentBindRotation = b.joint.parent != null ? Quaternion.Inverse(transform.rotation) * b.joint.parent.rotation : Quaternion.identity;
                for (Transform parent = b.joint.parent; parent != null && parent != transform; parent = parent.parent)
                {
                    b.mappedParent = bones.Find(candidate => candidate.joint == parent);
                    if (b.mappedParent != null) break;
                }
            }
            initialized = true;
        }
        private void CalibrateArmRestPose()
        {
            if (rigRoot.GetComponentInChildren<CharacterBoneMap>(true) == null) return;
            var catalog=Resources.Load<GameAssetCatalog>("Game Asset Catalog");
            var reference=catalog!=null?Array.Find(catalog.characters,c=>c.id==DownloadedAssetLibrary.DefaultCharacter)?.prefab:null;
            if (reference==null) return;
            var referenceBones=new Dictionary<string,Transform>();
            foreach (Transform joint in reference.GetComponentsInChildren<Transform>(true))
                if (BoneMappings.Avatar.TryGetValue(joint.name,out string id)) referenceBones[id]=joint;

            // Universal recordings contain changes from a common T-pose. Native Meta
            // models start in an A-pose: keeping that arm bend as the playback baseline
            // bends their arms a second time and can carry each wrist across the torso.
            // Pose only this instance into the reference arm directions before binding.
            foreach (string side in new[] {"Left","Right"})
            {
                Align(side+"UpperArm",side+"Forearm");
                Align(side+"Forearm",side+"Wrist");
                Transform hand=Joint(side+"Wrist"),index=Joint(side+"IndexProximal"),little=Joint(side+"PinkyProximal");
                if (hand==null || index==null || little==null ||
                    !referenceBones.TryGetValue(side+"Wrist",out var refHand) ||
                    !referenceBones.TryGetValue(side+"IndexProximal",out var refIndex) ||
                    !referenceBones.TryGetValue(side+"PinkyProximal",out var refLittle)) continue;
                if (PalmFrame(hand.position,index.position,little.position,out var current) &&
                    PalmFrame(refHand.position,refIndex.position,refLittle.position,out var target))
                {
                    Quaternion desired=rigRoot.rotation*Quaternion.Inverse(reference.transform.rotation)*target;
                    hand.rotation=desired*Quaternion.Inverse(current)*hand.rotation;
                }
            }

            void Align(string parentId,string childId)
            {
                Transform parent=Joint(parentId),child=Joint(childId);
                if (parent==null || child==null || !referenceBones.TryGetValue(parentId,out var refParent) ||
                    !referenceBones.TryGetValue(childId,out var refChild)) return;
                Vector3 direction=rigRoot.TransformDirection(reference.transform.InverseTransformDirection(refChild.position-refParent.position));
                Vector3 current=child.position-parent.position;
                if (current.sqrMagnitude>1e-8f && direction.sqrMagnitude>1e-8f)
                    parent.rotation=Quaternion.FromToRotation(current,direction)*parent.rotation;
            }
        }
        private static bool PalmFrame(Vector3 wrist,Vector3 index,Vector3 little,out Quaternion rotation)
        {
            Vector3 forward=(index+little)*.5f-wrist;
            Vector3 normal=Vector3.Cross(index-little,forward);
            rotation=Quaternion.identity;
            if (forward.sqrMagnitude<1e-8f || normal.sqrMagnitude<1e-12f) return false;
            rotation=Quaternion.LookRotation(forward,normal); return true;
        }
        private static int Depth(Transform t) { int n = 0; while (t.parent != null) { n++; t = t.parent; } return n; }
        public Transform Joint(string id) => bones.Find(b => b.boneId == id)?.joint;
        public void BeginPose()
        {
            previous.Clear();
            foreach (Binding b in bones)
            {
                previous[b.joint] = new Pose(b.joint.localPosition, b.joint.localRotation);
                b.joint.SetLocalPositionAndRotation(b.localPosition, b.localRotation);
            }
        }
        internal void CapturePose(List<Pose> pose)
        {
            pose.Clear();
            foreach (Binding b in bones) pose.Add(new Pose(b.joint.localPosition,b.joint.localRotation));
        }
        internal void BlendFrom(IReadOnlyList<Pose> pose,float weight)
        {
            for (int i=0;i<bones.Count && i<pose.Count;i++)
            {
                Transform joint=bones[i].joint;
                joint.SetLocalPositionAndRotation(Vector3.Lerp(pose[i].position,joint.localPosition,weight),
                    Quaternion.Slerp(pose[i].rotation,joint.localRotation,weight));
            }
        }
        public void Apply(IReadOnlyList<PoseJoint> pose, MoveAnimation animation, float weight)
        {
            samples.Clear(); rotations.Clear();
            foreach (PoseJoint p in pose) samples[p.id] = p;
            foreach (SkeletonBone b in UniversalSkeleton.Bones)
            {
                Quaternion parent = b.ParentId == null ? Quaternion.identity : rotations[b.ParentId];
                rotations[b.Id] = parent * (samples.TryGetValue(b.Id, out PoseJoint p) ? p.localRotation : Quaternion.identity);
            }
            // Blend in rig-local space: masked parents remain on the idle layer, including finger-only moves.
            foreach (Binding b in bones)
            {
                if (!samples.ContainsKey(b.boneId) || !Enabled(animation, b.boneId)) continue;
                Quaternion target = transform.rotation * rotations[b.boneId] * b.bindRotation;
                UniversalSkeleton.TryGet(b.boneId, out SkeletonBone universal);
                if (universal.ParentId != null && b.mappedParent != null)
                {
                    // Relative joints (especially fingers) inherit the live idle parent, not its recorded orientation.
                    Quaternion recordedParent = transform.rotation * rotations[b.mappedParent.boneId] * b.parentBindRotation;
                    b.joint.localRotation = Quaternion.Slerp(b.joint.localRotation, Quaternion.Inverse(recordedParent) * target, weight);
                }
                else b.joint.rotation = Quaternion.Slerp(b.joint.rotation, target, weight);
            }
            // Preserve recorded hip translation relative to the take's first frame, keeping the boss at its arena origin.
            if (Enabled(animation, "Hips") && samples.TryGetValue("Hips", out PoseJoint hips) && hips.hasPosition)
            {
                foreach (PoseJoint first in animation.frames[0].joints)
                    if (first.id == "Hips" && first.hasPosition)
                    {
                        Transform hip = Joint("Hips");
                        if (hip != null) hip.position += transform.TransformVector(hips.position - first.position) * weight;
                        break;
                    }
            }
            // Wrist positions drive untracked arms only when the move enables the arm as well as the hand.
            foreach (string side in new[] { "Left", "Right" })
            {
                if (!Enabled(animation, side + "UpperArm") || !Enabled(animation, side + "Forearm") || !Enabled(animation, side + "Wrist") ||
                    samples.ContainsKey(side + "Forearm") || !samples.TryGetValue(side + "Wrist", out PoseJoint wrist) || !wrist.hasPosition) continue;
                Transform hand = Joint(side + "Wrist");
                if (hand != null) SolveLimb(Joint(side + "UpperArm"), Joint(side + "Forearm"), hand,
                    Vector3.Lerp(hand.position, transform.TransformPoint(wrist.position), weight), -transform.up - transform.forward * .3f);
            }
        }
        internal static bool Enabled(MoveAnimation animation, string id) => animation?.IncludesBone(id) == true;
        public void Smooth(float seconds, float dt)
        {
            float t = seconds <= 0 ? 1 : 1 - Mathf.Exp(-dt / seconds);
            foreach (Binding b in bones)
            {
                Pose old = previous[b.joint];
                b.joint.SetLocalPositionAndRotation(Vector3.Lerp(old.position, b.joint.localPosition, t),
                    Quaternion.Slerp(old.rotation, b.joint.localRotation, t));
            }
        }
        internal static void SolveLimb(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 bendHint)
        {
            if (upper == null || lower == null || end == null) return;
            float a = Vector3.Distance(upper.position, lower.position), b = Vector3.Distance(lower.position, end.position);
            Vector3 delta = target - upper.position;
            if (a < .001f || b < .001f || delta.sqrMagnitude < .000001f) return;
            Quaternion rotation = end.rotation;
            Vector3 direction = delta.normalized;
            float distance = Mathf.Clamp(delta.magnitude, Mathf.Abs(a - b) + .0001f, a + b - .0001f);
            float along = (a * a - b * b + distance * distance) / (2 * distance);
            Vector3 bend = Vector3.ProjectOnPlane(bendHint, direction).normalized;
            if (bend.sqrMagnitude < .01f) bend = Vector3.Cross(direction, Vector3.right).normalized;
            Vector3 knee = upper.position + direction * along + bend * Mathf.Sqrt(Mathf.Max(0, a * a - along * along));
            upper.rotation = Quaternion.FromToRotation(lower.position - upper.position, knee - upper.position) * upper.rotation;
            lower.rotation = Quaternion.FromToRotation(end.position - lower.position, upper.position + direction * distance - lower.position) * lower.rotation;
            end.rotation = rotation;
        }
    }
}
