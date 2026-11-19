using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    // Keep existing move, timeline, and loadout references working after a device file is renamed.
    internal static class SavedFileNames
    {
        private static string Key(string kind, string name) => "SavedFileRename." + kind + "." + Uri.EscapeDataString(name.ToUpperInvariant());
        internal static string Resolve(string kind, string name, Func<string, string> path)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (!BuiltInContent.Exists(path(name)) && visited.Add(name))
            {
                string next = PlayerPrefs.GetString(Key(kind, name), "");
                if (string.IsNullOrEmpty(next)) break;
                name = next;
            }
            return name;
        }
        internal static void Write(string kind, string previous, string name, Func<string,string> path, object value)
        {
            string destination = path(name);
            BuiltInContent.RequireWritable(destination);
            if (previous != null) BuiltInContent.RequireWritable(path(previous));
            if (previous == null) { PoseStore.WriteJson(destination, value, replace: false); return; }
            string source = path(previous);
            if (!File.Exists(source)) throw new FileNotFoundException("The original saved file no longer exists.", source);
            if (string.Equals(source, destination, StringComparison.Ordinal))
            { PoseStore.WriteJson(source, value, replace: true); return; }
            if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
            {
                if ((Application.platform == RuntimePlatform.Android || Application.platform == RuntimePlatform.LinuxPlayer ||
                    Application.platform == RuntimePlatform.LinuxEditor) && File.Exists(destination))
                    throw new IOException("A saved file already uses that name.");
                PoseStore.WriteJson(source, value, replace: true);
                string temporary = source + "." + Guid.NewGuid().ToString("N") + ".rename";
                File.Move(source, temporary);
                try { File.Move(temporary, destination); }
                finally { if (File.Exists(temporary)) File.Move(temporary, source); }
                PlayerPrefs.SetString(Key(kind, previous), name); PlayerPrefs.Save();
                return;
            }
            PoseStore.WriteJson(destination, value, replace: false);
            try { File.Delete(source); }
            catch { File.Delete(destination); throw; }
            PlayerPrefs.SetString(Key(kind, previous), name); PlayerPrefs.Save();
        }
    }
}
