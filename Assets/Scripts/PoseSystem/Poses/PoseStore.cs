// Reads, validates, and writes saved pose JSON, including matching settings and legacy pose replacements.
// PoseWorkbench uses it for recording and editing. MoveWorkbench and MoveDetection use it to load pose steps.

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

        public string[] List() => ListFiles(directory, Prefix);

        internal static string[] ListFiles(string directory, string prefix)
        {
            if (!Directory.Exists(directory))
                return Array.Empty<string>();

            var names = new List<string>();
            foreach (string path in Directory.EnumerateFiles(directory, prefix + "*.json"))
                names.Add(Path.GetFileNameWithoutExtension(path).Substring(prefix.Length));
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names.ToArray();
        }

        public void Save(SavedPose pose)
        {
            Validate(pose);
            ValidateConstraints(pose.matchSettings);
            WriteJson(PathFor(pose.name), pose, replace: false);
        }

        public void SaveDraft(SavedPose pose, string previousName)
        {
            Validate(pose); ValidateConstraints(pose.matchSettings);
            SavedFileNames.Write("pose", previousName, pose.name, PathFor, pose);
        }

        public void Delete(string name) => File.Delete(PathFor(name));

        public bool IsLegacy(string name)
        {
            var header = JsonUtility.FromJson<SavedPose>(File.ReadAllText(PathFor(name)));
            return header?.version == 1;
        }

        public void ReplaceLegacy(SavedPose pose, bool useDraftSettings = false)
        {
            Validate(pose);
            string path = PathFor(pose.name);
            if (!IsLegacy(pose.name))
                throw new InvalidDataException("Only an old-format pose can be replaced by this capture.");
            if (!useDraftSettings) PrepareLegacyReplacement(pose);
            WriteJson(path, pose, replace: true, backup: path + ".legacy");
        }

        public void PrepareLegacyReplacement(SavedPose pose)
        {
            SavedPose previous = JsonUtility.FromJson<SavedPose>(File.ReadAllText(PathFor(pose.name)));
            if (previous.matchSettings != null)
            {
                // Category IDs and angle tolerances carry over. Old direction axes were source-specific.
                var bones = new Dictionary<string, PoseBoneSetting>(StringComparer.Ordinal);
                foreach (PoseBoneSetting old in previous.matchSettings.bones ?? Array.Empty<PoseBoneSetting>())
                {
                    PoseBoneSetting setting = old;
                    setting.id = BoneMappings.DefaultBone(old.id);
                    if (setting.id != null)
                        bones[setting.id] = setting;
                }
                var constraints = new List<PoseConstraint>();
                foreach (PoseConstraint old in previous.matchSettings.constraints ?? Array.Empty<PoseConstraint>())
                {
                    if (old.type != PoseConstraintType.Position)
                        continue;
                    PoseConstraint rule = old;
                    rule.boneId = BoneMappings.DefaultBone(old.boneId);
                    rule.targetBoneId = old.UsesBone ? BoneMappings.DefaultBone(old.targetBoneId) : null;
                    if ((!old.UsesBone || rule.UsesBone) && rule.IsValid)
                        constraints.Add(rule);
                }
                pose.matchSettings = new PoseMatchSettings
                {
                    categories = previous.matchSettings.categories,
                    bones = new List<PoseBoneSetting>(bones.Values).ToArray(),
                    constraints = constraints.ToArray()
                };
            }
        }

        public void SaveMatchSettings(string name, PoseMatchSettings settings)
        {
            SavedPose pose = Load(name);
            ValidateConstraints(settings);
            pose.matchSettings = settings;
            WriteJson(PathFor(name), pose, replace: true);
        }

        // Write beside the destination so the final move or replacement can stay on the same filesystem.
        internal static void WriteJson(string path, object value, bool replace, string backup = null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonUtility.ToJson(value, true));
                if (replace)
                    File.Replace(temporary, path, backup);
                else
                    File.Move(temporary, path);
            }
            finally
            {
                File.Delete(temporary);
            }
        }

        public SavedPose Load(string name) => ReadJson(File.ReadAllText(PathFor(SavedFileNames.Resolve("pose", name, PathFor))));

        internal static SavedPose ReadJson(string json)
        {
            SavedPose pose = JsonUtility.FromJson<SavedPose>(json);
            Validate(pose);
            return pose;
        }

        private string PathFor(string name) => NamedPath(directory, Prefix, name, "pose");

        internal static string NamedPath(string directory, string prefix, string name, string kind, bool allowParentheses = false)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 64)
                throw new ArgumentException($"Use a {kind} name between 1 and 64 characters.");

            foreach (char character in name)
            {
                if (!char.IsLetterOrDigit(character) && character != ' ' && character != '-' && character != '_' &&
                    !(allowParentheses && (character == '(' || character == ')')))
                    throw new ArgumentException($"Use letters, numbers, spaces, hyphens, underscores{(allowParentheses ? ", or parentheses" : "")} in the {kind} name.");
            }
            return Path.Combine(directory, prefix + name.Trim() + ".json");
        }

        private static void Validate(SavedPose pose)
        {
            if (pose?.version == 1)
                throw new InvalidDataException("This pose uses the old tracking skeleton. Capture it again with the universal skeleton.");
            if (pose == null || pose.version != 2 || string.IsNullOrWhiteSpace(pose.name) ||
                pose.joints == null || pose.joints.Length == 0)
                throw new InvalidDataException("The pose file is unsupported or has no usable joints.");

            // Joint IDs must be unique because matching looks up each joint by its saved ID.
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (PoseJoint joint in pose.joints)
            {
                if (!UniversalSkeleton.TryGet(joint.id, out _) || !ids.Add(joint.id) || !IsRotation(joint.localRotation) ||
                    (joint.hasPosition && (!float.IsFinite(joint.position.x) || !float.IsFinite(joint.position.y) || !float.IsFinite(joint.position.z))))
                    throw new InvalidDataException("The pose contains an invalid or duplicate joint.");
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
