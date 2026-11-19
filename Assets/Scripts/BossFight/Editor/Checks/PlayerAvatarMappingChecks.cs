using System;
using System.Linq;
using Meta.XR.Movement.Retargeting;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    public static class PlayerAvatarMappingChecks
    {
        public static string Run()
        {
            if(!Application.isPlaying)throw new InvalidOperationException("Run the avatar checks in Play mode.");
            var cameras=UnityEngine.Object.FindAnyObjectByType<OVRCameraRig>();
            var ownCamera=cameras.GetComponentInChildren<Camera>(true);
            var catalog=Resources.Load<GameAssetCatalog>("Game Asset Catalog");
            int gestures=0;
            foreach(string id in new[]{"character-realistic","character-tony"})
            {
                var asset=catalog.characters.Single(a=>a.id==id);
                var holder=new GameObject("Avatar mapping regression (temporary)");holder.SetActive(false);
                try
                {
                    var instance=UnityEngine.Object.Instantiate(asset.prefab,holder.transform,false);
                    var avatar=instance.GetComponentInChildren<CharacterRetargeter>(true);
                    var mapping=avatar.GetComponent<CharacterBoneMap>();var rig=new AvatarPoseRig(avatar);
                    instance.transform.SetPositionAndRotation(new Vector3(2,.3f,1),Quaternion.Euler(12,61,-9));
                    instance.transform.localScale=Vector3.one*.9f;
                    foreach(string side in new[]{"Left","Right"})
                    {
                        rig.TryGetJoint(side+"_Hand",out var wrist);rig.TryGetJoint(side+"_IndexProximal",out var index);rig.TryGetJoint(side+"_PinkyProximal",out var pinky);
                        Vector3 w=wrist.position,i=index.position,p=pinky.position;Quaternion expected=wrist.rotation;
                        wrist.rotation*=Quaternion.Euler(160,-70,90);
                        rig.AlignTrackedPalm(side,w,i,p);
                        Require(Quaternion.Angle(wrist.rotation,expected)<.1f,"Palm orientation depends on incorrect incoming SDK rotations.");
                    }
                    foreach(string side in new[]{"Left","Right"})foreach(string finger in new[]{"Thumb","Index","Middle","Ring","Pinky"})
                    {
                        string prefix=side+"_"+finger;
                        rig.TryGetJoint(prefix+"Proximal",out var a);rig.TryGetJoint(prefix+"Intermediate",out var b);
                        rig.TryGetJoint(prefix+"Distal",out var c);rig.TryGetJoint(prefix+"DistalEnd",out var d);
                        Require(a!=null && b!=null && c!=null && d!=null,"A complete finger chain is missing.");
                        Vector3 start=a.position,forward=(b.position-a.position).normalized;
                        Vector3 axis=Vector3.Cross(forward,instance.transform.up).normalized;
                        if(axis.sqrMagnitude<.1f)axis=instance.transform.right;
                        float ab=Vector3.Distance(a.position,b.position)*1.05f,bc=Vector3.Distance(b.position,c.position)*1.05f,cd=Vector3.Distance(c.position,d.position)*1.05f;
                        var joints=new[]{a,b,c};
                        for(int frame=0;frame<6;frame++)
                        {
                            float curl=frame%2==0?0:125;
                            Vector3 pa=start,pb=pa+Quaternion.AngleAxis(curl*.25f,axis)*forward*ab;
                            Vector3 pc=pb+Quaternion.AngleAxis(curl*.65f,axis)*forward*bc;
                            Vector3 pd=pc+Quaternion.AngleAxis(curl,axis)*forward*cd;
                            // Deliberately give the incoming rig wrong curl/roll axes, as SDK retargeting can do.
                            foreach(var joint in joints)joint.localRotation*=Quaternion.Euler(130,-70,35);
                            var before=joints.Select(j=>new {pose=new Pose(j.localPosition,j.localRotation),scale=j.localScale}).ToArray();
                            rig.AlignFingerPoints(side,finger,pa,pb,pc,pd);
                            Require(Vector3.Distance(a.localScale,before[0].scale)<.0001f && Vector3.Distance(b.localScale,before[1].scale)<.0001f,
                                "Knuckle scaling would inflate the finger's children.");
                            int lengthAxis=Mathf.Abs(d.localPosition.x)>Mathf.Abs(d.localPosition.y)?0:1;
                            if(Mathf.Abs(d.localPosition.z)>Mathf.Abs(d.localPosition[lengthAxis]))lengthAxis=2;
                            for(int axisIndex=0;axisIndex<3;axisIndex++)if(axisIndex!=lengthAxis)
                                Require(Mathf.Abs(c.localScale[axisIndex]-before[2].scale[axisIndex])<.0001f,"Fingertip thickness changed.");
                            Require(Vector3.Distance(a.position,pa)<.001f && Vector3.Distance(b.position,pb)<.001f &&
                                Vector3.Distance(c.position,pc)<.001f && Vector3.Distance(d.position,pd)<.001f,
                                asset.title+" "+side+" "+finger+" does not follow its tracked knuckles and tip.");
                            Require(Vector3.Dot((d.position-c.position).normalized,(pd-pc).normalized)>.999f,"A finger bent backwards.");
                            rig.RestoreHandPose();
                            for(int j=0;j<joints.Length;j++)Require(Vector3.Distance(joints[j].localScale,before[j].scale)<.0001f &&
                                Vector3.Distance(joints[j].localPosition,before[j].pose.position)<.0001f &&
                                Quaternion.Angle(joints[j].localRotation,before[j].pose.rotation)<.1f,"Finger correction accumulated between frames.");
                            rig.Reset();gestures++;
                        }
                    }
                    var visibility=holder.AddComponent<LocalAvatarHeadVisibility>();
                    visibility.Configure(mapping.firstPersonHiddenRenderers,cameras);
                    Require(mapping.firstPersonHiddenRenderers.Length>=4,"Missing eye, mouth or face renderers.");
                    var preview=UnityEngine.Object.Instantiate(asset.prefab,holder.transform,false);
                    visibility.Hide(ownCamera);
                    Require(mapping.firstPersonHiddenRenderers.All(r=>r.forceRenderingOff),"A local camera still sees part of its head.");
                    Require(instance.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r=>!mapping.firstPersonHiddenRenderers.Contains(r)).All(r=>!r.forceRenderingOff),"Hiding the head also hid the body.");
                    Require(preview.GetComponentsInChildren<Renderer>(true).All(r=>!r.forceRenderingOff),"The preview's head was hidden.");
                    visibility.Hide(null);
                    Require(mapping.firstPersonHiddenRenderers.All(r=>!r.forceRenderingOff),"Other cameras still have a hidden head.");
                    visibility.Hide(ownCamera);visibility.Restore();
                    Require(mapping.firstPersonHiddenRenderers.All(r=>!r.forceRenderingOff),"Head visibility was not restored after rendering.");
                }
                finally { UnityEngine.Object.DestroyImmediate(holder); }
            }
            return $"PASS: {gestures} open/closed finger cases on both hands and models, rotated/scaled rigs, bad incoming axes, knuckle/tip alignment, no scale drift, local-camera face/eyes/mouth hiding, and visible previews/other cameras.";
        }
        private static void Require(bool result,string message) { if(!result)throw new Exception(message); }
    }
}
