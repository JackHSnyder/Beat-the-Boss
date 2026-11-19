// Locates saved move files and reads move JSON, updating older bone IDs as needed.
// MoveWorkbench uses it to browse and open files. MoveDetection uses it to load device saves and bundled assets.

using System.IO;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    internal static class MoveStore
    {
        private const string Prefix = "move-";
        private static string DirectoryPath => Path.Combine(Application.persistentDataPath, "Moves");

        internal static string ResolveName(string name) => SavedFileNames.Resolve("move", name, PathFor);
        internal static void Delete(string name) => BuiltInContent.Delete(PathFor(name));

        public static string[] List() => PoseStore.ListFiles(DirectoryPath, Prefix);
        public static string PathFor(string name) => PoseStore.NamedPath(DirectoryPath, Prefix, name, "move");

        public static MoveDefinition Load(string name, out bool legacy) =>
            ReadJson(BuiltInContent.ReadAllText(PathFor(ResolveName(name))), out legacy);

        internal static MoveAnimation LoadAnimation(MoveDefinition move) =>
            string.IsNullOrWhiteSpace(move.animationName) ? move.animation : AnimationStore.Load(move.animationName);

        public static MoveDefinition ReadJson(string json, out bool legacy)
        {
            MoveDefinition definition = JsonUtility.FromJson<MoveDefinition>(json);
            if (definition == null || definition.version is not (1 or 2) || definition.steps == null ||
                definition.steps.Exists(step => step == null || step.actions == null))
                throw new InvalidDataException("The move file is empty or unsupported.");

            legacy = definition.version == 1;
            definition.UpdateLegacyBoneIds();
            definition.animation?.Validate();
            return definition;
        }
    }
}
