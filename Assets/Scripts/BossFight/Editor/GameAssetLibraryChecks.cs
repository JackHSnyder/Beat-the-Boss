using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    public static class GameAssetLibraryChecks
    {
        public static string RunDataChecks()
        {
            DownloadedAssetLibrary.Refresh();
            Require(DownloadedAssetLibrary.Find("song-chill")?.clip!=null,"chill was not registered.");
            Require(DownloadedAssetLibrary.Find("song-ethreal")?.clip!=null,"ethreal was not registered.");
            DownloadedAssetLibrary.ValidateCharacter(DownloadedAssetLibrary.ResolveCharacter(DownloadedAssetLibrary.DefaultCharacter));
            bool rejected=false;
            try { DownloadedAssetLibrary.Delete(DownloadedAssetLibrary.DefaultCharacter); }
            catch (InvalidOperationException) { rejected=true; }
            Require(rejected && DownloadedAssetLibrary.Find(DownloadedAssetLibrary.DefaultCharacter)!=null,"Default character could be deleted.");
            string token=Guid.NewGuid().ToString("N");
            string first=Path.Combine(DownloadedAssetLibrary.SongsFolder,"check-"+token+".wav");
            string renamed=Path.Combine(DownloadedAssetLibrary.SongsFolder,"renamed-"+token+".wav");
            string timeline="Asset check "+token;
            try
            {
                // A short PCM silence file also exercises asynchronous runtime decoding.
                using (var writer=new BinaryWriter(File.Create(first)))
                {
                    int samples=4410;
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36+samples*2);
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                    writer.Write((short)1); writer.Write((short)1); writer.Write(44100); writer.Write(88200);
                    writer.Write((short)2); writer.Write((short)16); writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(samples*2);
                    for (int i=0;i<samples;i++) writer.Write((short)0);
                }
                DownloadedAssetLibrary.Refresh();
                var song=DownloadedAssetLibrary.All.Single(a=>a.path==first); string id=song.id;
                File.Move(first,renamed); DownloadedAssetLibrary.Refresh();
                Require(DownloadedAssetLibrary.Find(id)?.path==renamed,"Renaming a song broke its content ID.");
                var doc=new BossTimelineStore.Document {name=timeline,levelId=token,songId=id,songDelay=2.5f,bossModelId=DownloadedAssetLibrary.DefaultCharacter};
                BossTimelineStore.SaveDraft(timeline,doc,null);
                var saved=BossTimelineStore.Load(timeline);
                Require(saved.songId==id && saved.songDelay==2.5f && saved.bossModelId==DownloadedAssetLibrary.DefaultCharacter,"Level asset references did not round trip.");
                Require(!File.ReadAllText(Path.Combine(Application.persistentDataPath,"BossTimelines","timeline-"+timeline+".json")).Contains("RIFF"),"Level contains media bytes.");
                DownloadedAssetLibrary.Delete(id);
                Require(!File.Exists(renamed) && DownloadedAssetLibrary.Find(id)==null,"Deleting a downloaded song failed.");
                Require(BossTimelineStore.Load(timeline).songId==id,"Deleting an asset rewrote a level reference.");
                var legacy=JsonUtility.FromJson<BossTimelineStore.Document>("{\"version\":1,\"duration\":12,\"events\":[]}");
                Require(legacy.songDelay==0 && string.IsNullOrEmpty(legacy.songId),"Legacy levels gained unwanted music.");
                return "PASS: bundled catalog, rig compatibility, protected character, raw-song refresh, stable IDs after rename, level references and delay, deletion, missing references, legacy defaults.";
            }
            finally
            {
                File.Delete(first); File.Delete(renamed);
                File.Delete(Path.Combine(Application.persistentDataPath,"BossTimelines","timeline-"+timeline+".json"));
                DownloadedAssetLibrary.Refresh();
            }
        }
        private static void Require(bool value,string error) { if (!value) throw new Exception(error); }
    }
}
