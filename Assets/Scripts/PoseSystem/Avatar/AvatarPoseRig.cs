// Maps universal bone motion onto the avatar rig while preserving its bind transforms.
// PlayerAvatarDriver uses it for live animation. PoseWorkbench uses it for model tests and saved pose previews.

using System;
using System.Collections.Generic;
using System.Linq;
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
        private readonly Dictionary<Transform, (Pose pose,Vector3 scale)> uncorrectedHandPose = new();
        private readonly TrackingPointBuffer modelPoints = new();
        private readonly BoneMappings modelMapping = new("", load: false);
        private readonly Vector3 restScale;
        private Vector3 previewScale;
        private Vector3 previewHeadScale;
        private bool hasRetargetedHeadScale;
        private readonly Dictionary<Transform,string> jointNames=new();
        private readonly Dictionary<string,string> jointPaths=new();
        private readonly CharacterBoneMap boneMap;
        private readonly List<FingerBinding> fingers=new();
        private readonly Dictionary<Transform,Pose> uncorrectedBodyPose=new();
        private readonly List<ForearmTwist> forearmTwists=new();
        private Transform[] bodyChain=Array.Empty<Transform>();
        private (Pose pose,Vector3 scale)[] safeBodyPose=Array.Empty<(Pose,Vector3)>();
        private (Pose pose,Vector3 scale)[] displayedBodyPose=Array.Empty<(Pose,Vector3)>();
        private Vector3 safeBodyScale,safeHeadOffset,bodyEyePosition;
        private Vector3 displayedBodyScale,displayedHeadOffset;
        private bool stabilizeBody,holdBody;
        private float bodyRecovery=1;
        private sealed class ForearmTwist
        {
            internal string side;
            internal Transform joint,forearm,hand;
            internal Quaternion bind,forearmBind,handBind;
            internal float fraction;
        }
        private sealed class FingerBinding
        {
            internal string side,name,firstId,secondId,thirdId;
            internal Transform hand,proximal,intermediate,distal,tip,metacarpal;
            internal HandJointId start,tipId;
            internal Quaternion wristBind;
        }

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
            public Vector3 rootPosition;
        }

        public Transform Root { get; }
        internal bool UsesTrackedFingerPositions => boneMap!=null;

        public AvatarPoseRig(CharacterRetargeter avatar)
        {
            Root = avatar.transform;
            boneMap=avatar.GetComponent<CharacterBoneMap>();
            restScale = Root.localScale;
            previewScale = restScale;
            var source=avatar.JointPairs.Select(p=>p.Joint);
            if (boneMap!=null) source=source.Concat(boneMap.bindings.Select(b=>b.joint));
            foreach (Transform joint in source.Where(j=>j!=null).Distinct())
            {
                string name=boneMap!=null?boneMap.NameOf(joint):joint.name;
                joints.Add(name, joint); jointNames[joint]=name;
                jointPaths[name]=RelativePath(joint,Root);
                rest.Add(name, new RestJoint
                {
                    localPose = new Pose(joint.localPosition, joint.localRotation),
                    scale = joint.localScale,
                    rootPosition=Root.InverseTransformPoint(joint.position),
                    rootRotation = Quaternion.Inverse(Root.rotation) * joint.rotation
                });
            }
            foreach (var pair in BoneMappings.Avatar)
                if (joints.ContainsKey(pair.Key))
                    bindings.Add(new RigBinding { name = pair.Key, from = pair.Value });
            if(joints.TryGetValue("Head",out var bodyHead) && joints.TryGetValue("Skeleton",out var bodyRoot))
            {
                var chain=new List<Transform>();
                for(Transform joint=bodyHead.parent;joint!=null && joint!=Root;joint=joint.parent)
                { chain.Add(joint);if(joint==bodyRoot)break; }
                chain.Reverse();bodyChain=chain.ToArray();safeBodyPose=new (Pose,Vector3)[bodyChain.Length];
                displayedBodyPose=new (Pose,Vector3)[bodyChain.Length];
                CacheStableBody(bodyRoot,bodyHead.position);
            }

            // Distribute the saved torso bend across the model's extra chest joint.
            AddHelper("Chest", "Spine", "Chest", 0.55f);
            // Set parent transforms before descendants, even where rig and universal hierarchies differ.
            bindings.Sort((a, b) => Depth(joints[a.name]).CompareTo(Depth(joints[b.name])));
            if(boneMap!=null)foreach(string side in new[]{"Left","Right"})foreach(var finger in TrackedFingers)
            {
                string prefix=side+"_"+finger.name;
                if(!joints.TryGetValue(side+"_Hand",out var hand) || !joints.TryGetValue(prefix+"Proximal",out var proximal) ||
                    !joints.TryGetValue(prefix+"Intermediate",out var intermediate) || !joints.TryGetValue(prefix+"Distal",out var distal) ||
                    !joints.TryGetValue(prefix+"DistalEnd",out var tip))continue;
                joints.TryGetValue(side+"_"+finger.name+"Base",out var metacarpal);
                fingers.Add(new FingerBinding {side=side,name=finger.name,hand=hand,proximal=proximal,intermediate=intermediate,distal=distal,tip=tip,
                    metacarpal=metacarpal,
                    firstId=BoneMappings.Avatar[prefix+"Proximal"],secondId=BoneMappings.Avatar[prefix+"Intermediate"],thirdId=BoneMappings.Avatar[prefix+"Distal"],
                    start=finger.start,tipId=finger.tip,wristBind=rest[side+"_Hand"].rootRotation});
            }
            if(boneMap!=null)foreach(string side in new[]{"Left","Right"})
            {
                if(!joints.TryGetValue(side+"_LowerArm",out var forearm) || !joints.TryGetValue(side+"_Hand",out var hand))continue;
                Vector3 arm=rest[side+"_Hand"].rootPosition-rest[side+"_LowerArm"].rootPosition;
                if(arm.sqrMagnitude<1e-8f)continue;
                foreach(var pair in joints.OrderBy(p=>Depth(p.Value)))
                    if(pair.Value.name.StartsWith(side+"ArmLowerTwist",StringComparison.Ordinal) || pair.Value.name==side+"WristTwist")
                        forearmTwists.Add(new ForearmTwist {side=side,joint=pair.Value,forearm=forearm,hand=hand,
                            bind=rest[pair.Key].rootRotation,forearmBind=rest[side+"_LowerArm"].rootRotation,handBind=rest[side+"_Hand"].rootRotation,
                            fraction=Mathf.Clamp01(Vector3.Dot(rest[pair.Key].rootPosition-rest[side+"_LowerArm"].rootPosition,arm)/arm.sqrMagnitude)});
            }
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
        internal Vector3 AimDirection(string name,Vector3 localDirection) => joints[name].TransformDirection(
            (boneMap!=null?boneMap.AimCorrection(name):Quaternion.identity)*localDirection);
        private Transform FingerTip(string side,Transform distal) => joints.TryGetValue(side+"_IndexDistalEnd",out var tip)
            ? tip : distal.Find(side+"_IndexDistalEnd");

        internal void AlignTrackedHand(string side, IHand tracking, Vector3? pokePoint, Transform player, bool alignIndex,
            Func<string,bool> canAlignBone=null)
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
                tracking.GetJointPose(HandJointId.HandPinky1, out Pose trackedLittle))
                AlignTrackedPalm(side,wrist.position,knuckle.position,trackedLittle.position);

            if(boneMap!=null)
            {
                ApplyForearmTwist(side);
                AlignMappedFingers(side,tracking,pokePoint,canAlignBone);
                return;
            }

            if (!alignIndex ||
                !joints.TryGetValue(side + "_IndexIntermediate", out Transform intermediate) ||
                !joints.TryGetValue(side + "_IndexDistal", out Transform distal) ||
                !tracking.GetJointPose(HandJointId.HandIndex2, out Pose middle) ||
                !tracking.GetJointPose(HandJointId.HandIndex3, out Pose end))
                return;

            Transform tip = FingerTip(side,distal);
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
        internal void AlignTrackedPalm(string side,Vector3 wrist,Vector3 index,Vector3 little)
        {
            if(!joints.TryGetValue(side+"_Hand",out var hand) || !TryGetPalmRotation(wrist,index,little,out var tracked))return;
            if(boneMap!=null)
            {
                var bind=rest[side+"_Hand"];
                if(TryGetPalmRotation(bind.rootPosition,rest[side+"_IndexProximal"].rootPosition,
                    rest[side+"_PinkyProximal"].rootPosition,out var neutral))
                    hand.rotation=tracked*Quaternion.Inverse(neutral)*bind.rootRotation;
            }
            else if(joints.TryGetValue(side+"_IndexProximal",out var proximal) && joints.TryGetValue(side+"_PinkyProximal",out var pinky) &&
                TryGetPalmRotation(hand.position,proximal.position,pinky.position,out var model))
                hand.rotation=tracked*Quaternion.Inverse(model)*hand.rotation;
        }

        private static readonly (string name,HandJointId start,HandJointId tip)[] TrackedFingers=
        {
            ("Thumb",HandJointId.HandThumb1,HandJointId.HandThumbTip),
            ("Index",HandJointId.HandIndex1,HandJointId.HandIndexTip),
            ("Middle",HandJointId.HandMiddle1,HandJointId.HandMiddleTip),
            ("Ring",HandJointId.HandRing1,HandJointId.HandRingTip),
            ("Pinky",HandJointId.HandPinky1,HandJointId.HandPinkyTip)
        };
        private void AlignMappedFingers(string side,IHand tracking,Vector3? pokePoint,Func<string,bool> canAlignBone)
        {
            foreach(var finger in fingers)
            {
                if(finger.side!=side || (canAlignBone!=null &&
                    (!canAlignBone(finger.firstId) || !canAlignBone(finger.secondId) || !canAlignBone(finger.thirdId))))continue;
                if(!tracking.GetJointPose(finger.start,out Pose a) ||
                    !tracking.GetJointPose((HandJointId)((int)finger.start+1),out Pose b) ||
                    !tracking.GetJointPose((HandJointId)((int)finger.start+2),out Pose c) ||
                    !tracking.GetJointPose(finger.tipId,out Pose d))continue;
                if(finger.metacarpal!=null)
                {
                    ResetFingerJoint(finger.metacarpal,finger.hand.rotation*Quaternion.Inverse(finger.wristBind));
                    // OpenXR has no thumb trapezium sample; keep that helper in the palm's neutral frame.
                    if(finger.name=="Pinky" && tracking.GetJointPose(HandJointId.HandPinky0,out Pose basePose))
                        FitFingerSegment(finger.metacarpal,finger.proximal,basePose.position,a.position);
                }
                AlignFingerPoints(finger,a.position,b.position,c.position,
                    finger.name=="Index" && pokePoint.HasValue?pokePoint.Value:d.position);
            }
        }
        // Segment directions come from tracked positions, independent of the character's finger bind axes.
        internal void AlignFingerPoints(string side,string finger,Vector3 a,Vector3 b,Vector3 c,Vector3 tipPoint)
        {
            foreach(var binding in fingers)if(binding.side==side && binding.name==finger)
            {
                AlignFingerPoints(binding,a,b,c,tipPoint);return;
            }
        }
        private void AlignFingerPoints(FingerBinding finger,Vector3 a,Vector3 b,Vector3 c,Vector3 tipPoint)
        {
            if((b-a).sqrMagnitude<1e-10f || (c-b).sqrMagnitude<1e-10f || (tipPoint-c).sqrMagnitude<1e-10f)return;
            Quaternion rotation=finger.hand.rotation*Quaternion.Inverse(finger.wristBind);
            ResetFingerJoint(finger.proximal,rotation);ResetFingerJoint(finger.intermediate,rotation);ResetFingerJoint(finger.distal,rotation);
            FitFingerSegment(finger.proximal,finger.intermediate,a,b);
            FitFingerSegment(finger.intermediate,finger.distal,b,c);
            FitFingerSegment(finger.distal,finger.tip,c,tipPoint,true);
        }
        private void ResetFingerJoint(Transform joint,Quaternion wrist)
        {
            RememberHandJoint(joint);RestJoint bind=rest[jointNames[joint]];
            joint.localPosition=bind.localPose.position;joint.localScale=bind.scale;joint.rotation=wrist*bind.rootRotation;
        }
        private static void FitFingerSegment(Transform joint,Transform child,Vector3 start,Vector3 end,bool fingertip=false)
        {
            AimFingerSegment(joint,child,start,end);
            if(!fingertip)
            {
                // Fit the knuckles by position. Scaling their parents would also widen every child phalanx.
                child.position=end;return;
            }
            float length=Vector3.Distance(joint.position,child.position);
            if(length>.0001f)
            {
                Vector3 offset=child.localPosition,scale=joint.localScale;
                int axis=Mathf.Abs(offset.x)>Mathf.Abs(offset.y)?0:1;
                if(Mathf.Abs(offset.z)>Mathf.Abs(offset[axis]))axis=2;
                // Only length can vary slightly; finger thickness and inherited parent scale stay unchanged.
                scale[axis]*=Mathf.Clamp(Vector3.Distance(start,end)/length,.75f,1.25f);
                joint.localScale=scale;
                AimFingerSegment(joint,child,start,end);
                joint.position+=end-child.position;
            }
        }
        internal void ApplyForearmTwist(string side)
        {
            ForearmTwist first=null;
            foreach(var twist in forearmTwists)if(twist.side==side) { first=twist;break; }
            if(first==null)return;
            var wrist=new Pose(first.hand.position,first.hand.rotation);
            Vector3 axis=(wrist.position-first.forearm.position).normalized;
            if(axis.sqrMagnitude<.9f)return;
            Quaternion reference=first.forearm.rotation*Quaternion.Inverse(first.forearmBind);
            Quaternion delta=wrist.rotation*Quaternion.Inverse(reference*first.handBind);
            if(delta.w<0)delta=new Quaternion(-delta.x,-delta.y,-delta.z,-delta.w);
            float projection=Vector3.Dot(new Vector3(delta.x,delta.y,delta.z),axis);
            float angle=projection*projection+delta.w*delta.w<1e-10f?0:2*Mathf.Atan2(projection,delta.w)*Mathf.Rad2Deg;
            RememberHandJoint(first.hand);
            foreach(var twist in forearmTwists)if(twist.side==side)
            {
                RememberHandJoint(twist.joint);
                twist.joint.rotation=Quaternion.AngleAxis(angle*twist.fraction,axis)*reference*twist.bind;
            }
            // Some rigs put a twist helper above the wrist; the tracked palm must remain fixed in world space.
            first.hand.SetPositionAndRotation(wrist.position,wrist.rotation);
        }
        internal void ApplyBodyPosture(Vector3 forward,Vector3 up,float backoff,float leanDegrees)
        {
            if((backoff<=0 && leanDegrees<=0) || !joints.TryGetValue("Spine",out var spine))return;
            forward=Vector3.ProjectOnPlane(forward,up).normalized;
            if(forward.sqrMagnitude<.9f)return;
            RememberBodyJoint(spine);
            joints.TryGetValue("Left_Hand",out var left);joints.TryGetValue("Right_Hand",out var right);
            Pose leftPose=left!=null?new Pose(left.position,left.rotation):default;
            Pose rightPose=right!=null?new Pose(right.position,right.rotation):default;
            foreach(string side in HandSides)
                foreach(string segment in ArmSegments)
                    if(joints.TryGetValue(side+segment,out var joint))RememberBodyJoint(joint);
            spine.position-=forward*backoff;
            spine.rotation=Quaternion.AngleAxis(-leanDegrees,Vector3.Cross(up,forward))*spine.rotation;
            if(left!=null) { AimArm("Left",leftPose.position,Root,joints,true);left.SetPositionAndRotation(leftPose.position,leftPose.rotation); }
            if(right!=null) { AimArm("Right",rightPose.position,Root,joints,true);right.SetPositionAndRotation(rightPose.position,rightPose.rotation); }
        }
        private static readonly string[] HandSides={"Left","Right"};
        private static readonly string[] ArmSegments={"_UpperArm","_LowerArm","_Hand"};
        private void RememberBodyJoint(Transform joint) => uncorrectedBodyPose.TryAdd(joint,new Pose(joint.localPosition,joint.localRotation));
        internal void RestoreBodyPosture()
        {
            foreach(var pair in uncorrectedBodyPose)if(pair.Key!=null)pair.Key.SetLocalPositionAndRotation(pair.Value.position,pair.Value.rotation);
            uncorrectedBodyPose.Clear();
        }

        private void RememberHandJoint(Transform joint) =>
            uncorrectedHandPose.TryAdd(joint, (new Pose(joint.localPosition, joint.localRotation),joint.localScale));

        internal void RestoreHandPose()
        {
            // Meta only writes rotations on some joints. Remove last frame's positional correction before retargeting.
            foreach (var pair in uncorrectedHandPose)
                if (pair.Key != null)
                {
                    pair.Key.SetLocalPositionAndRotation(pair.Value.pose.position, pair.Value.pose.rotation);
                    pair.Key.localScale=pair.Value.scale;
                }
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

            Transform fingertip = FingerTip("Right",distal);
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

        public void Reset() { RestoreBodyPosture();ResetRig(Root, joints); }

        public void SetTestBend(float degrees)
        {
            Reset();
            if (joints.TryGetValue("Left_LowerArm", out Transform elbow))
                elbow.localRotation *= Quaternion.AngleAxis(degrees,
                    (boneMap!=null?boneMap.AimCorrection("Left_LowerArm"):Quaternion.identity)*Vector3.forward);
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
            StabilizeBodyPose();
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
            StabilizeBodyPose();
        }

        internal void ConfigureBodyStability(bool holding,Vector3 eyePosition,float dt)
        {
            if(holding && !holdBody)
            {
                Array.Copy(displayedBodyPose,safeBodyPose,safeBodyPose.Length);
                safeBodyScale=displayedBodyScale;safeHeadOffset=displayedHeadOffset;
            }
            stabilizeBody=true;bodyEyePosition=eyePosition;holdBody=holding;
            bodyRecovery=holding?0:Mathf.MoveTowards(bodyRecovery,1,Mathf.Max(0,dt)/.25f);
        }
        private void CacheStableBody(Transform skeleton,Vector3 eyePosition)
        {
            for(int i=0;i<bodyChain.Length;i++)safeBodyPose[i]=(new Pose(bodyChain[i].localPosition,bodyChain[i].localRotation),bodyChain[i].localScale);
            safeBodyScale=Root.localScale;
            safeHeadOffset=Root.InverseTransformVector(skeleton.position-eyePosition);
            CacheDisplayedBody(skeleton,eyePosition);
        }
        private void CacheDisplayedBody(Transform skeleton,Vector3 eyePosition)
        {
            for(int i=0;i<bodyChain.Length;i++)displayedBodyPose[i]=(new Pose(bodyChain[i].localPosition,bodyChain[i].localRotation),bodyChain[i].localScale);
            displayedBodyScale=Root.localScale;
            displayedHeadOffset=Root.InverseTransformVector(skeleton.position-eyePosition);
        }
        private void StabilizeBodyPose()
        {
            if(!stabilizeBody || !joints.TryGetValue("Skeleton",out var skeleton) || bodyChain.Length==0)return;
            if(!holdBody && bodyRecovery>=1) { CacheStableBody(skeleton,bodyEyePosition);return; }
            Vector3 desiredOffset=Root.InverseTransformVector(skeleton.position-bodyEyePosition);
            Root.localScale=Vector3.Lerp(safeBodyScale,Root.localScale,bodyRecovery);
            for(int i=0;i<bodyChain.Length;i++)
            {
                Transform joint=bodyChain[i];var safe=safeBodyPose[i];
                joint.SetLocalPositionAndRotation(Vector3.Lerp(safe.pose.position,joint.localPosition,bodyRecovery),
                    Quaternion.Slerp(safe.pose.rotation,joint.localRotation,bodyRecovery));
                joint.localScale=Vector3.Lerp(safe.scale,joint.localScale,bodyRecovery);
            }
            // Follow real head translation, while keeping the torso/neck attachment independent of head rotation.
            skeleton.position=bodyEyePosition+Root.TransformVector(Vector3.Lerp(safeHeadOffset,desiredOffset,bodyRecovery));
            CacheDisplayedBody(skeleton,bodyEyePosition);
        }

        private bool AlignYaw(Transform skeleton, Transform joint, TrackedBone tracked, Transform player)
        {
            if (!tracked.HasTrackingRotation || (stabilizeBody && holdBody))
                return false;

            // Remove the joint's bind-pose axes to compare the avatar and tracking forward vectors.
            Quaternion avatarFacing = joint.rotation * Quaternion.Inverse(rest[jointNames[joint]].rootRotation);
            Quaternion trackedFacing = player.rotation * tracked.PlayerPose.rotation;
            if(tracked.Id=="Head" &&
                (Mathf.Abs(Vector3.Dot(trackedFacing*Vector3.forward,Vector3.up))>.94f ||
                 Vector3.Dot(trackedFacing*Vector3.up,Vector3.up)<.35f ||
                 Mathf.Abs(Vector3.Dot(avatarFacing*Vector3.forward,Vector3.up))>.94f ||
                 Vector3.Dot(avatarFacing*Vector3.up,Vector3.up)<.35f))return false;
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
            foreach (var pair in jointPaths)
            {
                Transform bone=pair.Value.Length==0?clone:clone.Find(pair.Value);
                if (bone!=null) bones.Add(pair.Key,bone);
            }
            return bones;
        }
        private static string RelativePath(Transform joint,Transform root)
        {
            var names=new Stack<string>();
            for (Transform t=joint;t!=root && t!=null;t=t.parent) names.Push(t.name);
            return string.Join("/",names);
        }
    }
}
