using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    public sealed class BoneMappings
    {
        [Serializable]
        private struct Assignment
        {
            public string point;
            public string bone;
            public bool calibrated;
            public Quaternion neutralRotation;
        }

        [Serializable]
        private sealed class FileData
        {
            public Assignment[] assignments;
        }

        private static readonly Dictionary<string, string> Body = new(StringComparer.Ordinal)
        {
            ["Body/Hips"] = "Hips",
            ["Body/SpineLower"] = "Spine",
            ["Body/Chest"] = "Chest",
            ["Body/Neck"] = "Neck",
            ["Body/Head"] = "Head",
            ["Body/LeftShoulder"] = "LeftShoulder",
            ["Body/LeftArmUpper"] = "LeftUpperArm",
            ["Body/LeftArmLower"] = "LeftForearm",
            ["Body/LeftHandWrist"] = "LeftWrist",
            ["Body/LeftUpperLeg"] = "LeftThigh",
            ["Body/LeftLowerLeg"] = "LeftShin",
            ["Body/LeftFootAnkle"] = "LeftFoot",
            ["Body/LeftFootBall"] = "LeftToes",
            ["Body/LeftHandThumbMetacarpal"] = "LeftThumbMetacarpal",
            ["Body/LeftHandThumbProximal"] = "LeftThumbProximal",
            ["Body/LeftHandThumbDistal"] = "LeftThumbDistal",
            ["Body/LeftHandIndexProximal"] = "LeftIndexProximal",
            ["Body/LeftHandIndexIntermediate"] = "LeftIndexIntermediate",
            ["Body/LeftHandIndexDistal"] = "LeftIndexDistal",
            ["Body/LeftHandMiddleProximal"] = "LeftMiddleProximal",
            ["Body/LeftHandMiddleIntermediate"] = "LeftMiddleIntermediate",
            ["Body/LeftHandMiddleDistal"] = "LeftMiddleDistal",
            ["Body/LeftHandRingProximal"] = "LeftRingProximal",
            ["Body/LeftHandRingIntermediate"] = "LeftRingIntermediate",
            ["Body/LeftHandRingDistal"] = "LeftRingDistal",
            ["Body/LeftHandLittleProximal"] = "LeftLittleProximal",
            ["Body/LeftHandLittleIntermediate"] = "LeftLittleIntermediate",
            ["Body/LeftHandLittleDistal"] = "LeftLittleDistal",
            ["Body/RightShoulder"] = "RightShoulder",
            ["Body/RightArmUpper"] = "RightUpperArm",
            ["Body/RightArmLower"] = "RightForearm",
            ["Body/RightHandWrist"] = "RightWrist",
            ["Body/RightUpperLeg"] = "RightThigh",
            ["Body/RightLowerLeg"] = "RightShin",
            ["Body/RightFootAnkle"] = "RightFoot",
            ["Body/RightFootBall"] = "RightToes",
            ["Body/RightHandThumbMetacarpal"] = "RightThumbMetacarpal",
            ["Body/RightHandThumbProximal"] = "RightThumbProximal",
            ["Body/RightHandThumbDistal"] = "RightThumbDistal",
            ["Body/RightHandIndexProximal"] = "RightIndexProximal",
            ["Body/RightHandIndexIntermediate"] = "RightIndexIntermediate",
            ["Body/RightHandIndexDistal"] = "RightIndexDistal",
            ["Body/RightHandMiddleProximal"] = "RightMiddleProximal",
            ["Body/RightHandMiddleIntermediate"] = "RightMiddleIntermediate",
            ["Body/RightHandMiddleDistal"] = "RightMiddleDistal",
            ["Body/RightHandRingProximal"] = "RightRingProximal",
            ["Body/RightHandRingIntermediate"] = "RightRingIntermediate",
            ["Body/RightHandRingDistal"] = "RightRingDistal",
            ["Body/RightHandLittleProximal"] = "RightLittleProximal",
            ["Body/RightHandLittleIntermediate"] = "RightLittleIntermediate",
            ["Body/RightHandLittleDistal"] = "RightLittleDistal",
        };

        internal static readonly Dictionary<string, string> Avatar = new(StringComparer.Ordinal)
        {
            ["Hips"] = "Hips",
            ["Spine"] = "Spine",
            ["UpperChest"] = "Chest",
            ["Neck"] = "Neck",
            ["Head"] = "Head",
            ["Left_Shoulder"] = "LeftShoulder",
            ["Left_UpperArm"] = "LeftUpperArm",
            ["Left_LowerArm"] = "LeftForearm",
            ["Left_Hand"] = "LeftWrist",
            ["Left_UpperLeg"] = "LeftThigh",
            ["Left_LowerLeg"] = "LeftShin",
            ["Left_Foot"] = "LeftFoot",
            ["Left_Toes"] = "LeftToes",
            ["Left_ThumbProximal"] = "LeftThumbMetacarpal",
            ["Left_ThumbIntermediate"] = "LeftThumbProximal",
            ["Left_ThumbDistal"] = "LeftThumbDistal",
            ["Left_IndexProximal"] = "LeftIndexProximal",
            ["Left_IndexIntermediate"] = "LeftIndexIntermediate",
            ["Left_IndexDistal"] = "LeftIndexDistal",
            ["Left_MiddleProximal"] = "LeftMiddleProximal",
            ["Left_MiddleIntermediate"] = "LeftMiddleIntermediate",
            ["Left_MiddleDistal"] = "LeftMiddleDistal",
            ["Left_RingProximal"] = "LeftRingProximal",
            ["Left_RingIntermediate"] = "LeftRingIntermediate",
            ["Left_RingDistal"] = "LeftRingDistal",
            ["Left_PinkyProximal"] = "LeftLittleProximal",
            ["Left_PinkyIntermediate"] = "LeftLittleIntermediate",
            ["Left_PinkyDistal"] = "LeftLittleDistal",
            ["Right_Shoulder"] = "RightShoulder",
            ["Right_UpperArm"] = "RightUpperArm",
            ["Right_LowerArm"] = "RightForearm",
            ["Right_Hand"] = "RightWrist",
            ["Right_UpperLeg"] = "RightThigh",
            ["Right_LowerLeg"] = "RightShin",
            ["Right_Foot"] = "RightFoot",
            ["Right_Toes"] = "RightToes",
            ["Right_ThumbProximal"] = "RightThumbMetacarpal",
            ["Right_ThumbIntermediate"] = "RightThumbProximal",
            ["Right_ThumbDistal"] = "RightThumbDistal",
            ["Right_IndexProximal"] = "RightIndexProximal",
            ["Right_IndexIntermediate"] = "RightIndexIntermediate",
            ["Right_IndexDistal"] = "RightIndexDistal",
            ["Right_MiddleProximal"] = "RightMiddleProximal",
            ["Right_MiddleIntermediate"] = "RightMiddleIntermediate",
            ["Right_MiddleDistal"] = "RightMiddleDistal",
            ["Right_RingProximal"] = "RightRingProximal",
            ["Right_RingIntermediate"] = "RightRingIntermediate",
            ["Right_RingDistal"] = "RightRingDistal",
            ["Right_PinkyProximal"] = "RightLittleProximal",
            ["Right_PinkyIntermediate"] = "RightLittleIntermediate",
            ["Right_PinkyDistal"] = "RightLittleDistal",
        };

        private readonly Dictionary<string, Assignment> overrides = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> defaults = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Candidate> mapped = new(StringComparer.Ordinal);
        private readonly string path;
        public int Revision { get; private set; }

        private struct Candidate
        {
            public string point;
            public int priority;
            public Pose pose;
            public bool oriented;
        }

        public BoneMappings(string path, bool load = true)
        {
            this.path = path;
            if (!load || !File.Exists(path))
                return;
            var data = JsonUtility.FromJson<FileData>(File.ReadAllText(path));
            foreach (Assignment assignment in data?.assignments ?? Array.Empty<Assignment>())
            {
                if (!string.IsNullOrEmpty(assignment.point) &&
                    (string.IsNullOrEmpty(assignment.bone) || UniversalSkeleton.TryGet(assignment.bone, out _)) &&
                    (!assignment.calibrated || TrackingPointBuffer.ValidRotation(assignment.neutralRotation)))
                    overrides[assignment.point] = assignment;
            }
        }

        public string AssignedBone(string point)
        {
            if (overrides.TryGetValue(point, out Assignment assignment))
                return string.IsNullOrEmpty(assignment.bone) ? null : assignment.bone;
            if (!defaults.TryGetValue(point, out string bone))
                defaults.Add(point, bone = DefaultBone(point));
            return bone;
        }

        public void Assign(string point, string bone)
        {
            if (string.IsNullOrEmpty(point) || (!string.IsNullOrEmpty(bone) && !UniversalSkeleton.TryGet(bone, out _)))
                throw new ArgumentException("Choose a detected point and a universal bone.");
            overrides[point] = new Assignment { point = point, bone = bone };
            Revision++;
        }

        public void Reset(string point)
        {
            if (overrides.Remove(point))
                Revision++;
        }

        internal bool HasOverride(string point) => point != null && overrides.ContainsKey(point);

        public void Calibrate(string point, Quaternion neutralRotation)
        {
            string bone = AssignedBone(point);
            if (string.IsNullOrEmpty(bone) || !TrackingPointBuffer.ValidRotation(neutralRotation))
                throw new InvalidOperationException("Calibration needs an assigned point with a valid orientation.");
            overrides[point] = new Assignment
                { point = point, bone = bone, calibrated = true, neutralRotation = Quaternion.Normalize(neutralRotation) };
            Revision++;
        }

        public void Save()
        {
            var data = new FileData { assignments = new List<Assignment>(overrides.Values).ToArray() };
            PoseStore.WriteJson(path, data, File.Exists(path));
        }

        public void BuildFrame(TrackingPointBuffer points, TrackingMode mode, TrackedPose frame)
        {
            mapped.Clear();
            frame.Clear(mode);
            foreach (var entry in points.Points)
            {
                string id = AssignedBone(entry.Key);
                if (string.IsNullOrEmpty(id))
                    continue;

                bool custom = overrides.TryGetValue(entry.Key, out Assignment assignment);
                TrackingPoint point = entry.Value;
                // Explicit assignments win. Prefer headset orientation for Head and optical wrists
                // over controller proxies when multiple built-in sources are available.
                bool device = entry.Key.StartsWith("Devices/", StringComparison.Ordinal) ||
                    entry.Key.StartsWith("XR/", StringComparison.Ordinal);
                int priority = custom ? 5 : device && id == "Head" ? 4 :
                    entry.Key.StartsWith("Body/", StringComparison.Ordinal) ? 3 : device ? 1 : 2;
                if (mapped.TryGetValue(id, out Candidate current) &&
                    (priority < current.priority || (priority == current.priority &&
                    string.CompareOrdinal(entry.Key, current.point) >= 0)))
                    continue;

                Quaternion neutral = custom && assignment.calibrated ? assignment.neutralRotation : point.NeutralRotation;
                // Remove the source's rest axes before computing any parent-relative rotations.
                var pose = new Pose(point.Pose.position, point.HasOrientation
                    ? Quaternion.Normalize(point.Pose.rotation * Quaternion.Inverse(neutral)) : Quaternion.identity);
                mapped[id] = new Candidate { point = entry.Key, priority = priority, pose = pose, oriented = point.HasOrientation };
            }

            foreach (SkeletonBone definition in UniversalSkeleton.Bones)
            {
                if (!mapped.TryGetValue(definition.Id, out Candidate bone))
                    continue;
                Quaternion relative = bone.pose.rotation;
                bool localValid = bone.oriented;
                if (definition.ParentId != null)
                {
                    localValid &= mapped.TryGetValue(definition.ParentId, out Candidate parent) && parent.oriented;
                    if (localValid)
                        relative = Quaternion.Inverse(parent.pose.rotation) * relative;
                }
                frame.Add(new TrackedBone(definition.Id, definition.ParentId, bone.pose,
                    relative, localValid, bone.oriented, bone.point, Revision));
            }
        }

        // Also used to translate names in older move files. Helper joints stay unmapped.
        public static string DefaultBone(string point)
        {
            if (UniversalSkeleton.TryGet(point, out _))
                return point;
            if (point == null)
                return null;
            if (Body.TryGetValue(point, out string id))
                return id;
            if (point.StartsWith("Model/", StringComparison.Ordinal))
                return Avatar.TryGetValue(point.Substring(6), out id) ? id : null;

            switch (point)
            {
                case "Devices/Head": case "XR/Head": return "Head";
                case "Devices/LeftController": case "XR/LeftController": return "LeftWrist";
                case "Devices/RightController": case "XR/RightController": return "RightWrist";
            }

            int slash = point.IndexOf('/');
            if (slash < 0)
                return null;
            string source = point.Substring(0, slash);
            string joint = point.Substring(slash + 1);
            if (source is "LeftHand" or "RightHand")
            {
                id = source.Substring(0, source.Length - 4) + joint;
                return UniversalSkeleton.TryGet(id, out _) ? id : null;
            }
            if (source is not ("LeftLegacyHand" or "RightLegacyHand"))
                return null;
            string side = source.StartsWith("Left", StringComparison.Ordinal) ? "Left" : "Right";
            if (joint == "WristRoot")
                return side + "Wrist";
            if (joint.Length < 2)
                return null;
            string finger = joint.Substring(0, joint.Length - 1).Replace("Pinky", "Little");
            string segment = joint[^1] switch
            {
                '1' => finger == "Thumb" ? "Metacarpal" : "Proximal",
                '2' => finger == "Thumb" ? "Proximal" : "Intermediate",
                '3' => "Distal",
                _ => ""
            };
            id = side + finger + segment;
            return UniversalSkeleton.TryGet(id, out _) ? id : null;
        }
    }
}
