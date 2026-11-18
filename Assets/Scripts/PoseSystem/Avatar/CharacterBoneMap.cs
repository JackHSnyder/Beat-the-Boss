using System;
using System.Collections.Generic;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    // Aliases preserve the native Meta skeleton/config while exposing the game's established rig names.
    public sealed class CharacterBoneMap : MonoBehaviour
    {
        [Serializable] public sealed class Binding
        {
            public string name;
            public Transform joint;
            public Quaternion aimCorrection=Quaternion.identity;
        }
        public List<Binding> bindings=new();
        public Renderer[] firstPersonHiddenRenderers=Array.Empty<Renderer>();
        public string NameOf(Transform joint) => bindings.Find(b=>b.joint==joint)?.name ?? "Native:"+joint.name;
        internal Quaternion AimCorrection(string name) => bindings.Find(b=>b.name==name)?.aimCorrection ?? Quaternion.identity;
    }
}
