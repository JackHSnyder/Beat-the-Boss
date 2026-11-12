using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    public sealed class PoseStore
    {
        private const string Prefix = "pose-";
        private readonly string directory;

        public PoseStore(string directory) => this.directory = directory;

        public string[] List()
        {
            if (!Directory.Exists(directory))
                return Array.Empty<string>();

            var names = new List<string>();
            foreach (string path in Directory.EnumerateFiles(directory, Prefix + "*.json"))
                names.Add(Path.GetFileNameWithoutExtension(path).Substring(Prefix.Length));
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names.ToArray();
        }

        public void Save(SavedPose pose)
        {
            Validate(pose);
            ValidateConstraints(pose.matchSettings);
            Write(PathFor(pose.name), pose, replace: false);
        }

        public void SaveMatchSettings(string name, PoseMatchSettings settings)
        {
            SavedPose pose = Load(name);
            ValidateConstraints(settings);
            pose.matchSettings = settings;
            Write(PathFor(name), pose, replace: true);
        }

        private void Write(string path, SavedPose pose, bool replace)
        {
            Directory.CreateDirectory(directory);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonUtility.ToJson(pose, true));
                if (replace)
                    File.Replace(temporary, path, null);
                else
                    File.Move(temporary, path);
            }
            finally
            {
                File.Delete(temporary);
            }
        }

        public SavedPose Load(string name)
        {
            SavedPose pose = JsonUtility.FromJson<SavedPose>(File.ReadAllText(PathFor(name)));
            Validate(pose);
            return pose;
        }

        private string PathFor(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 64)
                throw new ArgumentException("Use a pose name between 1 and 64 characters.");

            foreach (char character in name)
            {
                if (!char.IsLetterOrDigit(character) && character != ' ' && character != '-' && character != '_')
                    throw new ArgumentException("Use letters, numbers, spaces, hyphens, or underscores in the pose name.");
            }
            return Path.Combine(directory, Prefix + name.Trim() + ".json");
        }

        private static void Validate(SavedPose pose)
        {
            if (pose == null || pose.version != 1 || string.IsNullOrWhiteSpace(pose.name) ||
                pose.joints == null || pose.joints.Length == 0)
                throw new InvalidDataException("The pose file is unsupported or has no usable joints.");

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (PoseJoint joint in pose.joints)
            {
                if (string.IsNullOrEmpty(joint.id) || !ids.Add(joint.id) || !IsRotation(joint.localRotation))
                    throw new InvalidDataException("The pose contains an invalid or duplicate joint.");
            }

            if (pose.avatar?.joints == null)
                return;

            ids.Clear();
            foreach (AvatarJoint joint in pose.avatar.joints)
            {
                if (string.IsNullOrEmpty(joint.name) || !ids.Add(joint.name) || !IsRotation(joint.localRotation))
                    throw new InvalidDataException("The avatar preview contains an invalid or duplicate joint.");
            }
        }

        private static bool IsRotation(Quaternion rotation) =>
            Mathf.Abs(Quaternion.Dot(rotation, rotation) - 1f) < 0.01f;

        private static void ValidateConstraints(PoseMatchSettings settings)
        {
            if (settings?.constraints == null)
                return;
            for (int i = 0; i < settings.constraints.Length; i++)
            {
                if (!settings.constraints[i].IsValid)
                    throw new InvalidDataException($"Complete constraint {i + 1} before saving.");
            }
        }
    }
}
