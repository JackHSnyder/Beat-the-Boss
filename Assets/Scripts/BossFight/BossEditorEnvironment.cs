using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;

namespace BeatTheBoss.Poses
{
    [DefaultExecutionOrder(200)]
    public sealed class BossEditorEnvironment : MonoBehaviour
    {
        public BossController boss;
        public Transform observer;
        public OVRCameraRig headset;
        public Transform dummy;
        public Camera desktopCamera;
        private AudioListener[] listeners;
        private AudioListener desktopListener;
        private Camera desktopBackground;
        private bool? headsetPresentation;
        private Vector3 widePosition;
        private Quaternion wideRotation;
        private Vector3 wideObserverPosition;
        private Quaternion wideObserverRotation;
        public event System.Action<Pose,Pose> ViewChanged;
        internal bool InHeadset => headsetPresentation == true;

        private void Awake()
        {
            // This scene is an observation tool. The locomotor otherwise applies gravity and
            // writes the rig back to its separate character controller after a viewpoint change.
            FloatingObserver.SuspendLocomotion(observer);
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                if (root.name == "Timeline viewing platform") root.SetActive(false);
            boss.previewMode = true; boss.playOnStart = false; boss.player = dummy;
            desktopCamera.stereoTargetEye = StereoTargetEyeMask.None;
            desktopCamera.depth = 100;
            var data = desktopCamera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            if (data == null) data = desktopCamera.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            data.allowXRRendering = false;
            // Clear the full desktop frame so resizing a partial viewport cannot leave old UI behind.
            desktopBackground = new GameObject("Editor desktop background", typeof(Camera)).GetComponent<Camera>();
            desktopBackground.transform.SetParent(transform, false);
            desktopBackground.depth = 99;
            desktopBackground.cullingMask = 0;
            desktopBackground.clearFlags = CameraClearFlags.SolidColor;
            desktopBackground.backgroundColor = new Color(.035f,.04f,.055f);
            desktopBackground.stereoTargetEye = StereoTargetEyeMask.None;
            desktopBackground.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>().allowXRRendering = false;
            desktopListener = desktopCamera.GetComponent<AudioListener>();
            listeners = observer.GetComponentsInChildren<AudioListener>(true);
            widePosition = desktopCamera.transform.position; wideRotation = desktopCamera.transform.rotation;
            wideObserverPosition = observer.position; wideObserverRotation = observer.rotation;
            ApplyPresentation();
        }
        public void FrameBoss()
        {
            if (InHeadset)
            {
                MoveObserver(boss.transform.position + new Vector3(0,1.5f,-4.5f));
                return;
            }
            desktopCamera.transform.position = boss.transform.position + new Vector3(0, 2.4f, -4.5f);
            desktopCamera.transform.LookAt(boss.transform.position + Vector3.up);
            desktopCamera.fieldOfView = 45;
        }
        public void FrameArena()
        {
            if (InHeadset)
            {
                Pose previous=new Pose(observer.position,observer.rotation);
                observer.SetPositionAndRotation(wideObserverPosition,wideObserverRotation);
                ObserverMoved(previous); return;
            }
            desktopCamera.transform.SetPositionAndRotation(widePosition, wideRotation); desktopCamera.fieldOfView = 65;
        }
        public void Zoom(float delta)
        {
            if (InHeadset)
            {
                Vector3 offset=observer.position-boss.transform.position; offset.y=0;
                if (offset.sqrMagnitude<.001f) offset=Vector3.back;
                float distance=Mathf.Clamp(offset.magnitude+delta*.15f,2.5f,18);
                Vector3 position=boss.transform.position+offset.normalized*distance;position.y=observer.position.y;
                MoveObserver(position); return;
            }
            desktopCamera.fieldOfView = Mathf.Clamp(desktopCamera.fieldOfView + delta, 20, 85);
        }
        public void Orbit(float angle)
        {
            // Positive means the player's right when looking toward the center.
            angle=-angle;
            if (InHeadset)
            {
                Vector3 offset=Quaternion.AngleAxis(angle,Vector3.up)*(observer.position-boss.transform.position);
                MoveObserver(boss.transform.position+offset); return;
            }
            desktopCamera.transform.RotateAround(boss.transform.position + Vector3.up, Vector3.up, angle);
            desktopCamera.transform.LookAt(boss.transform.position + Vector3.up);
        }
        private void MoveObserver(Vector3 position)
        {
            Pose previous=new Pose(observer.position,observer.rotation);
            // Move the tracking origin, retaining the player's tracked head pose and a level horizon.
            Vector3 facing=boss.transform.position-position; facing.y=0;
            observer.SetPositionAndRotation(position,facing.sqrMagnitude>.001f?Quaternion.LookRotation(facing):observer.rotation);
            ObserverMoved(previous);
        }
        private void ObserverMoved(Pose previous)
        {
            ViewChanged?.Invoke(previous,new Pose(observer.position,observer.rotation));
        }
        private void LateUpdate() => ApplyPresentation();
        private void ApplyPresentation()
        {
            // Keep the stereo output selected while the XR display is running, even if
            // device tracking is temporarily unavailable.
            var display = XRGeneralSettings.Instance?.Manager?.activeLoader?.GetLoadedSubsystem<XRDisplaySubsystem>();
            bool active = display?.running ?? XRSettings.isDeviceActive;
            if (headsetPresentation != active)
                SetHeadsetPresentation(active);
            if (active)
                return;

            float scale = Mathf.Min(Screen.width / 1600f, Screen.height / 900f);
            // Leave the lower area for tracks and the right side for node properties.
            float bottom = 436 * scale;
            desktopCamera.rect = new Rect(20 * scale / Screen.width, bottom / Screen.height,
                1100 * scale / Screen.width, Mathf.Max(1, Screen.height - bottom - 20 * scale) / Screen.height);
        }

        private void SetHeadsetPresentation(bool active)
        {
            headsetPresentation = active;
            // The rig owns the stereo camera selection. Changing its eye cameras externally
            // makes it reset the eye anchors when it restores the expected camera state.
            headset.disableEyeAnchorCameras = !active;
            headset.EnsureGameObjectIntegrity();
            desktopCamera.enabled = !active;
            desktopBackground.enabled = !active;
            desktopListener.enabled = !active;
            for (int i = 0; i < listeners.Length; i++) listeners[i].enabled = active && i == 0;
        }
    }
}
