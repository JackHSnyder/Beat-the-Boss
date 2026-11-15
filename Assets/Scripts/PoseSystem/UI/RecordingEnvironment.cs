// Fades the recording floor against optional passthrough on Horizon OS and in Meta Link Play mode.

using UnityEngine;
using UnityEngine.Rendering;

namespace BeatTheBoss.Poses
{
    public sealed class RecordingEnvironment : MonoBehaviour
    {
        [SerializeField] private OVRCameraRig cameraRig;
        [SerializeField] private Renderer floorRenderer;
        [SerializeField, Min(0.1f)] private float cycleSeconds = 12f;
        [SerializeField, Min(0f)] private float initialHoldSeconds = 3f;
        [SerializeField, Min(1f)]
        [Tooltip("For each fade, time below its opacity midpoint divided by time above it. 1 gives an even cycle.")]
        private float troughToPeakTimeRatio = 1f;

        [Header("Passthrough Opacity")]
        [SerializeField, Range(0f, 1f)] private float passthroughMinOpacity = 0f;
        [SerializeField, Range(0f, 1f)] private float passthroughMaxOpacity = 0.5f;

        [Header("Floor Opacity")]
        [SerializeField, Range(0f, 1f)] private float floorMinOpacity = 0.25f;
        [SerializeField, Range(0f, 1f)] private float floorMaxOpacity = 1f;

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private OVRManager manager;
        private OVRPassthroughLayer passthrough;
        private Camera[] eyeCameras;
        private Material originalFloorMaterial;
        private Material fadingFloorMaterial;
        private Color floorColor;
        private bool previousPassthroughEnabled;
        private bool backgroundTransparent;
        private float fadeStarted;

        private void Update()
        {
            if (passthrough == null)
            {
                RuntimePlatform platform = Application.platform;
                if (cameraRig == null || platform is not (RuntimePlatform.Android or RuntimePlatform.WindowsEditor))
                {
                    enabled = false;
                    return;
                }

                if (!OVRPlugin.initialized)
                    return;

                var headset = OVRPlugin.GetSystemHeadsetType();
                if (headset == OVRPlugin.SystemHeadset.None)
                    return;

                if (!IsSupportedHeadset(platform, headset) ||
                    !cameraRig.TryGetComponent(out manager) || !OVRManager.IsInsightPassthroughSupported())
                {
                    enabled = false;
                    return;
                }

                eyeCameras = new[]
                {
                    cameraRig.centerEyeAnchor.GetComponent<Camera>(),
                    cameraRig.leftEyeAnchor.GetComponent<Camera>(),
                    cameraRig.rightEyeAnchor.GetComponent<Camera>()
                };
                previousPassthroughEnabled = manager.isInsightPassthroughEnabled;
                manager.isInsightPassthroughEnabled = true;
                var layerObject = new GameObject("Recording passthrough");
                layerObject.transform.SetParent(transform, false);
                passthrough = layerObject.AddComponent<OVRPassthroughLayer>();
                passthrough.textureOpacity = Mathf.Min(passthroughMinOpacity, passthroughMaxOpacity);
            }

            // The SDK composites this layer behind the eye buffer. Only clear pixels reveal it.
            bool initialized = OVRManager.IsInsightPassthroughInitialized();
            if (initialized != backgroundTransparent)
            {
                SetBackgroundTransparency(initialized);
                if (initialized)
                    BeginFloorFade();
                else
                    RestoreFloor();
                fadeStarted = Time.unscaledTime;
            }
            if (initialized)
            {
                float elapsed = Mathf.Max(0f, Time.unscaledTime - fadeStarted - initialHoldSeconds);
                float cyclePosition = Mathf.Repeat(elapsed / Mathf.Max(0.1f, cycleSeconds), 1f);
                float passthroughAmount = EvaluateFade(cyclePosition, troughToPeakTimeRatio);
                passthrough.textureOpacity = InterpolateOpacity(passthroughMinOpacity, passthroughMaxOpacity, passthroughAmount);
                if (fadingFloorMaterial != null)
                {
                    // Offset the cycle before weighting so both fades have the same trough and peak durations.
                    float floorPosition = Mathf.Repeat(cyclePosition + 0.5f, 1f);
                    float floorAmount = EvaluateFade(floorPosition, troughToPeakTimeRatio);
                    floorColor.a = InterpolateOpacity(floorMinOpacity, floorMaxOpacity, floorAmount);
                    fadingFloorMaterial.SetColor(BaseColor, floorColor);
                }
            }
        }

        private static float EvaluateFade(float cyclePosition, float timeRatio)
        {
            float halfPhase = Mathf.PI * cyclePosition;
            float sine = Mathf.Sin(halfPhase);
            if (timeRatio <= 1f)
                return sine * sine;

            // Move the midpoint crossing so the time below it is timeRatio times the time above it.
            float crossingOffset = (Mathf.PI * 0.5f) / (1f + timeRatio);
            float peakWeight = sine * Mathf.Sin(crossingOffset);
            float troughWeight = Mathf.Sin(Mathf.PI * (0.5f - cyclePosition)) * Mathf.Cos(crossingOffset);
            double peakSquared = (double)peakWeight * peakWeight;
            return (float)(peakSquared / (peakSquared + (double)troughWeight * troughWeight));
        }

        private static float InterpolateOpacity(float min, float max, float amount) =>
            Mathf.Lerp(Mathf.Min(min, max), Mathf.Max(min, max), amount);

        private void BeginFloorFade()
        {
            if (floorRenderer == null || floorRenderer.sharedMaterial == null)
                return;

            originalFloorMaterial = floorRenderer.sharedMaterial;
            // The recording floor uses URP Lit. Clone it so only this floor uses transparent rendering.
            fadingFloorMaterial = new Material(originalFloorMaterial);
            floorColor = fadingFloorMaterial.GetColor(BaseColor);
            fadingFloorMaterial.SetFloat("_Surface", 1f);
            fadingFloorMaterial.SetFloat("_Blend", 0f);
            fadingFloorMaterial.SetFloat("_BlendModePreserveSpecular", 0f);
            fadingFloorMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            fadingFloorMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            fadingFloorMaterial.SetOverrideTag("RenderType", "Transparent");
            fadingFloorMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            fadingFloorMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            fadingFloorMaterial.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            fadingFloorMaterial.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            fadingFloorMaterial.SetFloat("_ZWrite", 0f);
            fadingFloorMaterial.SetShaderPassEnabled("DepthOnly", false);
            fadingFloorMaterial.SetShaderPassEnabled("ShadowCaster", false);
            fadingFloorMaterial.renderQueue = (int)RenderQueue.Transparent;
            floorRenderer.sharedMaterial = fadingFloorMaterial;
        }

        private void RestoreFloor()
        {
            if (fadingFloorMaterial == null)
                return;

            if (floorRenderer != null)
                floorRenderer.sharedMaterial = originalFloorMaterial;
            Destroy(fadingFloorMaterial);
            fadingFloorMaterial = null;
        }

        private static bool IsSupportedHeadset(RuntimePlatform platform, OVRPlugin.SystemHeadset headset)
        {
            // Link passthrough is for development. Shipped builds only use standalone Horizon OS.
            return platform switch
            {
                RuntimePlatform.WindowsEditor => headset is OVRPlugin.SystemHeadset.Oculus_Link_Quest or
                    OVRPlugin.SystemHeadset.Oculus_Link_Quest_2 or OVRPlugin.SystemHeadset.Meta_Link_Quest_Pro or
                    OVRPlugin.SystemHeadset.Meta_Link_Quest_3 or OVRPlugin.SystemHeadset.Meta_Link_Quest_3S or
                    OVRPlugin.SystemHeadset.Meta_Link_VR_Glasses,
                RuntimePlatform.Android => headset is OVRPlugin.SystemHeadset.Oculus_Quest or
                    OVRPlugin.SystemHeadset.Oculus_Quest_2 or OVRPlugin.SystemHeadset.Meta_Quest_Pro or
                    OVRPlugin.SystemHeadset.Meta_Quest_3 or OVRPlugin.SystemHeadset.Meta_Quest_3S or
                    OVRPlugin.SystemHeadset.Meta_VR_Glasses,
                _ => false
            };
        }

        private void SetBackgroundTransparency(bool transparent)
        {
            backgroundTransparent = transparent;
            foreach (Camera eye in eyeCameras)
                if (eye != null)
                    eye.backgroundColor = transparent ? Color.clear : Color.black;
        }

        private void OnDisable()
        {
            RestoreFloor();
            if (passthrough == null)
                return;

            SetBackgroundTransparency(false);
            passthrough.gameObject.SetActive(false);
            Destroy(passthrough.gameObject);
            passthrough = null;
            if (manager != null)
                manager.isInsightPassthroughEnabled = previousPassthroughEnabled;
        }
    }
}
