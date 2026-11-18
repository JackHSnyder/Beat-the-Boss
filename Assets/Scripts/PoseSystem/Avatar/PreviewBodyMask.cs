using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BeatTheBoss.Poses
{
    internal sealed class PreviewBodyMask : IDisposable
    {
        private readonly (SkinnedMeshRenderer Renderer, Mesh Source)[] meshes;
        private readonly List<Mesh> copies = new();
        private readonly Dictionary<Transform,string> boneNames=new();

        public PreviewBodyMask(GameObject preview,IReadOnlyDictionary<string,Transform> mappedBones=null)
        {
            if (mappedBones!=null) foreach(var pair in mappedBones) boneNames[pair.Value]=pair.Key;
            meshes = Array.ConvertAll(preview.GetComponentsInChildren<SkinnedMeshRenderer>(true),
                renderer => (renderer, renderer.sharedMesh));
        }

        public bool Apply(IReadOnlyCollection<PoseCategory> disabled)
        {
            Dispose();
            if (disabled == null || disabled.Count == 0)
                return true;

            foreach (var mesh in meshes)
                if (mesh.Source != null && !mesh.Source.isReadable)
                    return false;

            foreach (var mesh in meshes)
            {
                if (mesh.Source == null)
                    continue;

                bool[] visible = IncludedVertices(mesh.Renderer, mesh.Source, disabled);
                Mesh copy = Object.Instantiate(mesh.Source);
                copies.Add(copy);
                // Keep the original skinning and materials, and omit triangles crossing into a hidden region.
                var triangles = new List<int>();
                for (int submesh = 0; submesh < mesh.Source.subMeshCount; submesh++)
                {
                    int[] sourceIndices = mesh.Source.GetTriangles(submesh);
                    triangles.Clear();
                    for (int i = 0; i < sourceIndices.Length; i += 3)
                    {
                        int a = sourceIndices[i], b = sourceIndices[i + 1], c = sourceIndices[i + 2];
                        if (visible[a] && visible[b] && visible[c])
                        {
                            triangles.Add(a);
                            triangles.Add(b);
                            triangles.Add(c);
                        }
                    }
                    copy.SetTriangles(triangles, submesh, calculateBounds: false);
                }
                mesh.Renderer.sharedMesh = copy;
            }
            return true;
        }

        private bool[] IncludedVertices(SkinnedMeshRenderer renderer, Mesh source,
            IReadOnlyCollection<PoseCategory> disabled)
        {
            var visible = new bool[source.vertexCount];
            var bones = renderer.bones;
            var categories = Array.ConvertAll(bones, BoneCategory);
            var counts = source.GetBonesPerVertex();
            var weights = source.GetAllBoneWeights();
            int offset = 0;
            for (int vertex = 0; vertex < visible.Length; vertex++)
            {
                int count = vertex < counts.Length ? counts[vertex] : 0;
                // Unity stores the strongest influence first for each vertex.
                int bone = count > 0 ? weights[offset].boneIndex : -1;
                PoseCategory category = (uint)bone < categories.Length ? categories[bone] : PoseCategory.TorsoAndHead;
                visible[vertex] = !PoseMatchProfile.IsSessionIgnored(category, disabled);
                offset += count;
            }
            return visible;
        }

        private PoseCategory BoneCategory(Transform bone)
        {
            for (; bone != null; bone = bone.parent)
            {
                string name = boneNames.TryGetValue(bone,out var mapped)?mapped:bone.name;
                // The stylized mesh uses deformation copies for some finger joints.
                if (name.EndsWith("Def", StringComparison.Ordinal))
                    name = name[..^3];
                if (BoneMappings.Avatar.TryGetValue(name, out string id) && UniversalSkeleton.TryGet(id, out SkeletonBone joint))
                    return joint.Category;
                if (name == "Chest")
                    return PoseCategory.TorsoAndHead;
            }
            return PoseCategory.TorsoAndHead;
        }

        public void Dispose()
        {
            if (copies.Count == 0)
                return;
            foreach (var mesh in meshes)
                if (mesh.Renderer != null)
                    mesh.Renderer.sharedMesh = mesh.Source;
            foreach (Mesh copy in copies)
                Object.Destroy(copy);
            copies.Clear();
        }
    }
}
