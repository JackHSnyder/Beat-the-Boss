using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    internal static class BossArmRetargetingChecks
    {
        [MenuItem("Tools/Beat the Boss/Check boss arm retargeting")]
        internal static string Run()
        {
            int frames=0;
            foreach(var character in Resources.Load<GameAssetCatalog>("Game Asset Catalog").characters)
            {
                var holder=new GameObject("Boss arm check") {hideFlags=HideFlags.HideAndDontSave};
                holder.SetActive(false);
                holder.transform.SetPositionAndRotation(new Vector3(2,.4f,-3),Quaternion.Euler(0,133,0));
                try
                {
                    var visual=UnityEngine.Object.Instantiate(character.prefab,holder.transform,false);
                    var rig=holder.AddComponent<BossRig>();rig.rigRoot=visual.transform;rig.MapStylized();
                    Vector3 head=rig.Joint("Head").position,hips=rig.Joint("Hips").position;
                    rig.Initialize();
                    Require(rig.bones.Count==51,"Bone mapping changed: "+character.id);
                    Require(Vector3.Distance(head,rig.Joint("Head").position)<1e-5f && Vector3.Distance(hips,rig.Joint("Hips").position)<1e-5f,"Arm calibration moved torso/head");
                    var upper=new Vector3[2];var reach=new Vector3[2];
                    for(int side=0;side<2;side++)
                    {
                        string name=side==0?"Left":"Right";
                        upper[side]=holder.transform.InverseTransformPoint(rig.Joint(name+"UpperArm").position);
                        reach[side]=holder.transform.InverseTransformPoint(rig.Joint(name+"Wrist").position)-upper[side];
                    }
                    var joints=new List<PoseJoint>();
                    var animation=new MoveAnimation {frames=new[]{new MoveAnimationFrame {time=0,joints=Array.Empty<PoseJoint>()},new MoveAnimationFrame {time=1,joints=Array.Empty<PoseJoint>()}}};
                    for(int frame=0;frame<120;frame++)
                    {
                        joints.Clear();float angle=65+Mathf.Sin(frame*Mathf.PI/60)*4;
                        for(int side=0;side<2;side++)
                        {
                            string name=side==0?"Left":"Right";float sign=side==0?1:-1;
                            joints.Add(new PoseJoint {id=name+"UpperArm",localRotation=Quaternion.AngleAxis(sign*angle,Vector3.forward)});
                            joints.Add(new PoseJoint {id=name+"Forearm",localRotation=Quaternion.identity});
                            joints.Add(new PoseJoint {id=name+"Wrist",localRotation=Quaternion.identity});
                        }
                        rig.BeginPose();rig.Apply(joints,animation,1);
                        for(int side=0;side<2;side++)
                        {
                            string name=side==0?"Left":"Right";float sign=side==0?1:-1;
                            Vector3 wrist=holder.transform.InverseTransformPoint(rig.Joint(name+"Wrist").position);
                            Require((side==0?wrist.x:-wrist.x)<-.1f,"Idle carried wrist across torso: "+character.id);
                            Vector3 expected=upper[side]+Quaternion.AngleAxis(sign*angle,Vector3.forward)*reach[side];
                            Require(Vector3.Distance(wrist,expected)<.0002f,"Arm rotation changed across native helper bones: "+character.id);
                        }
                        frames++;
                    }
                    Vector3 left=rig.Joint("LeftWrist").position,right=rig.Joint("RightWrist").position;
                    rig.Initialize();Require(Vector3.Distance(left,rig.Joint("LeftWrist").position)<1e-6f,"Initialize recalibrated an animated pose");
                    var fingers=new MoveAnimation {frames=animation.frames,excludedBones=new List<string>{"LeftUpperArm","LeftForearm","LeftWrist","RightUpperArm","RightForearm","RightWrist"}};
                    rig.Apply(new[]{new PoseJoint{id="RightIndexProximal",localRotation=Quaternion.AngleAxis(30,Vector3.right)}},fingers,1);
                    Require(Vector3.Distance(left,rig.Joint("LeftWrist").position)<1e-6f && Vector3.Distance(right,rig.Joint("RightWrist").position)<1e-6f,"Finger-only layer moved idle wrists");
                }
                finally {UnityEngine.Object.DestroyImmediate(holder);}
            }
            return "PASS: "+frames+" idle frames across all three models, correct hand sides, rotated origins, native helper parents, head/hips unchanged, idempotent binding, and finger-only masks.";
        }
        private static void Require(bool value,string message) {if(!value)throw new InvalidOperationException(message);}
    }
}
