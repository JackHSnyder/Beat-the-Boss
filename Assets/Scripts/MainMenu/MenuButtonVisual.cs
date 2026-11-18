using Oculus.Interaction.Samples;
using TMPro;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    // Shares UISet visuals while animating Content independently of the full-size hit area.
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(UnityEngine.UI.Button))]
    public sealed class MenuButtonVisual : MonoBehaviour
    {
        [SerializeField] private MenuButtonStyle style;
        [SerializeField] private MenuButtonStyle.Role role;
        private bool finishSetup;
        public void Configure(MenuButtonStyle value, MenuButtonStyle.Role buttonRole)
        { style=value; role=buttonRole; Apply(); finishSetup=true; }
        private void OnEnable() { Apply(); finishSetup=true; }
        private void OnValidate() => Apply();
        private void LateUpdate()
        {
            if(style==null)return;
            var animator=GetComponent<Animator>();
            // New UISet animators initialize after Configure; theme switches may also replace them.
            bool replaced=style.visualController!=null && animator!=null && animator.runtimeAnimatorController!=style.visualController;
            if(!finishSetup && !replaced)return;
            finishSetup=false;
            Apply();
        }
        [ContextMenu("Apply shared button style")]
        public void Apply()
        {
            if(style==null)return;
            var background=transform.Find("Content/Background");
            if(background==null)return;
            var button=GetComponent<UnityEngine.UI.Button>();
            var image=background.GetComponent<UnityEngine.UI.Image>();
            image.color=style.Background(role);
            if(style.backgroundMaterial!=null)image.material=style.backgroundMaterial;
            var rounding=background.GetComponent<RoundedBoxUIProperties>();
            if(rounding!=null)rounding.borderRadius=style.cornerRadius;
            image.SetVerticesDirty();
            foreach(var label in background.GetComponentsInChildren<TMP_Text>(true))
            {
                if(style.font!=null)label.font=style.font;
                label.color=style.TextColor(role);
                // Letter keys must still show the case selected by the keyboard's Shift control.
                label.fontStyle=label.text.Length==1?style.fontStyle & ~(FontStyles.UpperCase|FontStyles.LowerCase|FontStyles.SmallCaps):style.fontStyle;
            }
            button.targetGraphic=image;
            button.transition=UnityEngine.UI.Selectable.Transition.Animation;
            var animator=GetComponent<Animator>();
            if(animator!=null && style.visualController!=null)
            {
                if(animator.runtimeAnimatorController!=style.visualController)
                    animator.runtimeAnimatorController=style.visualController;
                // UISet's alpha curves also restore cached color defaults. Capture this palette,
                // rather than the package prefab's white, before the first animated frame.
                if(Application.isPlaying && animator.isActiveAndEnabled) { animator.Rebind(); animator.Update(0); }
            }
            EnsureHitArea(button);
        }
        internal static void EnsureHitArea(UnityEngine.UI.Button button)
        {
            var hit=button.GetComponent<UnityEngine.UI.Image>();
            if(hit==null)hit=button.gameObject.AddComponent<UnityEngine.UI.Image>();
            hit.color=Color.clear; hit.raycastTarget=true;
        }
    }
}
