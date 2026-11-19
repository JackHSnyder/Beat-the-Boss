using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    internal static class BossLevelFileChecks
    {
        [MenuItem("Tools/Beat the Boss/Check level files and renames")]
        internal static void Run()
        {
            string prefix="Codex check "+Guid.NewGuid().ToString("N");
            string first=prefix+" A",renamed=prefix+" B",legacy=prefix+" C";
            string id=Guid.NewGuid().ToString("N");
            var mode=EncounterLevelSession.Mode; string selected=EncounterLevelSession.TimelineName, bundled=EncounterLevelSession.BundledTitle;
            var template=EncounterLevelSession.Template;
            LevelLoadout before=null,after=null;
            string assignment="LevelMoveLoadout."+Uri.EscapeDataString(id)+".attack";
            try
            {
                var slot=JsonUtility.FromJson<LevelLoadout.Slot>("{\"id\":\"attack\",\"title\":\"Attack\",\"action\":0}");
                var doc=new BossTimelineStore.Document {name=first,levelId=id,loop=true,loadoutSlots=new[]{slot}};
                doc.events.Add(new BossTimelineStore.Entry {time=1,action=BossController.ActionKind.FaceAngle,angle=90,
                    transitionTiming=BossController.TransitionTiming.FinishAtNode,transitionSeconds=.4f});
                BossTimelineStore.SaveDraft(first,doc,null);
                var loaded=BossTimelineStore.Load(first);
                Require(loaded.levelId==id && loaded.name==first && loaded.events[0].angle==90,"Level metadata or timeline did not persist.");
                before=LevelLoadout.FromTimeline(loaded,null);
                MoveLoadout.Assign(before,before.Slots[0],"Check move");
                BossTimelineStore.SaveDraft(renamed,loaded,first);
                Require(!File.Exists(PathFor(first)) && File.Exists(PathFor(renamed)),"Rename left two level files.");
                var moved=BossTimelineStore.Load(first);
                Require(moved.name==renamed && moved.levelId==id,"Rename lost identity or old references.");
                after=LevelLoadout.FromTimeline(moved,null);
                Require(MoveLoadout.Get(after,after.Slots[0])=="Check move","Rename lost level loadout assignments.");
                bool rejected=false;
                try { BossTimelineStore.SaveDraft(renamed,new BossTimelineStore.Document {levelId="collision"},null); }
                catch(IOException) { rejected=true; }
                Require(rejected && BossTimelineStore.Load(renamed).levelId==id,"A duplicate name overwrote another level.");
                EncounterLevelSession.Saved(renamed,after);
                var gameplay=BossTimelineStore.LoadForEncounter("Unrelated default",out string gameplayName);
                Require(gameplayName==renamed && gameplay.events[0].transitionSeconds==.4f,"Gameplay did not select the level's exact timeline.");
                BossTimelineStore.Save(legacy,new BossTimelineStore.Document());
                var old=BossTimelineStore.Load(legacy);
                Require(!string.IsNullOrEmpty(old.levelId) && old.name==legacy,"Old timeline files were not compatible.");
                File.WriteAllText("Temp/BossLevelFileChecks.txt","PASS: level metadata, timeline and slots; stable identity and assignments after rename; old-name resolution; collision protection; exact gameplay selection; legacy compatibility.");
                Debug.Log("Level file and rename checks passed.");
            }
            finally
            {
                foreach(string name in new[]{first,renamed,legacy})
                {
                    File.Delete(PathFor(name));
                    PlayerPrefs.DeleteKey("SavedFileRename.timeline."+Uri.EscapeDataString(name.ToUpperInvariant()));
                }
                PlayerPrefs.DeleteKey(assignment); PlayerPrefs.Save();
                if (mode==EncounterLevelSession.Selection.New) EncounterLevelSession.New(template);
                else if (mode==EncounterLevelSession.Selection.Saved) EncounterLevelSession.Saved(selected,template);
                else if (mode==EncounterLevelSession.Selection.Bundled) EncounterLevelSession.Bundled(bundled,template);
                else typeof(EncounterLevelSession).GetMethod("Reset",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Invoke(null,null);
                if(before!=null)UnityEngine.Object.DestroyImmediate(before);
                if(after!=null)UnityEngine.Object.DestroyImmediate(after);
            }
        }
        private static string PathFor(string name) => Path.Combine(Application.persistentDataPath,"BossTimelines","timeline-"+name+".json");
        private static void Require(bool condition,string message) { if(!condition)throw new Exception(message); }
    }
}
