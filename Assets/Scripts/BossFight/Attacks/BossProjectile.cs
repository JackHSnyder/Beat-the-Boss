using UnityEngine;

namespace BeatTheBoss.Poses
{
    [RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
    public sealed class BossProjectile : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] private float speed = 3f;
        [SerializeField, Min(0.01f)] private float lifetime = 8f;
        [SerializeField, Min(0f)] private float damage = 20f;

        private Rigidbody body;
        private SphereCollider hitbox;
        private Vector3 direction;
        private float age;
        private bool preview;
        private bool flying;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            hitbox = GetComponent<SphereCollider>();
        }

        internal void Launch(bool previewOnly)
        {
            preview = previewOnly;
            direction = transform.forward;
            flying = true;
            hitbox.enabled = !preview;
            if (preview)
            {
                body.detectCollisions = false;
                body.interpolation = RigidbodyInterpolation.None;
            }
        }

        private void FixedUpdate()
        {
            if (flying && !preview && AdvanceLifetime(Time.fixedDeltaTime))
                body.MovePosition(body.position + direction * (speed * Time.fixedDeltaTime));
        }

        internal bool AdvancePreview(float seconds)
        {
            if (!AdvanceLifetime(seconds))
                return false;
            transform.position += direction * (speed * seconds);
            return true;
        }

        private bool AdvanceLifetime(float seconds)
        {
            age += seconds;
            if (age < lifetime)
                return true;
            Retire();
            return false;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!flying || preview || !other.TryGetComponent(out EncounterPlayerDamage player))
                return;
            Retire();
            player.TakeDamage(damage);
        }

        internal void Retire()
        {
            flying = false;
            gameObject.SetActive(false);
            Destroy(gameObject);
        }
    }
}
