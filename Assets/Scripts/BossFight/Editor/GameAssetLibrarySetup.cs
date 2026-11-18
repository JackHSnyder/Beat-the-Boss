using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    public static class GameAssetLibrarySetup
    {
        [MenuItem("Tools/Beat the Boss/Set up game asset catalog")]
        public static void Configure()
        {
            const string path="Assets/Resources/Game Asset Catalog.asset";
            var catalog=AssetDatabase.LoadAssetAtPath<GameAssetCatalog>(path);
            if (catalog!=null) return;
            catalog=ScriptableObject.CreateInstance<GameAssetCatalog>();
            catalog.songs=new[]
            {
                new GameAssetCatalog.Song {id="song-ethreal",title="ethreal",clip=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Erika Music/ethreal.wav")},
                new GameAssetCatalog.Song {id="song-chill",title="chill",clip=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Erika Music/chill.mp3")}
            };
            catalog.characters=new[] {new GameAssetCatalog.Character
            {
                id=DownloadedAssetLibrary.DefaultCharacter,title="StylizedCharacter",protectedAsset=true,
                prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.meta.xr.sdk.movement/Shared/Prefabs/Character/StylizedCharacter.prefab")
            }};
            AssetDatabase.CreateAsset(catalog,path); AssetDatabase.SaveAssets();
        }
        [MenuItem("Tools/Beat the Boss/Export selected character package")]
        public static void ExportSelected()
        {
            Configure();
            GameObject prefab=Selection.activeObject as GameObject;
            string path=AssetDatabase.GetAssetPath(prefab);
            if (prefab==null || !path.EndsWith(".prefab",StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Select a configured character prefab in the Project window first.");
            DownloadedAssetLibrary.ValidateCharacter(prefab);
            string id="character-"+AssetDatabase.AssetPathToGUID(path);
            string output=Path.GetFullPath(Path.Combine("Temp","CharacterPackages",id));
            Directory.CreateDirectory(output);
            var build=new AssetBundleBuild {assetBundleName="character.bundle",assetNames=new[] {path}};
            var result=BuildPipeline.BuildAssetBundles(output,new[] {build},BuildAssetBundleOptions.ChunkBasedCompression,EditorUserBuildSettings.activeBuildTarget);
            if (result==null) throw new IOException("The character package could not be built.");
            File.WriteAllText(Path.Combine(output,"asset.json"),JsonUtility.ToJson(new DownloadedAssetLibrary.CharacterFile
                {id=id,title=prefab.name,prefab=path.ToLowerInvariant()},true));
            Debug.Log("Character package exported for "+EditorUserBuildSettings.activeBuildTarget+": "+output+". Copy asset.json and character.bundle together into DownloadedAssets/CharacterModels/"+id);
        }
    }
}
