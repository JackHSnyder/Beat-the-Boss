using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    [DefaultExecutionOrder(300)]
    public sealed class BossTimelineWorkbench : MonoBehaviour
    {
        public BossController boss;
        public string saveName = "Boss Timeline";
        public float snap = .1f;
        public float Cursor { get; private set; }
        public bool Playing { get; private set; }
        public bool Dirty { get; private set; }
        public string Status { get; private set; } = "Ready";
        public BossController.Cue Selected { get; private set; }
        public event Action Changed;
        private readonly Stack<string> undo = new(), redo = new();
        private string savedState;
        private string sourceName, levelId;
        private LevelLoadout.Slot[] loadoutSlots;
        public bool IsNew => sourceName==null;
        // OnEnable also runs after a script reload, when the nonserialized preview flag resets.
        private void OnEnable() { if (boss != null) { boss.previewMode = true; boss.playOnStart = false; } }
        private void Start()
        {
            if (EncounterLevelSession.Mode==EncounterLevelSession.Selection.New) { New(); return; }
            try
            {
                string defaultName = string.IsNullOrWhiteSpace(boss.timelineName) ? saveName : boss.timelineName;
                var doc = BossTimelineStore.LoadForEncounter(defaultName, out string name);
                if (doc != null) { ApplyLoaded(doc,name); return; }
                saveName = EncounterLevelSession.BundledTitle ?? name;
            }
            catch (Exception e) when (AnimationStore.IsFileError(e)) { Status = e.Message; }
            levelId=EncounterLevelSession.Mode==EncounterLevelSession.Selection.Bundled && EncounterLevelSession.Template!=null
                ? EncounterLevelSession.Template.Id : Guid.NewGuid().ToString("N");
            loadoutSlots=LevelLoadout.CopySlots(EncounterLevelSession.Template?.Slots);
            boss.RebuildTimeline(); savedState=null; Dirty=true; Changed?.Invoke();
        }
        private void Update()
        {
            if (!Playing) return;
            Playing=boss.AdvancePreview(Time.unscaledDeltaTime);
            Cursor=boss.TimePosition;
        }
        private BossTimelineStore.Document Capture()
        {
            var doc=BossTimelineStore.Capture(boss);
            doc.name=saveName; doc.levelId=levelId; doc.loadoutSlots=loadoutSlots;
            return doc;
        }
        private string Serialize() => JsonUtility.ToJson(Capture());
        private void BeforeEdit() { Playing = false; undo.Push(Serialize()); redo.Clear(); }
        private void Edited()
        {
            boss.RebuildTimeline(); Cursor = Mathf.Min(Cursor, boss.TimelineDuration); boss.Seek(Cursor);
            Dirty = IsNew || Serialize() != savedState;
            Status=Dirty?(IsNew?"New unsaved level":"Timeline edited"):"No unsaved changes";
            Changed?.Invoke();
        }
        public float Snapped(float time) => Mathf.Clamp(snap > 0 ? Mathf.Round(time / snap) * snap : time, 0, 120);
        public void Seek(float time) { Playing = false; Cursor = Mathf.Clamp(time, 0, boss.TimelineDuration); boss.Seek(Cursor); }
        public void TogglePlay() { if (Cursor >= boss.TimelineDuration) Seek(0); Playing = !Playing; }
        public void Stop() => Seek(0);
        public void Select(BossController.Cue cue) { Selected = cue; Changed?.Invoke(); }
        public void Add(BossController.ActionKind kind, string animation = null)
        {
            BeforeEdit(); Selected = new BossController.Cue { time = Snapped(Cursor), action = kind, recording = new BossController.Recording { savedName = animation } };
            boss.timeline.Add(Selected); Edited();
        }
        public void MoveSelected(float time)
        { if (Selected == null) return; BeforeEdit(); Selected.time = Snapped(time); Edited(); }
        public void SetSelectedAction(BossController.ActionKind action)
        {
            if (Selected == null || Selected.action == action)
                return;
            BeforeEdit();
            Selected.action = action;
            Edited();
        }
        public void SetTransitionTiming(BossController.TransitionTiming timing)
        {
            if (Selected==null || !BossController.HasTransition(Selected.action) || Selected.transitionTiming==timing) return;
            BeforeEdit(); Selected.transitionTiming=timing; Edited();
        }
        public void SetTransitionSeconds(float seconds)
        {
            if (Selected==null || !BossController.HasTransition(Selected.action)) return;
            BeforeEdit(); Selected.transitionSeconds=Mathf.Clamp(seconds,.01f,120); Edited();
        }
        public void SetSelectedAnimation(string name)
        {
            if (Selected == null || Selected.action is not (BossController.ActionKind.PlayAnimation or BossController.ActionKind.SetIdle))
                return;
            BeforeEdit();
            Selected.recording ??= new BossController.Recording();
            Selected.recording.savedName = name;
            Selected.recording.bundledJson = null;
            Edited();
        }
        public void AdjustAngle(float change)
        { if (Selected == null || Selected.action is not (BossController.ActionKind.FaceAngle or BossController.ActionKind.DummyAngle)) return; BeforeEdit(); Selected.angle = Mathf.Repeat(Selected.angle + change, 360); Edited(); }
        public void SetSpeed(float speed)
        { if (Selected == null || Selected.action != BossController.ActionKind.PlayAnimation) return; BeforeEdit(); Selected.speed = Mathf.Clamp(speed, .1f, 4); Edited(); }
        public void DeleteSelected()
        { if (Selected == null) return; BeforeEdit(); boss.timeline.Remove(Selected); Selected = null; Edited(); }
        public void DuplicateSelected()
        {
            if (Selected == null) return;
            BeforeEdit();
            Selected = new BossController.Cue { action = Selected.action, time = Snapped(Selected.time + Mathf.Max(.1f, boss.ClipDuration(Selected))),
                angle = Selected.angle, speed = Selected.speed, transitionTiming = Selected.transitionTiming, transitionSeconds = Selected.transitionSeconds, recording = new BossController.Recording { savedName = Selected.recording.savedName, bundledJson = Selected.recording.bundledJson } };
            boss.timeline.Add(Selected); Edited();
        }
        public void SetIdle(string name) { BeforeEdit(); boss.idle.savedName = name; boss.idle.bundledJson = null; Edited(); }
        public void SetSong(string id) { BeforeEdit(); boss.songId=id; Edited(); }
        public void SetSongDelay(float seconds) { BeforeEdit(); boss.songDelay=Mathf.Clamp(seconds,0,3600); Edited(); }
        public void SetBossModel(string id)
        {
            try
            {
                DownloadedAssetLibrary.ResolveCharacter(id);
                BeforeEdit(); boss.SetCharacter(id); Edited();
            }
            catch (Exception e) when (AnimationStore.IsFileError(e)) { Status=e.Message; Changed?.Invoke(); }
        }
        public void SetDuration(float value) { BeforeEdit(); boss.loopDuration = Mathf.Clamp(value, 1, 120); Edited(); }
        public void ToggleLoop() { BeforeEdit(); boss.loop = !boss.loop; Edited(); }
        public void SetDummyStart(float angle) { BeforeEdit(); boss.dummyStartAngle = Mathf.Repeat(angle, 360); Edited(); }
        public void ReloadAnimations() { Edited(); Status = "Animation library refreshed."; }
        public bool SetName(string value)
        {
            try
            {
                value=(value??"").Trim(); BossTimelineStore.ValidateName(value);
                if (!string.Equals(value,sourceName,StringComparison.OrdinalIgnoreCase) &&
                    BossTimelineStore.List().Any(n=>string.Equals(n,value,StringComparison.OrdinalIgnoreCase)))
                    throw new System.IO.IOException("A timeline already uses that name. Choose another name.");
                if (value!=saveName) { BeforeEdit(); saveName=value; Edited(); }
                return true;
            }
            catch (Exception e) when (AnimationStore.IsFileError(e)) { Status=e.Message; Changed?.Invoke(); return false; }
        }
        public void Save() => TrySave();
        public bool TrySave()
        {
            try
            {
                BossTimelineStore.SaveDraft(saveName, Capture(), sourceName);
                sourceName=saveName;
                EncounterLevelSession.Saved(saveName,EncounterLevelSession.Template);
                bool linkedToEncounter = !string.IsNullOrWhiteSpace(boss.timelineName);
                if (linkedToEncounter)
                    BossTimelineStore.SelectForEncounter(boss.timelineName, saveName);
                savedState = Serialize();
                Dirty = false;
                Status = "Saved level: " + saveName;
            }
            catch (Exception e) when (AnimationStore.IsFileError(e) || e is PlayerPrefsException)
            { Status = e.Message; Changed?.Invoke(); return false; }
            Changed?.Invoke();
            return true;
        }
        private void ApplyLoaded(BossTimelineStore.Document doc,string name)
        {
            BossTimelineStore.Apply(doc,boss);
            saveName=sourceName=BossTimelineStore.ResolveName(name); levelId=doc.levelId;
            loadoutSlots=LevelLoadout.CopySlots(doc.loadoutSlots ?? (IEnumerable<LevelLoadout.Slot>)EncounterLevelSession.Template?.Slots);
            undo.Clear(); redo.Clear(); Selected=null; Cursor=0; Playing=false;
            boss.RebuildTimeline(); savedState=Serialize(); Dirty=false;
            EncounterLevelSession.Saved(saveName,EncounterLevelSession.Template);
            Status="Loaded "+saveName; Changed?.Invoke();
        }
        public void Load(string name)
        {
            try
            {
                var doc = BossTimelineStore.Load(name); ApplyLoaded(doc,name);
            }
            catch (Exception e) when (AnimationStore.IsFileError(e)) { Status = e.Message; }
            Changed?.Invoke();
        }
        public void New()
        {
            undo.Clear(); redo.Clear(); int i = 1; while (BossTimelineStore.Exists("New Level " + i)) i++;
            saveName = "New Level " + i; sourceName=savedState=null; levelId=Guid.NewGuid().ToString("N");
            loadoutSlots=LevelLoadout.CopySlots(EncounterLevelSession.Template?.Slots);
            boss.timeline.Clear(); boss.idle=new BossController.Recording(); boss.loop=true; boss.loopDuration=12; boss.dummyStartAngle=270;
            boss.songId=null; boss.songDelay=0; boss.SetCharacter(DownloadedAssetLibrary.DefaultCharacter);
            EncounterLevelSession.New(EncounterLevelSession.Template);
            Selected = null; Cursor = 0; Status="New unsaved level"; Edited();
        }
        public void Undo(bool forward = false)
        {
            var source = forward ? redo : undo; var destination = forward ? undo : redo;
            if (source.Count == 0) return;
            Playing = false; destination.Push(Serialize());
            var doc=JsonUtility.FromJson<BossTimelineStore.Document>(source.Pop());
            BossTimelineStore.Apply(doc,boss); saveName=doc.name; levelId=doc.levelId; loadoutSlots=doc.loadoutSlots;
            Selected = null; Edited();
        }
        public void StepEvent(int direction)
        {
            float target = direction < 0 ? 0 : boss.TimelineDuration;
            foreach (var cue in boss.timeline)
            {
                if (direction < 0 && cue.time < Cursor - .001f) target = Mathf.Max(target, cue.time);
                if (direction > 0 && cue.time > Cursor + .001f) target = Mathf.Min(target, cue.time);
            }
            Seek(target);
        }
    }
}
