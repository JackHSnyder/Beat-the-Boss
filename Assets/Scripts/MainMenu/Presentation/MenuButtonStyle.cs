using TMPro;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    [CreateAssetMenu(menuName="Beat the Boss/Menu Button Style")]
    public sealed class MenuButtonStyle : ScriptableObject
    {
        public enum Role { Secondary, Primary, Accent }
        public Color background=new(.453f,.203f,.203f,.902f);
        public Color primaryBackground=new(.698f,.698f,.698f,.902f);
        public Color accentBackground=new(.472f,.327f,.327f,.902f);
        public Color text=new(.965f,.97f,.985f,1);
        public Color primaryText=new(.153f,.153f,.153f,1);
        public TMP_FontAsset font;
        public FontStyles fontStyle=FontStyles.Normal;
        public Material backgroundMaterial;
        public Vector4 cornerRadius=new(24,24,24,24);
        [Min(0)] public float horizontalPadding=16;
        public RuntimeAnimatorController visualController;
        private static MenuButtonStyle shared;
        public static MenuButtonStyle Shared
        {
            get { if(shared==null)shared=Resources.Load<MenuButtonStyle>("UI/Menu Button Style"); return shared; }
        }
        internal Color Background(Role role) => role==Role.Primary?primaryBackground:role==Role.Accent?accentBackground:background;
        internal Color TextColor(Role role) => role==Role.Primary?primaryText:text;
    }
}
