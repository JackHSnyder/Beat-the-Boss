// Fades one scene's background track between silence and its Audio Source volume.

using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public sealed class SceneBackgroundAudio : MonoBehaviour
{
    [SerializeField, Min(0f)] private float fadeInSeconds = 1.5f;
    [SerializeField, Min(0f)] private float fadeOutSeconds = 1.5f;

    private AudioSource source;
    private float trackVolume;
    private bool deferStartupFade;
    private Coroutine fade;

    private void Awake()
    {
        source = GetComponent<AudioSource>();
        source.playOnAwake = false;
        trackVolume = source.volume;
        source.volume = 0f;
    }

    private bool CanPlay => isActiveAndEnabled && source.isActiveAndEnabled && source.resource != null;

    private bool CanStartStartupFade => !deferStartupFade && CanPlay;

    private IEnumerator Start()
    {
        if (!CanStartStartupFade)
            yield break;

        // Let scene and XR initialization finish their first frame before starting the track.
        yield return null;
        if (!CanStartStartupFade)
            yield break;

        AudioClip clip = source.clip;
        if (clip != null)
        {
            // Clips without preload can otherwise spend part of the fade loading.
            if (clip.loadState == AudioDataLoadState.Unloaded)
                clip.LoadAudioData();
            while (CanStartStartupFade && clip.loadState == AudioDataLoadState.Loading)
                yield return null;
            if (clip.loadState == AudioDataLoadState.Failed)
            {
                Debug.LogWarning($"Could not load background audio '{clip.name}'.", this);
                yield break;
            }
        }

        if (!CanStartStartupFade)
            yield break;

        if (fadeInSeconds > 0f)
        {
            // Give playback a frame to start at zero gain before starting the fade clock.
            source.Play();
            yield return null;
            while (CanStartStartupFade && clip != null && source.isPlaying && source.timeSamples == 0)
                yield return null;
        }

        if (CanStartStartupFade)
            FadeIn();
    }

    // SceneHandler calls this before Start when it owns the incoming reveal.
    internal void DeferStartupFade() => deferStartupFade = true;

    internal void FadeIn()
    {
        if (!CanPlay)
            return;

        if (!source.isPlaying)
            source.Play();
        FadeTo(trackVolume, fadeInSeconds);
    }

    internal float FadeOut()
    {
        DeferStartupFade();
        return FadeTo(0f, source.isPlaying ? fadeOutSeconds : 0f);
    }

    private float FadeTo(float targetVolume, float seconds)
    {
        if (fade != null)
        {
            StopCoroutine(fade);
            fade = null;
        }

        float currentVolume = source.volume;
        if (seconds <= 0f || Mathf.Approximately(currentVolume, targetVolume))
        {
            source.volume = targetVolume;
            return 0f;
        }

        fade = StartCoroutine(FadeVolume(currentVolume, targetVolume, seconds));
        return seconds;
    }

    private IEnumerator FadeVolume(float from, float to, float seconds)
    {
        double start = Time.unscaledTimeAsDouble;
        while (Time.unscaledTimeAsDouble - start < seconds)
        {
            float progress = (float)((Time.unscaledTimeAsDouble - start) / seconds);
            source.volume = Mathf.Lerp(from, to, progress);
            yield return null;
        }

        source.volume = to;
        fade = null;
    }
}
