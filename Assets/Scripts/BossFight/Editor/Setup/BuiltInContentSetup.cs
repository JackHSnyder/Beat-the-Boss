using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    public static class BuiltInContentSetup
    {
        private const string Destination = "Assets/Resources/BuiltInContent";

        // Explicit snapshot only: builds never depend on this machine's save folder.
        [MenuItem("Tools/Beat the Boss/Package current saved content as built-in")]
        public static void PackageCurrent()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before packaging saved content.");
            var templates = AssetDatabase.FindAssets("t:LevelLoadout")
                .Select(id => AssetDatabase.LoadAssetAtPath<LevelLoadout>(AssetDatabase.GUIDToAssetPath(id))).ToArray();
            int count = 0;
            foreach (var group in new[] { ("Poses", "pose-"), ("Moves", "move-"), ("Animations", "animation-"), ("BossTimelines", "timeline-") })
            {
                string source = Path.Combine(Application.persistentDataPath, group.Item1);
                string target = Path.Combine(Destination, group.Item1);
                if (!Directory.Exists(source)) continue;
                Directory.CreateDirectory(target);
                foreach (string file in Directory.GetFiles(source, group.Item2 + "*.json"))
                {
                    string json = File.ReadAllText(file);
                    // Read source files directly so repeating this tool can update an existing snapshot.
                    if (group.Item1 == "Poses") PoseStore.ReadJson(json);
                    else if (group.Item1 == "Moves")
                    {
                        var move = MoveStore.ReadJson(json, out _);
                        foreach (var step in move.steps)
                            if (!string.IsNullOrWhiteSpace(step.poseName))
                                step.poseName = SavedFileNames.Resolve("pose", step.poseName, n => Path.Combine(Application.persistentDataPath, "Poses", "pose-" + n + ".json"));
                        if (!string.IsNullOrWhiteSpace(move.animationName)) move.animationName = SavedFileNames.Resolve("animation", move.animationName, AnimationStore.PathFor);
                        json = JsonUtility.ToJson(move, true);
                    }
                    else if (group.Item1 == "Animations") AnimationStore.ReadJson(json);
                    else
                    {
                        var doc = JsonUtility.FromJson<BossTimelineStore.Document>(json);
                        doc.name = Path.GetFileNameWithoutExtension(file).Substring(group.Item2.Length);
                        if (string.IsNullOrWhiteSpace(doc.levelId)) doc.levelId = "timeline-" + doc.name.ToUpperInvariant();
                        var template = templates.FirstOrDefault(t => t.Id == doc.levelId) ?? templates.FirstOrDefault();
                        var level = LevelLoadout.FromTimeline(doc, template);
                        foreach (var slot in level.Slots) slot.DefaultMove = MoveLoadout.Get(level, slot);
                        doc.loadoutSlots = LevelLoadout.CopySlots(level.Slots);
                        UnityEngine.Object.DestroyImmediate(level);
                        if (!string.IsNullOrWhiteSpace(doc.idle)) doc.idle = SavedFileNames.Resolve("animation", doc.idle, AnimationStore.PathFor);
                        foreach (var entry in doc.events)
                            if (!string.IsNullOrWhiteSpace(entry.animation)) entry.animation = SavedFileNames.Resolve("animation", entry.animation, AnimationStore.PathFor);
                        json = JsonUtility.ToJson(doc, true);
                    }
                    File.WriteAllText(Path.Combine(target, Path.GetFileName(file)), json);
                    count++;
                }
            }
            // Catalog references make Unity build the correct native assets for Android/Quest.
            var catalog = Resources.Load<GameAssetCatalog>("Game Asset Catalog");
            if (catalog == null) throw new InvalidDataException("The game asset catalog is missing.");
            foreach (var character in catalog.characters) character.protectedAsset = true;
            EditorUtility.SetDirty(catalog);
            foreach (var template in templates)
            {
                foreach (var slot in template.Slots) slot.DefaultMove = MoveLoadout.Get(template, slot);
                EditorUtility.SetDirty(template);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            BuiltInContent.Reset();
            Debug.Log($"Packaged {count} saved documents, {catalog.songs.Length} songs, and {catalog.characters.Length} characters as built-in content.");
        }

        [MenuItem("Tools/Beat the Boss/Check built-in content")]
        public static void ValidatePackaged()
        {
            foreach (var group in new[] { ("Poses", "pose-"), ("Moves", "move-"), ("Animations", "animation-"), ("BossTimelines", "timeline-") })
            {
                var resources = Resources.LoadAll<TextAsset>("BuiltInContent/" + group.Item1);
                if (resources.Length == 0) throw new InvalidDataException("No packaged content found in " + group.Item1);
                var names = PoseStore.ListFiles(Path.Combine(Application.persistentDataPath, group.Item1), group.Item2);
                foreach (var resource in resources)
                {
                    string path = Path.Combine(Application.persistentDataPath, group.Item1, resource.name + ".json");
                    if (!BuiltInContent.IsProtectedPath(path) || !BuiltInContent.Exists(path) ||
                        BuiltInContent.ReadAllText(path) != resource.text ||
                        !names.Contains(resource.name.Substring(group.Item2.Length), StringComparer.OrdinalIgnoreCase))
                        throw new InvalidDataException("Packaged resource did not resolve: " + resource.name);
                }
            }
            // Resolve every dependency through the same stores used in the player.
            var poseStore = new PoseStore(Path.Combine(Application.persistentDataPath, "Poses"));
            foreach (var name in PoseStore.ListFiles(Path.Combine(Application.persistentDataPath, "Poses"), "pose-").Where(n => BuiltInContent.IsProtected("Poses", "pose-", n)))
            {
                poseStore.Load(name);
                Reject(() => poseStore.Delete(name));
                Reject(() => poseStore.SaveDraft(poseStore.Load(name), name));
            }
            foreach (string name in MoveStore.List().Where(n => BuiltInContent.IsProtected("Moves", "move-", n)))
            {
                var move = MoveStore.Load(name, out _);
                foreach (var step in move.steps) if (step.kind == MoveStepKind.Pose) poseStore.Load(step.poseName);
                if (!string.IsNullOrWhiteSpace(move.animationName)) MoveStore.LoadAnimation(move);
                Reject(() => MoveStore.Delete(name));
                Reject(() => SavedFileNames.Write("move", name, name + " renamed", MoveStore.PathFor, move));
            }
            foreach (string name in AnimationStore.List().Where(n => BuiltInContent.IsProtected("Animations", "animation-", n)))
            {
                var animation = AnimationStore.Load(name);
                Reject(() => AnimationStore.Delete(name));
                Reject(() => AnimationStore.Save(name, animation, true));
            }
            DownloadedAssetLibrary.Refresh();
            foreach (string name in BossTimelineStore.List().Where(n => BuiltInContent.IsProtected("BossTimelines", "timeline-", n)))
            {
                var doc = BossTimelineStore.Load(name);
                if (!string.IsNullOrWhiteSpace(doc.idle)) AnimationStore.Load(doc.idle);
                foreach (var entry in doc.events) if (!string.IsNullOrWhiteSpace(entry.animation)) AnimationStore.Load(entry.animation);
                foreach (var slot in doc.loadoutSlots ?? Array.Empty<LevelLoadout.Slot>())
                    if (!string.IsNullOrWhiteSpace(slot.DefaultMove)) MoveStore.Load(slot.DefaultMove, out _);
                if (!string.IsNullOrWhiteSpace(doc.songId) && DownloadedAssetLibrary.Find(doc.songId)?.clip == null)
                    throw new InvalidDataException("Missing built-in song: " + doc.songId);
                DownloadedAssetLibrary.ResolveCharacter(doc.bossModelId);
                Reject(() => BossTimelineStore.Save(name, doc));
            }
            foreach (var asset in DownloadedAssetLibrary.All.Where(a => a.protectedAsset).ToArray()) Reject(() => DownloadedAssetLibrary.Delete(asset.id));
            Debug.Log("Built-in content passed dependency, playback-data, overwrite, rename, and deletion checks.");
        }

        private static void Reject(Action action)
        {
            try { action(); }
            catch (UnauthorizedAccessException) { return; }
            throw new InvalidOperationException("A built-in content mutation was allowed.");
        }
    }
}
