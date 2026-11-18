using System;
using System.Linq;
using Meta.XR.Movement.Retargeting;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    public static class MetaCharacterChecks
    {
        public static string Run()
        {
            var catalog=Resources.Load<GameAssetCatalog>("Game Asset Catalog");
            var reference=catalog.characters.Single(a=>a.id==DownloadedAssetLibrary.DefaultCharacter).prefab.GetComponentInChildren<CharacterRetargeter>(true);
            var standard=new AvatarPoseRig(reference);
            foreach(string id in new[]{"character-realistic","character-tony"})
            {
                var asset=catalog.characters.Single(a=>a.id==id);
                Require(!asset.protectedAsset,"Additional characters should be deletable.");
                DownloadedAssetLibrary.ValidateCharacter(asset.prefab);
                var holder=new GameObject("Character check (temporary)"); holder.SetActive(false);
                try
                {
                    var instance=UnityEngine.Object.Instantiate(asset.prefab,holder.transform,false);
                    var avatar=instance.GetComponentInChildren<CharacterRetargeter>(true);
                    var rig=new AvatarPoseRig(avatar);
                    foreach(var pair in BoneMappings.Avatar)
                        Require(rig.TryGetJoint(pair.Key,out var joint) && joint!=null,asset.title+" lacks "+pair.Key);
                    Require(rig.TryGetRightIndexRay(out var ray) && ray.direction.sqrMagnitude>.99f,"Missing fingertip ray.");
                    rig.TryGetJoint("Right_IndexProximal",out var index);
                    Quaternion neutral=index.rotation;
                    var pose=new[]{new PoseJoint {id="RightIndexProximal",localRotation=Quaternion.AngleAxis(40,Vector3.right)}};
                    rig.ApplyPreview(pose,avatar.transform,rig.MapClone(avatar.transform));
                    Require(Quaternion.Angle(neutral,index.rotation)>30,"Finger animation did not retarget.");
                    rig.Reset();
                    Require(Vector3.Angle(rig.AimDirection("Right_IndexProximal",Vector3.forward),standard.AimDirection("Right_IndexProximal",Vector3.forward))<.1f,"Aim axes were not calibrated.");
                    var boss=holder.AddComponent<BossRig>(); boss.rigRoot=instance.transform; boss.MapStylized();boss.Initialize();
                    Require(boss.bones.Count==BoneMappings.Avatar.Count,"Boss mapping lost a universal bone.");
                    boss.BeginPose();
                    var animation=new MoveAnimation {frames=new[]{new MoveAnimationFrame {time=0,joints=pose},new MoveAnimationFrame {time=1,joints=pose}}};
                    boss.Apply(pose,animation,1);
                    Require(Quaternion.Angle(neutral,index.rotation)>30,"Boss finger animation did not retarget.");
                    var clone=UnityEngine.Object.Instantiate(avatar.gameObject,holder.transform,false);
                    using var mask=new PreviewBodyMask(clone,rig.MapClone(clone.transform));
                    Require(mask.Apply(new[]{PoseCategory.RightHand}),"Preview meshes are not readable.");
                    foreach(var mesh in clone.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        Require(mesh.sharedMaterials.All(m=>m!=null && m.shader!=null),"A character material is missing.");
                }
                finally { UnityEngine.Object.DestroyImmediate(holder); }
            }
            return "PASS: both saved characters, all 51 universal bones, finger motion in previews and boss rigs, fingertip rays, aim-axis calibration, readable body masks and material references.";
        }
        private static void Require(bool value,string message) { if(!value)throw new Exception(message); }
    }
}
