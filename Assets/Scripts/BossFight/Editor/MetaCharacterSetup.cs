using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Meta.XR.Movement.Retargeting;
using UnityEditor;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    public static class MetaCharacterSetup
    {
        private const string Destination="Assets/Characters/MetaSamples/";
        private static readonly string[] Sources={"BodyTrackingSamples/Body/Prefabs/RealisticCharacter.prefab",
            "FaceTrackingSamples/Face/Prefabs/BlendshapeMappingExampleARKIT_A2E.prefab"};
        [MenuItem("Tools/Beat the Boss/Set up additional Meta characters")]
        public static void Setup() { Import(); Register(); }
        public static void Import()
        {
            string root=UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.meta.xr.sdk.movement/package.json").resolvedPath;
            var lookup=new Dictionary<string,string>();
            foreach(string meta in Directory.EnumerateFiles(root,"*.meta",SearchOption.AllDirectories))
            {
                var match=Regex.Match(File.ReadAllText(meta),@"(?m)^guid: (\w+)");
                if(match.Success)lookup[match.Groups[1].Value]=meta[..^5];
            }
            string sampleRoot=Path.Combine(root,"Samples~")+Path.DirectorySeparatorChar;
            var pending=new Stack<string>(Sources.Select(p=>Path.Combine(sampleRoot,p)));
            var files=new HashSet<string>();
            while(pending.Count>0)
            {
                string file=pending.Pop();
                if(!File.Exists(file) || !files.Add(file))continue;
                foreach(string input in new[]{file,file+".meta"})
                {
                    if(!File.Exists(input) || !new[]{".prefab",".meta",".mat",".asset",".json",".shader",".cs"}.Contains(Path.GetExtension(input)))continue;
                    foreach(Match match in Regex.Matches(File.ReadAllText(input),@"guid: ([0-9a-f]{32})"))
                        if(lookup.TryGetValue(match.Groups[1].Value,out string dependency) && dependency.StartsWith(sampleRoot,StringComparison.OrdinalIgnoreCase))pending.Push(dependency);
                }
            }
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach(string file in files)
                {
                    string target=Destination+Path.GetRelativePath(sampleRoot,file).Replace('\\','/');
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    if(!File.Exists(target))File.Copy(file,target);
                    if(File.Exists(file+".meta") && !File.Exists(target+".meta"))File.Copy(file+".meta",target+".meta");
                }
                string license=Path.Combine(root,"LICENSE.txt");
                if(!File.Exists(license))license=Path.Combine(root,"LICENSE.md");
                if(File.Exists(license))File.Copy(license,Destination+"META-LICENSE"+Path.GetExtension(license),true);
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }
        public static void Register()
        {
            GameAssetLibrarySetup.Configure();
            var catalog=Resources.Load<GameAssetCatalog>("Game Asset Catalog");
            var entries=catalog.characters.ToList();
            for(int i=0;i<Sources.Length;i++)
            {
                string title=i==0?"RealisticCharacter":"Tony";
                string id=i==0?"character-realistic":"character-tony";
                string source=Destination+Sources[i];
                // Recording previews need access to the mesh's skin weights and triangles.
                string model=i==0?"BodyTrackingSamples/Body/Models/Realistic.fbx":"FaceTrackingSamples/Face/Models/BlendshapeMappingExampleARKIT.fbx";
                var importer=(ModelImporter)AssetImporter.GetAtPath(Destination+model);
                if(!importer.isReadable) { importer.isReadable=true; importer.SaveAndReimport(); }
                GameObject instance=PrefabUtility.LoadPrefabContents(source);
                try
                {
                    var retargeter=instance.GetComponentInChildren<CharacterRetargeter>(true);
                    if(retargeter==null)throw new InvalidDataException("The Meta sample is missing its character retargeter.");
                    instance.name=title;
                    foreach(Behaviour behaviour in instance.GetComponentsInChildren<Behaviour>(true))
                        behaviour.enabled=behaviour is CharacterRetargeter || behaviour is MetaSourceDataProvider;
                    var mapping=retargeter.GetComponent<CharacterBoneMap>() ?? retargeter.gameObject.AddComponent<CharacterBoneMap>();
                    mapping.enabled=true; mapping.bindings.Clear();
                    mapping.firstPersonHiddenRenderers=retargeter.GetComponentsInChildren<Renderer>(true).Where(r=>
                        new[]{"head","mouth","eye","cornea","brow","lash","hair"}.Any(part=>r.name.IndexOf(part,StringComparison.OrdinalIgnoreCase)>=0)).ToArray();
                    var transforms=instance.GetComponentsInChildren<Transform>(true).ToDictionary(t=>t.name);
                    var reference=catalog.characters.First(a=>a.id==DownloadedAssetLibrary.DefaultCharacter).prefab.GetComponentInChildren<CharacterRetargeter>(true);
                    var rest=reference.JointPairs.Where(p=>p.Joint!=null).ToDictionary(p=>p.Joint.name,p=>Quaternion.Inverse(reference.transform.rotation)*p.Joint.rotation);
                    foreach(var pair in Aliases())
                    {
                        if(!transforms.TryGetValue(pair.Value,out var joint))throw new InvalidDataException(title+" is missing "+pair.Value);
                        Quaternion correction=rest.TryGetValue(pair.Key,out var defaultRotation)
                            ? Quaternion.Inverse(Quaternion.Inverse(retargeter.transform.rotation)*joint.rotation)*defaultRotation:Quaternion.identity;
                        mapping.bindings.Add(new CharacterBoneMap.Binding {name=pair.Key,joint=joint,aimCorrection=correction});
                    }
                    if(i==1)FitTonyBody(instance);
                    string path="Assets/Characters/"+title+".prefab";
                    var prefab=PrefabUtility.SaveAsPrefabAsset(instance,path);
                    DownloadedAssetLibrary.ValidateCharacter(prefab);
                    entries.RemoveAll(e=>e.id==id);
                    entries.Add(new GameAssetCatalog.Character {id=id,title=title,prefab=prefab,protectedAsset=false});
                }
                finally { PrefabUtility.UnloadPrefabContents(instance); }
            }
            catalog.characters=entries.ToArray(); EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssetIfDirty(catalog);
            DownloadedAssetLibrary.Refresh();
        }
        private static void FitTonyBody(GameObject tony)
        {
            var realistic=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/RealisticCharacter.prefab");
            var source=realistic.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r=>r.name=="C_wetsuit_PLY");
            var sourceMap=realistic.GetComponentInChildren<CharacterBoneMap>(true);
            var targetMap=tony.GetComponentInChildren<CharacterBoneMap>(true);
            CharacterBoneMap.Binding Anchor(Transform joint)
            {
                // The samples have different ankle/twist-helper layouts. Keep each boot rigid on its foot.
                foreach(string side in new[]{"Left","Right"})
                    if(new[]{"Foot","Talo","Subtalar","Transversetarsal"}.Any(part=>joint.name.StartsWith(side+part,StringComparison.Ordinal)))
                        return sourceMap.bindings.Single(b=>b.name==side+"_Foot");
                for(Transform parent=joint;parent!=null;parent=parent.parent)
                {
                    var binding=sourceMap.bindings.Find(b=>b.joint==parent);
                    if(binding!=null)return binding;
                }
                throw new InvalidDataException("Cannot map body joint: "+joint.name);
            }
            var anchors=source.bones.Select(Anchor).ToArray();
            Transform Match(string name)=>targetMap.bindings.Single(b=>b.name==name).joint;
            var root=new GameObject("Fitted full body");root.transform.SetParent(tony.transform,false);
            root.transform.SetPositionAndRotation(source.transform.position,source.transform.rotation);
            root.transform.localScale=source.transform.lossyScale;
            var renderer=root.AddComponent<SkinnedMeshRenderer>();
            renderer.bones=anchors.Select(b=>Match(b.name)).ToArray();renderer.rootBone=Match("Skeleton");
            renderer.sharedMaterials=source.sharedMaterials;renderer.updateWhenOffscreen=true;
            // Fit joint positions without copying native bind axes (the rigs use different twist axes).
            // Bind the fitted mesh to Tony while retaining the original weights, normals and UVs.
            var mesh=UnityEngine.Object.Instantiate(source.sharedMesh);mesh.name="Tony fitted body";
            var vertices=mesh.vertices;
            var offsets=renderer.bones.Select((b,i)=>b.position-anchors[i].joint.position).ToArray();
            using(var counts=mesh.GetBonesPerVertex())
            using(var weights=mesh.GetAllBoneWeights())
            {
                int offset=0;
                for(int v=0;v<vertices.Length;v++)
                {
                    Vector3 offsetToFit=Vector3.zero;
                    for(int w=0;w<counts[v];w++)
                    {
                        var influence=weights[offset++];
                        offsetToFit+=offsets[influence.boneIndex]*influence.weight;
                    }
                    vertices[v]=root.transform.InverseTransformPoint(source.transform.TransformPoint(vertices[v])+offsetToFit);
                }
            }
            // Overlap Tony's wider neck base with a short raised collar rather than leaving an open seam.
            float collar=vertices.Max(v=>v.y);
            for(int i=0;i<vertices.Length;i++)
                vertices[i]+=Vector3.up*(.035f*Mathf.SmoothStep(0,1,Mathf.InverseLerp(collar-.07f,collar,vertices[i].y)));
            mesh.vertices=vertices;
            mesh.bindposes=renderer.bones.Select(b=>b.worldToLocalMatrix*root.transform.localToWorldMatrix).ToArray();mesh.RecalculateBounds();
            const string path="Assets/Characters/Tony Body.asset";
            var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(existing!=null) { EditorUtility.CopySerialized(mesh,existing);UnityEngine.Object.DestroyImmediate(mesh);mesh=existing;EditorUtility.SetDirty(mesh); }
            else AssetDatabase.CreateAsset(mesh,path);
            AssetDatabase.SaveAssetIfDirty(mesh);renderer.sharedMesh=mesh;renderer.localBounds=mesh.bounds;
        }
        private static Dictionary<string,string> Aliases()
        {
            var result=new Dictionary<string,string> { ["Skeleton"]="Root",["Hips"]="Hips",["Spine"]="SpineLower",
                ["Chest"]="SpineMiddle",["UpperChest"]="SpineUpper",["Neck"]="Neck",["Head"]="Head" };
            foreach(string side in new[]{"Left","Right"})
            {
                result[side+"_Shoulder"]=side+"Shoulder"; result[side+"_UpperArm"]=side+"ArmUpper";
                result[side+"_LowerArm"]=side+"ArmLower";result[side+"_Hand"]=side+"HandWrist";
                result[side+"_UpperLeg"]=side+"LegUpper";result[side+"_LowerLeg"]=side+"LegLower";
                result[side+"_Foot"]=side+"Foot";result[side+"_Toes"]=side+"FootBall";
                result[side+"_ThumbProximal"]=side+"HandThumbMeta";
                result[side+"_ThumbIntermediate"]=side+"HandThumbProximal";
                result[side+"_ThumbDistal"]=side+"HandThumbDistal";
                result[side+"_ThumbBase"]=side+"HandThumbTrapezium";
                result[side+"_PinkyBase"]=side+"HandPinkyMeta";
                foreach(string finger in new[]{"Index","Middle","Ring","Pinky"})
                    foreach(string segment in new[]{"Proximal","Intermediate","Distal"})
                        result[side+"_"+finger+segment]=side+"Hand"+finger+segment;
                foreach(string finger in new[]{"Thumb","Index","Middle","Ring","Pinky"})
                    result[side+"_"+finger+"DistalEnd"]=side+"Hand"+finger+"Tip";
            }
            return result;
        }
    }
}
