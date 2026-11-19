using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    // Resources are included in every build and can be read directly inside a Quest APK.
    // Persistent files with the same name never override or hide the packaged original.
    internal static class BuiltInContent
    {
        private static Dictionary<string, string> documents;
        private static Dictionary<string, string> Documents
        {
            get
            {
                if (documents == null)
                {
                    documents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string folder in new[] { "Poses", "Moves", "Animations", "BossTimelines" })
                        foreach (var asset in Resources.LoadAll<TextAsset>("BuiltInContent/" + folder))
                            documents.Add(folder + "/" + asset.name + ".json", asset.text);
                }
                return documents;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void Reset() => documents = null;

        private static string Key(string path)
        {
            string root = Path.GetFullPath(Application.persistentDataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(path);
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full.Substring(root.Length).Replace('\\', '/') : "";
        }

        internal static bool IsProtected(string folder, string prefix, string name) =>
            name != null && Documents.ContainsKey(folder + "/" + prefix + name.Trim() + ".json");
        internal static bool IsProtectedPath(string path) => Documents.ContainsKey(Key(path));
        internal static bool Exists(string path) => IsProtectedPath(path) || File.Exists(path);
        internal static string ReadAllText(string path) => Documents.TryGetValue(Key(path), out string json) ? json : File.ReadAllText(path);
        internal static void RequireWritable(string path)
        {
            if (IsProtectedPath(path)) throw new UnauthorizedAccessException("Built-in content cannot be edited, renamed, overwritten, or deleted.");
        }
        internal static void Delete(string path) { RequireWritable(path); File.Delete(path); }

        internal static string[] Names(string directory, string prefix, IEnumerable<string> saved)
        {
            string folder = Key(Path.Combine(directory, "placeholder.json"));
            folder = folder.Substring(0, Math.Max(0, folder.LastIndexOf('/') + 1));
            if (folder.Length == 0) return saved.ToArray();
            return saved.Concat(Documents.Keys.Where(k => k.StartsWith(folder + prefix, StringComparison.OrdinalIgnoreCase))
                .Select(k => Path.GetFileNameWithoutExtension(k).Substring(prefix.Length)))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }
}
