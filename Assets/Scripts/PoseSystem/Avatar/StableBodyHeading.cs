using UnityEngine;

namespace BeatTheBoss.Poses
{
    // Head yaw is undefined near vertical and ambiguous when inverted. Do not infer a new body facing there.
    internal sealed class StableBodyHeading
    {
        internal Vector3 Forward { get; private set; }
        internal bool Holding { get; private set; }
        private bool initialized;
        private Quaternion previousReference;

        internal void Update(Quaternion head,Quaternion reference,Vector3 up,float dt)
        {
            up=up.normalized;
            if(!initialized)
            {
                Forward=Vector3.ProjectOnPlane(reference*Vector3.forward,up).normalized;
                if(Forward.sqrMagnitude<.5f)Forward=Vector3.Cross(reference*Vector3.right,up).normalized;
                previousReference=reference;initialized=true;
            }
            else
            {
                // Deliberate scene/orbit locomotion still carries the held body with the player origin.
                Forward=Vector3.ProjectOnPlane(reference*Quaternion.Inverse(previousReference)*Forward,up).normalized;
                previousReference=reference;
            }
            Vector3 look=head*Vector3.forward;
            float vertical=Mathf.Abs(Vector3.Dot(look,up)),upright=Vector3.Dot(head*Vector3.up,up);
            if(Holding)
            {
                if(upright>.6f && vertical<.85f)Holding=false;
            }
            else if(upright<.35f || vertical>.94f)Holding=true;
            if(Holding)return;
            Vector3 desired=Vector3.ProjectOnPlane(look,up).normalized;
            if(desired.sqrMagnitude<.5f)return;
            float turn=Vector3.SignedAngle(Forward,desired,up);
            Forward=Quaternion.AngleAxis(Mathf.Clamp(turn,-360*Mathf.Max(0,dt),360*Mathf.Max(0,dt)),up)*Forward;
        }
    }
}
