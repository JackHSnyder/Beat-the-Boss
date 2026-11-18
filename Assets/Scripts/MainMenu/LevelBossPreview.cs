using System;
using System.Collections.Generic;
using BeatTheBoss.Poses;
using UnityEngine;

// A visual-only stage, independent of the sliding UI roots so it can finish exiting
// when the level section closes. Only the selected level's idle recording is played.
public sealed class LevelBossPreview : MonoBehaviour
{
    [SerializeField] private Transform panelAnchor;
    [SerializeField] private MeshFilter platformSource;
    [SerializeField] private Transform viewer;
    [SerializeField, Min(.5f)] private float distanceBehindPanels = 2.2f;
    [SerializeField] private float platformTopHeight = .35f;
    [SerializeField, Min(.5f)] private float platformDiameter = 2.5f;
    [SerializeField, Min(0)] private float slideSeconds = .45f;
    [SerializeField, Min(1)] private float slideDistance = 3.5f;
    [SerializeField, Min(0)] private float idleLoopBlend = .2f;

    private sealed class Selection
    {
        internal string key, model, idle;
        internal bool Matches(Selection other) => other != null && key == other.key && model == other.model && idle == other.idle;
    }
    private Selection requested, displayed;
    private Transform stage, modelRoot;
    private BossRig rig;
    private MoveAnimation idle;
    private float progress, idleClock;
    private Vector3 activePosition;
    private readonly List<PoseJoint> sample = new();
    private readonly Dictionary<string, PoseJoint> next = new();

    internal void Show(string key, string model, string idleName)
    {
        var selection = new Selection { key = key, model = model, idle = idleName };
        if (selection.Matches(requested)) return;
        requested = selection;
        if (stage == null || progress == 0) ReplaceAtBottom();
    }

    public void Hide() => requested = null;

    private void Update()
    {
        if (stage == null) return;
        bool showing = requested != null && requested.Matches(displayed);
        float step = slideSeconds <= 0 || SceneHandler.ReducedTransitions ? 1 : Time.unscaledDeltaTime / slideSeconds;
        progress = Mathf.MoveTowards(progress, showing ? 1 : 0, step);
        stage.position = activePosition - Vector3.up * (slideDistance * (1 - Mathf.SmoothStep(0, 1, progress)));
        if (!showing && progress == 0)
        {
            ReplaceAtBottom();
            return;
        }
        FaceViewer();
        AnimateIdle(Time.unscaledDeltaTime);
    }

    private void ReplaceAtBottom()
    {
        if (stage != null) { stage.gameObject.SetActive(false); Destroy(stage.gameObject); }
        stage = modelRoot = null; rig = null; idle = null; displayed = null;
        progress = idleClock = 0;
        if (requested == null) return;

        GameObject prefab;
        try { prefab = DownloadedAssetLibrary.ResolveCharacter(requested.model); }
        catch (Exception error) when (AnimationStore.IsFileError(error))
        {
            Debug.LogWarning("Level preview is using StylizedCharacter: " + error.Message, this);
            prefab = DownloadedAssetLibrary.ResolveCharacter(DownloadedAssetLibrary.DefaultCharacter);
        }
        displayed = requested;
        Vector3 forward = Vector3.ProjectOnPlane(panelAnchor.forward, Vector3.up).normalized;
        activePosition = panelAnchor.position + forward * distanceBehindPanels;
        activePosition.y = (platformSource != null ? platformSource.GetComponent<Renderer>().bounds.max.y : 0) + platformTopHeight;
        stage = new GameObject("Level boss preview stage").transform;
        stage.SetParent(transform, false);
        stage.position = activePosition - Vector3.up * slideDistance;

        var platform = new GameObject("Preview platform", typeof(MeshFilter), typeof(MeshRenderer));
        platform.transform.SetParent(stage, false);
        if (platformSource != null)
        {
            platform.GetComponent<MeshFilter>().sharedMesh = platformSource.sharedMesh;
            platform.GetComponent<MeshRenderer>().sharedMaterials = platformSource.GetComponent<Renderer>().sharedMaterials;
            Vector3 scale = platformSource.transform.lossyScale;
            scale.x = scale.z = platformDiameter;
            platform.transform.localScale = scale;
            float top = platformSource.sharedMesh.bounds.max.y * scale.y;
            platform.transform.localPosition = Vector3.down * top;
        }

        var holder = new GameObject("Preview boss");
        holder.SetActive(false);
        holder.transform.SetParent(stage, false);
        var visual = Instantiate(prefab, holder.transform, false);
        // Disable tracking, physics and animation drivers before the visual is activated.
        BossRig.DisableLiveDrivers(visual);
        foreach (Rigidbody body in visual.GetComponentsInChildren<Rigidbody>(true)) { body.isKinematic = true; body.useGravity = false; }
        foreach (AudioSource audio in visual.GetComponentsInChildren<AudioSource>(true)) audio.enabled = false;
        foreach (Transform child in visual.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 0;
        modelRoot = holder.transform;
        rig = holder.AddComponent<BossRig>();
        rig.Rebind(visual.transform);
        holder.SetActive(true);
        // Ground the neutral model on the platform, regardless of prefab pivot or character height.
        Bounds bounds = default; bool found = false;
        foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled) continue;
            if (!found) { bounds = renderer.bounds; found = true; } else bounds.Encapsulate(renderer.bounds);
        }
        if (found) visual.transform.position += Vector3.up * (stage.position.y - bounds.min.y);
        if (!string.IsNullOrWhiteSpace(displayed.idle))
            idle = new BossController.Recording { savedName = displayed.idle }.Load(this);
        FaceViewer();
        AnimateIdle(0);
    }

    private void FaceViewer()
    {
        if (viewer == null)
        {
            var cameraRig = UnityEngine.Object.FindAnyObjectByType<OVRCameraRig>();
            if (cameraRig != null) viewer = cameraRig.centerEyeAnchor;
        }
        if (viewer == null || modelRoot == null) return;
        Vector3 direction = Vector3.ProjectOnPlane(viewer.position - modelRoot.position, Vector3.up);
        if (direction.sqrMagnitude > .001f) modelRoot.rotation = Quaternion.LookRotation(direction, Vector3.up);
    }

    private void AnimateIdle(float dt)
    {
        if (idle == null || rig == null) return;
        idleClock += dt;
        float length = idle.PlaybackEnd - idle.PlaybackStart;
        float seam = Mathf.Min(idleLoopBlend, length * .5f);
        float phase = Mathf.Repeat(idleClock, Mathf.Max(.001f, length - seam));
        rig.BeginPose();
        idle.Sample(idle.PlaybackStart + phase, sample, next); rig.Apply(sample, idle, 1);
        if (seam > 0 && idleClock >= length - seam && phase < seam)
        {
            idle.Sample(idle.PlaybackEnd - seam + phase, sample, next);
            rig.Apply(sample, idle, 1 - Mathf.SmoothStep(0, 1, phase / seam));
        }
        rig.Smooth(.035f, dt);
    }

    private void OnDisable()
    {
        requested = displayed = null;
        if (stage != null) Destroy(stage.gameObject);
        stage = modelRoot = null; rig = null; idle = null; progress = 0;
    }
}
