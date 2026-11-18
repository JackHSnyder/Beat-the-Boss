using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;

namespace BeatTheBoss.Poses
{
    [RequireComponent(typeof(CapsuleCollider))]
    public sealed class EncounterPlayerDamage : MonoBehaviour
    {
        [SerializeField] private Transform head;
        [SerializeField, Min(1f)] private float maxHealth = 100f;
        [SerializeField, Min(0f), Tooltip("Seconds without taking damage before health starts to recover.")]
        private float recoveryDelay = 4f;
        [SerializeField, Min(0f), Tooltip("Health restored per second after the recovery delay.")]
        private float recoveryPerSecond = 10f;
        [SerializeField] private Shader damageVignetteShader;
        [SerializeField, Range(0f, 1f)] private float maximumVignetteOpacity = 1f;
        [SerializeField] private UnityEvent<float> onDamaged = new();

        private static readonly int Opacity = Shader.PropertyToID("_Opacity");
        private CapsuleCollider hitbox;
        private MeshRenderer damageVignette;
        private Material vignetteMaterial;
        private double recoverAt;
        private bool returningToMenu;
        public float Health { get; private set; }

        private void Awake()
        {
            hitbox = GetComponent<CapsuleCollider>();
            Health = maxHealth;
            UpdateHitbox();
        }

        private void FixedUpdate() => UpdateHitbox();

        private void Update()
        {
            if (Health <= 0f)
            {
                ReturnToMenuIfReady();
                return;
            }

            if (Health >= maxHealth || recoveryPerSecond <= 0f)
                return;

            // Only count the part of this frame that falls after the recovery delay.
            double recoveryTime = Math.Min(Time.deltaTime, Time.timeAsDouble - recoverAt);
            if (recoveryTime <= 0)
                return;
            Health = Mathf.Min(maxHealth, Health + recoveryPerSecond * (float)recoveryTime);
            UpdateVignette();
        }

        private void UpdateHitbox()
        {
            if (head == null)
                return;
            Vector3 localHead = transform.InverseTransformPoint(head.position);
            // Follow the tracked body when the player leans or crouches. Use the
            // authored height when desktop play has no usable eye height.
            float height = localHead.y > 0.3f ? localHead.y + 0.15f : 1.65f;
            hitbox.height = Mathf.Max(hitbox.radius * 2f, height);
            hitbox.center = new Vector3(localHead.x, hitbox.height * 0.5f, localHead.z);
        }

        public void TakeDamage(float amount)
        {
            if (!isActiveAndEnabled || !float.IsFinite(amount) || amount <= 0f || Health <= 0f)
                return;
            float applied = Mathf.Min(amount, Health);
            Health -= applied;
            recoverAt = Time.timeAsDouble + recoveryDelay;
            UpdateVignette();
            onDamaged.Invoke(applied);
            if (Health <= 0f)
                ReturnToMenuIfReady();
        }

        private void ReturnToMenuIfReady()
        {
            // A lethal hit can land while the incoming scene fade is still running.
            if (returningToMenu || SceneHandler.IsTransitioning)
                return;
            returningToMenu = true;
            SceneHandler.TransitionTo(SceneDestination.MainMenu);
        }

        private void UpdateVignette()
        {
            // Make early damage readable, with the strongest red reserved for critical health.
            float opacity = maximumVignetteOpacity * Mathf.Sqrt(1f - Mathf.Clamp01(Health / maxHealth));
            if (damageVignette == null)
            {
                if (opacity <= 0f || damageVignetteShader == null || head == null)
                    return;

                vignetteMaterial = new Material(damageVignetteShader) { enableInstancing = true };
                GameObject overlay = GameObject.CreatePrimitive(PrimitiveType.Quad);
                overlay.name = "Damage vignette";
                Collider overlayCollider = overlay.GetComponent<Collider>();
                overlayCollider.enabled = false;
                Destroy(overlayCollider);
                overlay.transform.SetParent(head, false);
                overlay.transform.localPosition = Vector3.forward;
                damageVignette = overlay.GetComponent<MeshRenderer>();
                damageVignette.sharedMaterial = vignetteMaterial;
                // Draw above world visuals and below the scene transition fade.
                damageVignette.sortingOrder = short.MaxValue - 1;
                damageVignette.shadowCastingMode = ShadowCastingMode.Off;
                damageVignette.receiveShadows = false;
                damageVignette.allowOcclusionWhenDynamic = false;
            }

            vignetteMaterial.SetFloat(Opacity, opacity);
            damageVignette.enabled = opacity > 0f;
        }

        private void OnEnable() => UpdateVignette();

        private void OnDisable()
        {
            if (damageVignette != null)
                damageVignette.enabled = false;
        }

        private void OnDestroy()
        {
            if (damageVignette != null)
                Destroy(damageVignette.gameObject);
            if (vignetteMaterial != null)
                Destroy(vignetteMaterial);
        }
    }
}
