using System;
using System.IO;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    internal static class AnimationStore
    {
        private const string Prefix = "animation-";
        private static string DirectoryPath => Path.Combine(Application.persistentDataPath, "Animations");

        [Serializable]
        private sealed class AnimationFile
        {
            public int version = 1;
            public string name;
            public MoveAnimation animation;
        }

        internal static void Delete(string name) => BuiltInContent.Delete(PathFor(name));

        internal static string[] List() => PoseStore.ListFiles(DirectoryPath, Prefix);
        internal static string PathFor(string name) => PoseStore.NamedPath(DirectoryPath, Prefix, name, "animation");

        internal static bool Exists(string name) => BuiltInContent.Exists(PathFor(SavedFileNames.Resolve("animation", name, PathFor)));
        internal static void SaveDraft(string name, MoveAnimation animation, string previousName)
        {
            ValidateTake(animation);
            SavedFileNames.Write("animation", previousName, name, PathFor,
                new AnimationFile { name = name, animation = animation });
        }

        internal static MoveAnimation Load(string name)
        {
            return ReadJson(BuiltInContent.ReadAllText(PathFor(SavedFileNames.Resolve("animation", name, PathFor))));
        }

        internal static MoveAnimation ReadJson(string json)
        {
            var file = JsonUtility.FromJson<AnimationFile>(json);
            if (file == null || file.version != 1 || string.IsNullOrWhiteSpace(file.name))
                throw new InvalidDataException("The animation file is empty or unsupported.");
            ValidateTake(file.animation);
            return file.animation;
        }

        internal static void Save(string name, MoveAnimation animation, bool replace)
        {
            string path = PathFor(name);
            ValidateTake(animation);
            PoseStore.WriteJson(path, new AnimationFile { name = name, animation = animation }, replace);
        }

        private static void ValidateTake(MoveAnimation animation)
        {
            if (animation?.HasFrames != true)
                throw new InvalidDataException("Record at least two tracked frames before saving an animation.");
            animation.Validate();
            if (!animation.HasEnabledSamples())
                throw new InvalidDataException("Include a bone tracked during the selected playback range.");
        }

        internal static bool IsFileError(Exception error) =>
            error is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException;
    }
}
