using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    internal static class BossTimelineStore
    {
        private static readonly Dictionary<string,string> bundledLevels = new(StringComparer.OrdinalIgnoreCase);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetBundledLevels() => bundledLevels.Clear();
        internal static void RegisterBundledLevel(string name,string id)
        {
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(id)) bundledLevels[name]=id;
        }
        [Serializable] internal sealed class Document
        {
            public int version = 1;
            public string name, levelId;
            public string songId;
            public string bossModelId = DownloadedAssetLibrary.DefaultCharacter;
            public float songDelay;
            public int pointsPerHit = 100, targetPoints = 1000;
            public LevelEndBasis endBasis;
            public LevelLoadout.Slot[] loadoutSlots;
            public string idle;
            public float duration = 12;
            public bool loop;
            public float dummyAngle = 270;
            public List<Entry> events = new();
        }
        [Serializable] internal sealed class Entry
        {
            public BossController.ActionKind action;
            public float time, angle, transitionSeconds;
            public BossController.TransitionTiming transitionTiming;
            public float speed = 1;
            public string animation;
        }
        private static string DirectoryPath => Path.Combine(Application.persistentDataPath, "BossTimelines");
        private static string PathFor(string name) => PoseStore.NamedPath(DirectoryPath, "timeline-", name, "timeline", allowParentheses:true);
        internal static string AvailableName(string requested, string replacing = null)
        {
            requested=requested.Trim(); ValidateName(requested);
            var files=List();
            var occupied=new HashSet<string>(files,StringComparer.OrdinalIgnoreCase);
            // Reserve visible bundled rows, but allow reuse of an old title after its
            // level has been saved/renamed. Ordinary loadout titles are not occupied names.
            var savedIds=new HashSet<string>(StringComparer.Ordinal);
            if (bundledLevels.Count>0) foreach(string file in files)
            {
                try { savedIds.Add(Load(file).levelId); }
                catch(Exception error) when(AnimationStore.IsFileError(error)) { }
            }
            foreach(var bundled in bundledLevels)
                if (!savedIds.Contains(bundled.Value)) occupied.Add(bundled.Key);
            if (replacing!=null) occupied.Remove(replacing);
            if (!occupied.Contains(requested)) return requested;
            var match=System.Text.RegularExpressions.Regex.Match(requested,@"^(.*) \([1-9][0-9]*\)$");
            string stem=match.Success?match.Groups[1].Value:requested;
            for (int i=1;;i++)
            {
                string suffix=" ("+i+")";
                string candidate=stem[..Math.Min(stem.Length,64-suffix.Length)].TrimEnd()+suffix;
                if (!occupied.Contains(candidate)) return candidate;
            }
        }
        private static string SelectionKey(string encounter) => "EncounterTimeline.Active." + encounter;
        internal static string ResolveName(string name) => SavedFileNames.Resolve("timeline",name,PathFor);
        internal static void ValidateName(string name) => PathFor(name);
        internal static bool Exists(string name) => File.Exists(PathFor(ResolveName(name)));
        internal static string[] List() => PoseStore.ListFiles(DirectoryPath, "timeline-");

        internal static Document LoadForEncounter(string defaultName, out string name)
        {
            name = defaultName;
            if (EncounterLevelSession.RetryDocument != null)
            {
                var retry = JsonUtility.FromJson<Document>(JsonUtility.ToJson(EncounterLevelSession.RetryDocument));
                name = retry.name; return retry;
            }
            if (EncounterLevelSession.Mode==EncounterLevelSession.Selection.Saved)
            {
                name=ResolveName(EncounterLevelSession.TimelineName);
                return Load(name);
            }
            if (EncounterLevelSession.Mode is EncounterLevelSession.Selection.Bundled or EncounterLevelSession.Selection.New)
                return null;
            if (string.IsNullOrWhiteSpace(defaultName))
                return null;

            string selected = PlayerPrefs.GetString(SelectionKey(defaultName), "");
            if (!string.IsNullOrWhiteSpace(selected) && Exists(selected))
            {
                Document doc = Load(selected);
                name = ResolveName(selected);
                return doc;
            }

            return Exists(defaultName) ? Load(defaultName) : null;
        }

        internal static void SelectForEncounter(string encounter, string name)
        {
            PlayerPrefs.SetString(SelectionKey(encounter), name);
            PlayerPrefs.Save();
        }

        internal static Document Capture(BossController boss)
        {
            var doc = new Document { idle = boss.idle?.savedName, duration = boss.loopDuration, loop = boss.loop, dummyAngle = boss.dummyStartAngle,
                songId=boss.songId,bossModelId=boss.bossModelId,songDelay=boss.songDelay,pointsPerHit=boss.pointsPerHit,targetPoints=boss.targetPoints,endBasis=boss.endBasis };
            foreach (BossController.Cue cue in boss.timeline)
                doc.events.Add(new Entry { action = cue.action, time = cue.time, angle = cue.angle, speed = cue.speed, animation = cue.recording?.savedName, transitionTiming = cue.transitionTiming, transitionSeconds = cue.transitionSeconds });
            return doc;
        }
        internal static void Apply(Document doc, BossController boss)
        {
            Validate(doc);
            boss.songId=doc.songId; boss.songDelay=doc.songDelay;
            boss.pointsPerHit=doc.pointsPerHit; boss.targetPoints=doc.targetPoints;
            boss.endBasis=doc.endBasis;
            boss.SetCharacter(string.IsNullOrEmpty(doc.bossModelId)?DownloadedAssetLibrary.DefaultCharacter:doc.bossModelId);
            boss.idle = new BossController.Recording { savedName = doc.idle };
            boss.loopDuration = doc.duration; boss.loop = doc.endBasis != LevelEndBasis.Timeline; boss.dummyStartAngle = doc.dummyAngle;
            boss.timeline.Clear();
            foreach (Entry e in doc.events)
                boss.timeline.Add(new BossController.Cue { time = e.time, action = e.action, angle = e.angle, speed = e.speed, transitionTiming = e.transitionTiming, transitionSeconds = e.transitionSeconds,
                    recording = new BossController.Recording { savedName = e.animation } });
        }
        internal static Document Load(string name)
        {
            name=ResolveName(name);
            Document doc = JsonUtility.FromJson<Document>(File.ReadAllText(PathFor(name)));
            Validate(doc);
            // Older files gain a deterministic identity, which is preserved on their next save or rename.
            doc.name=name;
            if (string.IsNullOrWhiteSpace(doc.levelId)) doc.levelId="timeline-"+name.ToUpperInvariant();
            return doc;
        }
        internal static void SaveDraft(string name, Document doc, string previousName)
        {
            Validate(doc);
            if (string.IsNullOrWhiteSpace(doc.levelId)) throw new InvalidDataException("The level needs an identity before saving.");
            doc.name=name.Trim();
            SavedFileNames.Write("timeline",previousName,doc.name,PathFor,doc);
        }
        internal static void Save(string name, Document doc)
        {
            Validate(doc);
            string path = PathFor(name);
            PoseStore.WriteJson(path, doc, replace: File.Exists(path));
        }
        private static void Validate(Document doc)
        {
            // Timelines saved before scoring existed omit these fields.
            if (doc != null)
            {
                if ((uint)doc.endBasis > (uint)LevelEndBasis.Points)
                    throw new InvalidDataException("Choose timeline, song, or points as the level completion rule.");
                if (doc.endBasis == LevelEndBasis.Song && string.IsNullOrWhiteSpace(doc.songId))
                    throw new InvalidDataException("Choose a song before saving a song-length level.");
                if (doc.pointsPerHit == 0) doc.pointsPerHit = 100;
                if (doc.targetPoints == 0) doc.targetPoints = 1000;
                if (doc.pointsPerHit < 1 || doc.pointsPerHit > 1000000 || doc.targetPoints < 1 || doc.targetPoints > 1000000000)
                    throw new InvalidDataException("Use 1–1,000,000 points per hit and a score target of 1–1,000,000,000.");
            }
            if (doc == null || doc.version != 1 || doc.events == null || doc.events.Count > 500 ||
                !float.IsFinite(doc.duration) || doc.duration < .1f || doc.duration > 120 || !float.IsFinite(doc.dummyAngle) ||
                !float.IsFinite(doc.songDelay) || doc.songDelay<0 || doc.songDelay>3600 ||
                (!string.IsNullOrEmpty(doc.songId) && !DownloadedAssetLibrary.ValidId(doc.songId)) ||
                (!string.IsNullOrEmpty(doc.bossModelId) && !DownloadedAssetLibrary.ValidId(doc.bossModelId)))
                throw new InvalidDataException("Invalid timeline: use a duration up to 120 seconds and at most 500 events.");
            foreach (Entry e in doc.events)
                if (e == null || (uint)e.action > (uint)BossController.ActionKind.FireLaser || !float.IsFinite(e.time) || e.time < 0 || e.time > 120 ||
                    !float.IsFinite(e.angle) || !float.IsFinite(e.transitionSeconds) || e.transitionSeconds<0 || e.transitionSeconds>120 ||
                    (uint)e.transitionTiming>(uint)BossController.TransitionTiming.FinishAtNode || !float.IsFinite(e.speed) || e.speed < .1f || e.speed > 4)
                    throw new InvalidDataException("The timeline contains an invalid event.");
            if (doc.loadoutSlots!=null)
            {
                var ids=new HashSet<string>(StringComparer.Ordinal);
                foreach (var slot in doc.loadoutSlots)
                    if (slot==null || string.IsNullOrWhiteSpace(slot.Id) || string.IsNullOrWhiteSpace(slot.Title) ||
                        !ids.Add(slot.Id) || (uint)slot.Action>(uint)MoveSlot.None)
                        throw new InvalidDataException("The level contains invalid loadout slots.");
            }
        }
    }
}
