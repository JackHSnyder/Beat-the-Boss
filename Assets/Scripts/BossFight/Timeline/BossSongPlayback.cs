using System;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    // Uses elapsed encounter time, independently of the looping event cursor.
    [DefaultExecutionOrder(350)]
    internal sealed class BossSongPlayback : MonoBehaviour
    {
        private BossController boss;
        private BossTimelineWorkbench editor;
        private AudioSource source;
        private string loadedId;
        private int requestVersion;
        private bool requested;
        internal float ClipLength => source != null && source.clip != null ? source.clip.length : 0;
        internal string LoadError { get; private set; }
        private void OnEnable() => DownloadedAssetLibrary.Changed+=Invalidate;
        private void Invalidate() => requested=false;
        private void Awake()
        {
            boss=GetComponent<BossController>();
            source=gameObject.AddComponent<AudioSource>();
            source.playOnAwake=false; source.loop=false; source.spatialBlend=0;
        }
        private void Update()
        {
            if (!requested || loadedId!=boss.songId)
            {
                StopAllCoroutines(); source.Stop(); source.clip=null;
                loadedId=boss.songId; requested=true; int version=++requestVersion;
                LoadError=null;
                if (!string.IsNullOrEmpty(loadedId))
                    StartCoroutine(DownloadedAssetLibrary.LoadSong(loadedId,(clip,error)=>
                    {
                        if (version!=requestVersion) return;
                        source.clip=clip;
                        LoadError=error;
                        if (error!=null) Debug.LogWarning("Encounter song unavailable: "+error,boss);
                    }));
            }
            if (editor==null && boss.previewMode) editor=FindAnyObjectByType<BossTimelineWorkbench>();
            bool playing=boss.previewMode ? editor!=null && editor.Playing : boss.IsPlaying;
            double position=boss.ElapsedTime-boss.songDelay;
            source.loop=boss.endBasis!=LevelEndBasis.Song;
            if (!playing || source.clip==null || position<0 || (!source.loop && position>=source.clip.length))
            { if (source.isPlaying) source.Pause(); return; }
            double phase=source.loop ? position%source.clip.length : position;
            double drift=Math.Abs(source.time-phase);
            if(source.loop) drift=Math.Min(drift,Math.Abs(source.clip.length-drift));
            if (!source.isPlaying || drift>.25)
                // A float seconds value can round up to the exact end of a short clip.
                // Seek to a valid sample so wrapping never requests a position past the audio data.
                source.timeSamples=(int)Math.Clamp(Math.Floor(phase*source.clip.frequency),0,Math.Max(0,source.clip.samples-1));
            if (!source.isPlaying) source.Play();
        }
        private void OnDisable()
        {
            DownloadedAssetLibrary.Changed-=Invalidate;
            StopAllCoroutines(); requested=false;
            if (source!=null) source.Stop();
        }
    }
}
