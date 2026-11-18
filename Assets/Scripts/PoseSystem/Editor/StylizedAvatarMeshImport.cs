using UnityEditor;

namespace BeatTheBoss.Poses
{
    internal sealed class StylizedAvatarMeshImport : AssetPostprocessor
    {
        public override uint GetVersion() => 1;

        private void OnPreprocessModel()
        {
            // The animation preview needs the skin weights and triangle indices for body-part selection.
            if (assetPath == "Packages/com.meta.xr.sdk.movement/Shared/ThirdParty/Unity/Armature.fbx")
                ((ModelImporter)assetImporter).isReadable = true;
        }
    }
}
