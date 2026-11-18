using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Meta.XR.Movement.Retargeting;
using UnityEngine;
using UnityEngine.Networking;

namespace BeatTheBoss.Poses
{
    internal static class DownloadedAssetLibrary
    {
        internal const string DefaultCharacter = "character-stylized";
        private const string EquippedKey = "Assets.PlayerCharacter";
        internal sealed class Asset
        {
            internal string id, title, path, bundlePath, prefabName;
            internal bool character, protectedAsset;
            internal AudioClip clip;
            internal GameObject prefab;
        }
        [Serializable] private sealed class Removed { public List<string> ids = new(); }
        [Serializable] internal sealed class CharacterFile { public int version=1; public string id, title, prefab; }
        internal static string Root => Path.Combine(Application.persistentDataPath,"DownloadedAssets");
        internal static string SongsFolder => Path.Combine(Root,"Songs");
        internal static string CharactersFolder => Path.Combine(Root,"CharacterModels");
        private static string RemovedPath => Path.Combine(Root,"removed-bundled-assets.json");
        private static readonly List<Asset> assets = new();
        private static readonly Dictionary<string,AssetBundle> bundles = new();
        private static bool initialized;
        internal static string Status { get; private set; }
        internal static event Action Changed;
        internal static IEnumerable<Asset> All { get { Ensure(); return assets; } }
        internal static string Equipped => PlayerPrefs.GetString(EquippedKey,DefaultCharacter);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            foreach (var bundle in bundles.Values) if (bundle!=null) bundle.Unload(false);
            bundles.Clear(); assets.Clear(); initialized=false; Changed=null;
        }
        private static void Ensure() { if (!initialized) Refresh(); }
        internal static Asset Find(string id) { Ensure(); return assets.Find(a=>a.id==id); }
        internal static string Title(string id, bool character=false) => string.IsNullOrEmpty(id)
            ? (character?"StylizedCharacter":"No song") : Find(id)?.title ?? id+" (missing)";
        internal static void Refresh()
        {
            // Keep live instances intact, but reopen packages on the next selection so updated files are read.
            foreach (var bundle in bundles.Values) if (bundle!=null) bundle.Unload(false);
            bundles.Clear();
            var previous=assets.ToDictionary(a=>a.id);
            assets.Clear(); initialized=true; int skipped=0;
            try
            {
                Directory.CreateDirectory(SongsFolder); Directory.CreateDirectory(CharactersFolder);
                Removed removed=ReadRemoved();
                var catalog=Resources.Load<GameAssetCatalog>("Game Asset Catalog");
                if (catalog!=null)
                {
                    foreach (var song in catalog.songs)
                        if (song.clip!=null && !removed.ids.Contains(song.id))
                            Add(new Asset {id=song.id,title=song.title,clip=song.clip});
                    foreach (var model in catalog.characters)
                        if (model.prefab!=null && (model.protectedAsset || !removed.ids.Contains(model.id)))
                            Add(new Asset {id=model.id,title=model.title,prefab=model.prefab,character=true,protectedAsset=model.protectedAsset});
                }
                foreach (string file in Directory.EnumerateFiles(SongsFolder).OrderBy(p=>p,StringComparer.OrdinalIgnoreCase))
                {
                    if (AudioTypeFor(file)==AudioType.UNKNOWN) continue;
                    try
                    {
                        using var stream=File.OpenRead(file); using var sha=SHA256.Create();
                        string id="song-"+BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
                        Add(new Asset {id=id,title=Path.GetFileNameWithoutExtension(file),path=file,
                            clip=previous.TryGetValue(id,out var old)?old.clip:null});
                    }
                    catch (Exception e) when (AnimationStore.IsFileError(e)) { skipped++; }
                }
                foreach (string dir in Directory.EnumerateDirectories(CharactersFolder).OrderBy(p=>p,StringComparer.OrdinalIgnoreCase))
                {
                    string file=Path.Combine(dir,"asset.json");
                    if (!File.Exists(file)) continue;
                    try
                    {
                        var doc=JsonUtility.FromJson<CharacterFile>(File.ReadAllText(file));
                        string bundle=Path.Combine(dir,"character.bundle");
                        if (doc==null || doc.version!=1 || !ValidId(doc.id) || string.IsNullOrWhiteSpace(doc.title) ||
                            string.IsNullOrWhiteSpace(doc.prefab) || !File.Exists(bundle)) throw new InvalidDataException("Invalid character package.");
                        if (assets.Any(a=>a.id==doc.id)) throw new InvalidDataException("Duplicate asset ID.");
                        Add(new Asset {id=doc.id,title=doc.title,character=true,path=file,bundlePath=bundle,prefabName=doc.prefab});
                    }
                    catch (Exception e) when (AnimationStore.IsFileError(e)) { skipped++; }
                }
                assets.Sort((a,b)=>StringComparer.OrdinalIgnoreCase.Compare(a.title,b.title));
                Status=$"{assets.Count} assets available."+(skipped>0?$" {skipped} invalid packages skipped.":"");
            }
            catch (Exception e) when (AnimationStore.IsFileError(e)) { Status="Couldn't refresh assets: "+e.Message; }
            Changed?.Invoke();
        }
        private static void Add(Asset asset) { if (!assets.Any(a=>a.id==asset.id)) assets.Add(asset); }
        internal static bool ValidId(string id) => !string.IsNullOrWhiteSpace(id) && id.Length<=96 &&
            id.All(c=>c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_' or '.');
        private static Removed ReadRemoved()
        {
            var removed=File.Exists(RemovedPath)?JsonUtility.FromJson<Removed>(File.ReadAllText(RemovedPath)) ?? new Removed():new Removed();
            removed.ids??=new List<string>();
            return removed;
        }
        internal static void Delete(string id)
        {
            Asset asset=Find(id) ?? throw new FileNotFoundException("This asset is no longer available.");
            if (asset.protectedAsset || id==DefaultCharacter) throw new InvalidOperationException("StylizedCharacter cannot be deleted.");
            // If the equipped model is removed, fall back before unloading its package.
            if (asset.character && Equipped==id) Equip(DefaultCharacter);
            if (asset.path==null)
            {
                var removed=ReadRemoved(); if (!removed.ids.Contains(id)) removed.ids.Add(id);
                PoseStore.WriteJson(RemovedPath,removed,File.Exists(RemovedPath));
            }
            else
            {
                if (asset.bundlePath!=null)
                {
                    if (bundles.Remove(asset.bundlePath,out var bundle) && bundle!=null) bundle.Unload(false);
                    File.Delete(asset.bundlePath);
                }
                File.Delete(asset.path);
            }
            Refresh();
        }
        internal static void Equip(string id)
        {
            ResolveCharacter(id); // Validate before changing the saved preference or any live avatar.
            foreach (var driver in UnityEngine.Object.FindObjectsByType<PlayerAvatarDriver>(FindObjectsInactive.Include,FindObjectsSortMode.None))
                driver.EquipCharacter(id);
            PlayerPrefs.SetString(EquippedKey,id); PlayerPrefs.Save(); Changed?.Invoke();
        }
        internal static GameObject ResolveCharacter(string id)
        {
            Asset asset=Find(string.IsNullOrEmpty(id)?DefaultCharacter:id);
            if (asset==null || !asset.character) throw new FileNotFoundException("Character model is missing: "+id);
            if (asset.prefab!=null) { ValidateCharacter(asset.prefab); return asset.prefab; }
            if (!bundles.TryGetValue(asset.bundlePath,out var bundle) || bundle==null)
            {
                bundle=AssetBundle.LoadFromFile(asset.bundlePath);
                if (bundle==null) throw new InvalidDataException("Couldn't open the character package. Check its build platform.");
                bundles[asset.bundlePath]=bundle;
            }
            var prefab=bundle.LoadAsset<GameObject>(asset.prefabName);
            ValidateCharacter(prefab);
            return prefab;
        }
        internal static void ValidateCharacter(GameObject prefab)
        {
            var retargeter=prefab!=null?prefab.GetComponentInChildren<CharacterRetargeter>(true):null;
            if (retargeter==null || retargeter.JointPairs==null) throw new InvalidDataException("The character needs a configured CharacterRetargeter.");
            var mapping=retargeter.GetComponent<CharacterBoneMap>();
            var names=retargeter.JointPairs.Where(p=>p.Joint!=null).Select(p=>mapping!=null?mapping.NameOf(p.Joint):p.Joint.name).ToArray();
            var joints=new HashSet<string>(names);
            if (joints.Count!=names.Length) throw new InvalidDataException("The character contains duplicate joint names.");
            var reference=Find(DefaultCharacter)?.prefab?.GetComponentInChildren<CharacterRetargeter>(true);
            if (reference==null) throw new InvalidDataException("The default character is unavailable.");
            foreach (var pair in reference.JointPairs)
                if (pair.Joint!=null && !joints.Contains(pair.Joint.name))
                    throw new InvalidDataException("The character is missing a mapped bone: "+pair.Joint.name);
        }
        internal static AudioType AudioTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
        { ".mp3"=>AudioType.MPEG, ".wav"=>AudioType.WAV, ".ogg"=>AudioType.OGGVORBIS, _=>AudioType.UNKNOWN };
        internal static IEnumerator LoadSong(string id, Action<AudioClip,string> done)
        {
            Asset asset=Find(id);
            if (asset==null || asset.character) { done(null,"Song is missing: "+id); yield break; }
            if (asset.clip!=null) { done(asset.clip,null); yield break; }
            using var request=UnityWebRequestMultimedia.GetAudioClip(new Uri(asset.path).AbsoluteUri,AudioTypeFor(asset.path));
            yield return request.SendWebRequest();
            if (request.result!=UnityWebRequest.Result.Success) { done(null,request.error); yield break; }
            asset.clip=DownloadHandlerAudioClip.GetContent(request); asset.clip.name=asset.title;
            done(asset.clip,null);
        }
    }
}
