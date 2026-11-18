using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BeatTheBoss.Poses
{
    // Only the owning rig's cameras hide these renderers. Preview copies and bosses retain their faces.
    public sealed class LocalAvatarHeadVisibility : MonoBehaviour
    {
        private Renderer[] renderers=Array.Empty<Renderer>();
        private readonly HashSet<Camera> cameras=new();
        private readonly Dictionary<Renderer,bool> previous=new();
        internal void Configure(Renderer[] hidden,OVRCameraRig rig)
        {
            Restore();renderers=hidden??Array.Empty<Renderer>();cameras.Clear();
            if(rig==null)return;
            foreach(Camera camera in rig.GetComponentsInChildren<Camera>(true))cameras.Add(camera);
        }
        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering+=Begin;RenderPipelineManager.endCameraRendering+=End;
            Camera.onPreCull+=BuiltInBegin;Camera.onPostRender+=BuiltInEnd;
        }
        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering-=Begin;RenderPipelineManager.endCameraRendering-=End;
            Camera.onPreCull-=BuiltInBegin;Camera.onPostRender-=BuiltInEnd;Restore();
        }
        private void BuiltInBegin(Camera camera) { if(GraphicsSettings.currentRenderPipeline==null)Hide(camera); }
        private void BuiltInEnd(Camera camera) { if(GraphicsSettings.currentRenderPipeline==null)Restore(); }
        private void Begin(ScriptableRenderContext context,Camera camera) => Hide(camera);
        private void End(ScriptableRenderContext context,Camera camera) => Restore();
        internal void Hide(Camera camera)
        {
            Restore();if(!cameras.Contains(camera))return;
            foreach(Renderer renderer in renderers)
                if(renderer!=null) { previous[renderer]=renderer.forceRenderingOff;renderer.forceRenderingOff=true; }
        }
        internal void Restore()
        {
            foreach(var pair in previous)if(pair.Key!=null)pair.Key.forceRenderingOff=pair.Value;
            previous.Clear();
        }
    }
}
