using System;
using Meta.XR.Movement.Retargeting;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    public static class BodyHeadingStabilityChecks
    {
        public static string Run()
        {
            const float dt=1f/60;
            var heading=new StableBodyHeading();Quaternion reference=Quaternion.Euler(0,30,0);
            for(int frame=0;frame<30;frame++)heading.Update(Quaternion.Euler(0,30,0),reference,Vector3.up,dt);
            Vector3 facing=heading.Forward;
            for(int pitch=0;pitch<=180;pitch++)
            {
                heading.Update(Quaternion.Euler(pitch,30,0),reference,Vector3.up,dt);
                Require(Vector3.Angle(heading.Forward,facing)<.1f,"Heading reversed while crossing vertical.");
            }
            Require(heading.Holding,"An inverted head must hold the body.");
            for(int i=0;i<200;i++)
            {
                heading.Update(Quaternion.Euler(90+Mathf.Sin(i)*.5f,i%2==0?30:210,Mathf.Sin(i*.7f)*35),reference,Vector3.up,dt);
                Require(heading.Holding && Vector3.Angle(heading.Forward,facing)<.1f,"Near-vertical jitter changed body heading.");
            }
            heading.Update(Quaternion.Euler(50,150,0),reference,Vector3.up,dt);
            Require(!heading.Holding && Vector3.Angle(heading.Forward,facing)<=6.01f,"Recovery snapped to a different heading.");
            for(int i=0;i<60;i++)heading.Update(Quaternion.Euler(50,150,0),reference,Vector3.up,dt);
            Require(Vector3.Angle(heading.Forward,Quaternion.Euler(0,150,0)*Vector3.forward)<.1f,"Normal turning did not recover.");
            heading.Update(Quaternion.Euler(90,150,0),reference,Vector3.up,dt);facing=heading.Forward;
            Quaternion orbit=Quaternion.Euler(0,90,0);
            heading.Update(orbit*Quaternion.Euler(90,150,0),orbit*reference,Vector3.up,dt);
            Require(Vector3.Angle(heading.Forward,orbit*facing)<.1f,"Player-origin rotation did not carry the held heading.");
            var initial=new StableBodyHeading();initial.Update(Quaternion.Euler(120,210,0),reference,Vector3.up,dt);
            Require(initial.Holding && Vector3.Angle(initial.Forward,reference*Vector3.forward)<.1f,"Starting inverted did not use a stable fallback.");

            int heldFrames=0;
            foreach(var asset in Resources.Load<GameAssetCatalog>("Game Asset Catalog").characters)
                foreach(bool native in new[]{false,true})
                {
                    var holder=new GameObject("Body heading regression (temporary)");holder.SetActive(false);
                    try
                    {
                        var instance=UnityEngine.Object.Instantiate(asset.prefab,holder.transform,false);
                        var rig=new AvatarPoseRig(instance.GetComponentInChildren<CharacterRetargeter>(true));
                        rig.TryGetJoint("Skeleton",out var skeleton);rig.TryGetJoint("Hips",out var hips);
                        rig.TryGetJoint("Spine",out var spine);rig.TryGetJoint("Neck",out var neck);
                        Vector3 eye=new Vector3(0,1.7f,0);var frame=new TrackedPose();
                        void Sample(Quaternion rotation,Vector3 position)
                        {
                            frame.Clear(native?TrackingMode.FullBody:TrackingMode.Devices);
                            frame.Add(new TrackedBone("Head",null,new Pose(position,rotation),rotation));
                            if(native)frame.Add(new TrackedBone("Hips",null,new Pose(new Vector3(0,.9f,0),Quaternion.identity),Quaternion.identity));
                        }
                        void Apply() { if(native)rig.PlaceRetargetedBody(frame,holder.transform);else rig.ApplyLive(frame,holder.transform); }
                        Sample(Quaternion.Euler(45,30,0),eye);rig.ConfigureBodyStability(false,eye,dt);Apply();
                        Vector3 attachment=neck.position-eye;Quaternion skeletonRotation=skeleton.rotation,hipRotation=hips.rotation;
                        for(int i=0;i<90;i++)
                        {
                            rig.Reset();
                            if(native)
                            {
                                // Simulate a tracking SDK changing its guessed body pose as the headset inverts.
                                skeleton.localRotation=Quaternion.Euler(i*23,180,90);hips.localRotation=Quaternion.Euler(i*13,i*41,80);
                                spine.localRotation=Quaternion.Euler(i*7,180,160);instance.transform.localScale=Vector3.one*(i%2==0?.8f:1.2f);
                            }
                            Vector3 movedEye=eye+new Vector3(Mathf.Sin(i)*.02f,i*.001f,.03f);
                            Sample(Quaternion.Euler(75+i*2,i%2==0?30:210,i*3),movedEye);
                            rig.ConfigureBodyStability(true,movedEye,dt);Apply();
                            Require(Vector3.Distance(neck.position-movedEye,attachment)<.0002f,asset.title+" changed its neck attachment with head rotation.");
                            Require(Quaternion.Angle(skeleton.rotation,skeletonRotation)<.1f && Quaternion.Angle(hips.rotation,hipRotation)<.1f,
                                asset.title+" rotated its held torso.");heldFrames++;
                        }
                        rig.Reset();Sample(Quaternion.Euler(0,210,0),eye);rig.ConfigureBodyStability(false,eye,dt);Apply();
                        Require(Quaternion.Angle(skeleton.rotation,skeletonRotation)<15,"Body recovery snapped 180 degrees.");
                        Vector3 recoveringAttachment=neck.position-eye;Quaternion recoveringFacing=skeleton.rotation;
                        rig.Reset();Sample(Quaternion.Euler(100,30,180),eye);rig.ConfigureBodyStability(true,eye,dt);Apply();
                        Require(Vector3.Distance(neck.position-eye,recoveringAttachment)<.0002f && Quaternion.Angle(skeleton.rotation,recoveringFacing)<.1f,
                            "Re-entering an extreme pose snapped back to an older body pose.");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(holder); }
                }
            return $"PASS: vertical/inverted heading sweeps and jitter, upright recovery, origin rotation, inverted startup; {heldFrames} frozen-torso frames across all three models and both tracking paths preserve the neck-to-head offset despite head rotation and changing SDK guesses.";
        }
        private static void Require(bool value,string error) { if(!value)throw new Exception(error); }
    }
}
