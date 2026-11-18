using System;
using System.Linq;
using Meta.XR.Movement.Retargeting;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    public static class AvatarPresentationChecks
    {
        public static string Run()
        {
            var catalog=Resources.Load<GameAssetCatalog>("Game Asset Catalog");
            int twists=0;
            foreach(var asset in catalog.characters)
            {
                var holder=new GameObject("Avatar presentation check (temporary)");holder.SetActive(false);
                try
                {
                    var instance=UnityEngine.Object.Instantiate(asset.prefab,holder.transform,false);
                    var avatar=instance.GetComponentInChildren<CharacterRetargeter>(true);var rig=new AvatarPoseRig(avatar);
                    instance.transform.rotation=Quaternion.Euler(0,35,0);
                    foreach(string side in new[]{"Left","Right"})
                    {
                        rig.Reset();rig.TryGetJoint(side+"_LowerArm",out var forearm);rig.TryGetJoint(side+"_Hand",out var hand);
                        Vector3 axis=(hand.position-forearm.position).normalized;Pose wrist=new Pose(hand.position,hand.rotation);
                        var helpers=instance.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith(side+"ArmLowerTwist",StringComparison.Ordinal) || t.name==side+"WristTwist").ToArray();
                        var neutral=helpers.Select(t=>new {rotation=t.rotation,position=t.position}).ToArray();
                        hand.rotation=Quaternion.AngleAxis(120,axis)*wrist.rotation;Quaternion target=hand.rotation;
                        rig.ApplyForearmTwist(side);
                        Require(Vector3.Distance(hand.position,wrist.position)<.0001f && Quaternion.Angle(hand.rotation,target)<.1f,"Twist distribution moved the tracked wrist.");
                        for(int i=0;i<helpers.Length;i++)
                        {
                            float fraction=Mathf.Clamp01(Vector3.Dot(neutral[i].position-forearm.position,axis)/Vector3.Distance(forearm.position,wrist.position));
                            Quaternion expected=Quaternion.AngleAxis(120*fraction,axis)*neutral[i].rotation;
                            Require(Quaternion.Angle(helpers[i].rotation,expected)<.1f,asset.title+" forearm twist was not distributed along the arm.");twists++;
                        }
                        rig.RestoreHandPose();
                    }
                    rig.Reset();
                    rig.TryGetJoint("Spine",out var spine);rig.TryGetJoint("UpperChest",out var chest);rig.TryGetJoint("Hips",out var hips);
                    rig.TryGetJoint("Left_Foot",out var foot);rig.TryGetJoint("Left_Hand",out var left);rig.TryGetJoint("Right_Hand",out var right);
                    Vector3 chestBefore=chest.position,hipsBefore=hips.position,footBefore=foot.position,rootBefore=instance.transform.position;
                    Pose spineBefore=new Pose(spine.localPosition,spine.localRotation),leftBefore=new Pose(left.position,left.rotation),rightBefore=new Pose(right.position,right.rotation);
                    for(int frame=0;frame<30;frame++)
                    {
                        rig.ApplyBodyPosture(Vector3.forward,Vector3.up,.12f,8);
                        Require(Vector3.Dot(chest.position-chestBefore,Vector3.forward)<-.1f,"Looking down did not clear the chest.");
                        Require(Vector3.Distance(hips.position,hipsBefore)<.0001f && Vector3.Distance(foot.position,footBefore)<.0001f &&
                            Vector3.Distance(instance.transform.position,rootBefore)<.0001f,"Chest clearance moved the hips, feet or player origin.");
                        Require(Vector3.Distance(left.position,leftBefore.position)<.0001f && Vector3.Distance(right.position,rightBefore.position)<.0001f &&
                            Quaternion.Angle(left.rotation,leftBefore.rotation)<.1f && Quaternion.Angle(right.rotation,rightBefore.rotation)<.1f,"Chest clearance displaced a tracked hand.");
                        rig.RestoreBodyPosture();
                        Require(Vector3.Distance(chest.position,chestBefore)<.0001f && Vector3.Distance(spine.localPosition,spineBefore.position)<.0001f &&
                            Quaternion.Angle(spine.localRotation,spineBefore.rotation)<.1f,"Chest clearance accumulated between frames.");
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(holder); }
            }
            Require(PlayerAvatarDriver.LookDownAmount(Vector3.forward,Vector3.up,20,70)==0,"Level gaze should not move the body.");
            Require(PlayerAvatarDriver.LookDownAmount(Vector3.up,Vector3.up,20,70)==0,"Upward gaze should not move the body.");
            Require(PlayerAvatarDriver.LookDownAmount(Vector3.down,Vector3.up,20,70)==1,"Straight-down gaze should provide full clearance.");
            float middle=PlayerAvatarDriver.LookDownAmount(Quaternion.Euler(45,0,0)*Vector3.forward,Vector3.up,20,70);
            Require(middle>.45f && middle<.55f,"The look-down ramp is not gradual.");
            return $"PASS: {twists} forearm twist helpers share wrist roll without moving the palms; all three avatars gain chest clearance while feet, hips and hands stay anchored; repeated correction restores cleanly; gaze thresholds behave correctly.";
        }
        private static void Require(bool value,string error) { if(!value)throw new Exception(error); }
    }
}
