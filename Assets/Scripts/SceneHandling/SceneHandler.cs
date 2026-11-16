// Runs scene changes and carries the fade from the outgoing player rig to the incoming one.

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;

public class SceneHandler : MonoBehaviour {
    private const string ReducedTransitionsKey = "Accessibility.ReducedTransitions";

    [SerializeField, Min(0f)]
    private float fadeOutTime = 2f, fadeInTime = 2f;
    [SerializeField, Min(0f)]
    [Tooltip("Time to hold the incoming scene at full black before starting the reveal.")]
    private float fadeInBlackHold = 0.5f;
    [SerializeField] private Shader headsetFadeShader;
    [SerializeField] private Shader headsetFadeInShader;
    [SerializeField] private Shader reducedFadeShader;
    [SerializeField, Min(0f)]
    [Tooltip("Seconds for each half of a reduced transition.")]
    private float reducedFadeTime = 1.25f;
    
    private static SceneHandler instance;
    private bool transitioning;
    private GameObject fadeObject;
    private Image desktopFade;
    private Material fadeMaterial;

    internal static bool ReducedTransitions
    {
        get => PlayerPrefs.GetInt(ReducedTransitionsKey, 0) != 0;
        set
        {
            PlayerPrefs.SetInt(ReducedTransitionsKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    void Awake() {
        if (instance != null && instance != this) {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public static void TransitionTo(SceneDestination destination)
    {
        if (instance == null)
        {
            Debug.LogError("Add a SceneHandler to the starting scene.");
            return;
        }

        if (instance.transitioning)
            return;

        string path = instance.GetScenePath(destination);
        if (string.IsNullOrEmpty(path) || !Application.CanStreamedLevelBeLoaded(path))
        {
            Debug.LogError($"Scene {destination} is missing from the build scene list.");
            return;
        }

        if (SceneManager.GetActiveScene().path == path)
            return;

        instance.transitioning = true;
        instance.StartCoroutine(instance.SwitchScene(path));
    }

    private IEnumerator SwitchScene(string path)
    {
        Scene outgoingScene = SceneManager.GetActiveScene();
        List<SceneBackgroundAudio> backgroundAudio = GetBackgroundAudio(outgoingScene);
        bool audioFadeInStarted = false;
        try
        {
            // Use the same preference for both halves of this scene transition.
            bool reduced = ReducedTransitions;
            Shader fadeOut = reduced ? reducedFadeShader : headsetFadeShader;
            Shader fadeIn = reduced || headsetFadeInShader == null ? fadeOut : headsetFadeInShader;
            bool wasInHeadset = XRSettings.isDeviceActive;
            CreateFade(outgoingScene, fadeOut, wasInHeadset, 0f);
            float audioFadeSeconds = 0f;
            foreach (SceneBackgroundAudio track in backgroundAudio)
                audioFadeSeconds = Mathf.Max(audioFadeSeconds, track.FadeOut());
            double audioFadeEnd = Time.unscaledTimeAsDouble + audioFadeSeconds;

            yield return Fade(0f, 1f, reduced ? reducedFadeTime : fadeOutTime);
            while (Time.unscaledTimeAsDouble < audioFadeEnd)
                yield return null;
            // Let the final black frame and silent audio settle before replacing the scene.
            yield return new WaitForEndOfFrame();

            void CoverIncomingScene(Scene scene, LoadSceneMode mode)
            {
                if (scene.path != path)
                    return;
                backgroundAudio = GetBackgroundAudio(scene);
                foreach (SceneBackgroundAudio track in backgroundAudio)
                    track.DeferStartupFade();
                // Keep the XR fade if the runtime temporarily reports inactive while changing rigs.
                CreateFade(scene, fadeIn, wasInHeadset || XRSettings.isDeviceActive, 1f);
            }

            SceneManager.sceneLoaded += CoverIncomingScene;
            try
            {
                AsyncOperation load = SceneManager.LoadSceneAsync(path, LoadSceneMode.Single);
                if (load != null)
                    yield return load;
                else
                    Debug.LogError($"Unity could not start loading '{path}'.", this);
            }
            finally
            {
                SceneManager.sceneLoaded -= CoverIncomingScene;
            }

            yield return new WaitForEndOfFrame();
            if (fadeInBlackHold > 0f)
                yield return new WaitForSecondsRealtime(fadeInBlackHold);
            FadeInBackgroundAudio(backgroundAudio);
            audioFadeInStarted = true;
            yield return Fade(1f, 0f, reduced ? reducedFadeTime : fadeInTime);
        }
        finally
        {
            if (!audioFadeInStarted)
                FadeInBackgroundAudio(backgroundAudio);
            ClearFade();
            transitioning = false;
        }
    }

    private static List<SceneBackgroundAudio> GetBackgroundAudio(Scene scene)
    {
        var tracks = new List<SceneBackgroundAudio>();
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (SceneBackgroundAudio track in root.GetComponentsInChildren<SceneBackgroundAudio>())
                if (track.isActiveAndEnabled)
                    tracks.Add(track);
        return tracks;
    }

    private static void FadeInBackgroundAudio(List<SceneBackgroundAudio> tracks)
    {
        foreach (SceneBackgroundAudio track in tracks)
            if (track != null)
                track.FadeIn();
    }

    private void CreateFade(Scene scene, Shader shader, bool useHeadset, float initialOpacity)
    {
        ClearFade();
        if (useHeadset && shader != null)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                OVRCameraRig rig = root.GetComponentInChildren<OVRCameraRig>();
                if (rig == null || rig.centerEyeAnchor == null)
                    continue;

                fadeMaterial = new Material(shader)
                {
                    enableInstancing = true,
                    renderQueue = 5000
                };
                fadeMaterial.SetFloat("_Opacity", initialOpacity);
                fadeObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
                fadeObject.name = "Headset scene fade";
                Destroy(fadeObject.GetComponent<Collider>());
                fadeObject.transform.SetParent(rig.centerEyeAnchor, false);
                fadeObject.transform.localPosition = Vector3.forward;
                var renderer = fadeObject.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = fadeMaterial;
                // World-space canvases sort by layer and order before render queue.
                // Put the fade above the recording canvas so its pixels are covered as well.
                renderer.sortingOrder = short.MaxValue;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.allowOcclusionWhenDynamic = false;
                return;
            }
        }

        fadeObject = new GameObject("Desktop scene fade", typeof(Canvas));
        SceneManager.MoveGameObjectToScene(fadeObject, scene);
        Canvas canvas = fadeObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;
        var cover = new GameObject("Black", typeof(RectTransform), typeof(Image));
        cover.transform.SetParent(canvas.transform, false);
        desktopFade = cover.GetComponent<Image>();
        desktopFade.color = new Color(0f, 0f, 0f, initialOpacity);
        desktopFade.raycastTarget = false;
        desktopFade.rectTransform.anchorMin = Vector2.zero;
        desktopFade.rectTransform.anchorMax = Vector2.one;
        desktopFade.rectTransform.offsetMin = Vector2.zero;
        desktopFade.rectTransform.offsetMax = Vector2.zero;
    }

    private void ClearFade()
    {
        if (fadeObject != null)
        {
            fadeObject.SetActive(false);
            Destroy(fadeObject);
        }
        if (fadeMaterial != null)
            Destroy(fadeMaterial);

        fadeObject = null;
        fadeMaterial = null;
        desktopFade = null;
    }

    private void SetOpacity(float opacity)
    {
        if (desktopFade != null)
            desktopFade.color = new Color(0f, 0f, 0f, opacity);
        if (fadeMaterial != null)
            fadeMaterial.SetFloat("_Opacity", opacity);
    }

    private IEnumerator Fade(float from, float to, float seconds)
    {
        for (float elapsed = 0f; elapsed < seconds; elapsed += Time.unscaledDeltaTime)
        {
            SetOpacity(Mathf.Lerp(from, to, elapsed / seconds));
            yield return null;
        }

        SetOpacity(to);
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
        ClearFade();
    }

    [System.Serializable]
    private sealed class SceneEntry
    {
        public SceneDestination destination;

    #if UNITY_EDITOR
        public UnityEditor.SceneAsset scene;
    #endif

        [HideInInspector] public string path;
    }

    [SerializeField] private SceneEntry[] scenes = BuildSceneEntries();

    private static SceneEntry[] BuildSceneEntries(SceneEntry[] existing = null)
    {
        var destinations = (SceneDestination[])Enum.GetValues(typeof(SceneDestination));
        var entries = new SceneEntry[destinations.Length];
        for (int i = 0; i < destinations.Length; i++)
        {
            SceneDestination destination = destinations[i];
            SceneEntry entry = existing == null ? null :
                Array.Find(existing, candidate => candidate != null && candidate.destination == destination);
            entries[i] = entry ?? new SceneEntry { destination = destination };
        }

        return entries;
    }

    #if UNITY_EDITOR
    private void OnValidate()
    {
        // Match by destination so enum changes keep the scene assets already assigned.
        scenes = BuildSceneEntries(scenes);
        foreach (SceneEntry entry in scenes)
            entry.path = entry.scene == null
                ? string.Empty
                : UnityEditor.AssetDatabase.GetAssetPath(entry.scene);
    }
    #endif

    private string GetScenePath(SceneDestination destination)
    {
        foreach (SceneEntry entry in scenes)
        {
            if (entry.destination == destination)
                return entry.path;
        }

        return string.Empty;
    }
}
