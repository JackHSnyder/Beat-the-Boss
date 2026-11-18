using UnityEngine;

namespace BeatTheBoss.Poses
{
    // Menu and authoring scenes position the rig directly instead of grounding it against the level.
    [DefaultExecutionOrder(-10000)]
    public sealed class FloatingObserver : MonoBehaviour
    {
        private void Awake() => SuspendLocomotion(transform);

        internal static void SuspendLocomotion(Transform root)
        {
            foreach (var locomotor in root.GetComponentsInChildren<Oculus.Interaction.Locomotion.FirstPersonLocomotor>(true))
            {
                locomotor.DisableMovement();
                locomotor.GravityFactor = 0;
                locomotor.Velocity = Vector3.zero;
                locomotor.enabled = false;
            }
            foreach (var connection in root.GetComponentsInChildren<Oculus.Interaction.Locomotion.LocomotionEventsConnection>(true))
                connection.enabled = false;
        }
    }
}
