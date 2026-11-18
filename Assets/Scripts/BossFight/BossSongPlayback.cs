using System;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    // Uses elapsed encounter time, not the looping event cursor. Songs play once across timeline loops.
    [DefaultExecutionOrder(350)]
    internal sealed class BossSongPlayback : MonoBehaviour
    {
        private BossController boss;
        private BossTimelineWorkbench editor;
        private AudioSource source;
        private string loadedId;
        private int requestVersion;
        private bool requested;
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
                if (!string.IsNullOrEmpty(loadedId))
                    StartCoroutine(DownloadedAssetLibrary.LoadSong(loadedId,(clip,error)=>
                    {
                        if (version!=requestVersion) return;
                        source.clip=clip;
                        if (error!=null) Debug.LogWarning("Encounter song unavailable: "+error,boss);
                    }));
            }
            if (editor==null && boss.previewMode) editor=FindAnyObjectByType<BossTimelineWorkbench>();
            bool playing=boss.previewMode ? editor!=null && editor.Playing : boss.IsPlaying;
            double position=boss.ElapsedTime-boss.songDelay;
            if (!playing || source.clip==null || position<0 || position>=source.clip.length)
            { if (source.isPlaying) source.Pause(); return; }
            if (!source.isPlaying || Math.Abs(source.time-position)>.25)
                source.time=(float)position;
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
