using UnityEngine;

namespace BeatTheBoss.Poses
{
    // A floor telegraph followed by a straight, swept laser bolt. Preview advances without collisions.
    public sealed class BossLaserAttack : MonoBehaviour
    {
        [SerializeField] private LineRenderer warningLine;
        [SerializeField] private LineRenderer bolt;
        [SerializeField, Min(.01f)] private float warningSeconds = 1.2f;
        [SerializeField, Min(.01f)] private float warningFadeSeconds = .1f;
        [SerializeField, Min(.01f)] private float warningWidth = .12f;
        [SerializeField, Min(0)] private float warningOvershoot = .6f;
        [SerializeField, Min(.001f)] private float floorOffset = .035f;
        [SerializeField] private Color warningStart = new(1,.025f,.04f,.18f);
        [SerializeField] private Color warningEnd = new(1,.025f,.04f,.85f);
        [SerializeField, Min(.01f)] private float speed = 60f;
        [SerializeField, Min(.01f)] private float range = 30f;
        [SerializeField, Min(.01f)] private float boltLength = 1.1f;
        [SerializeField, Min(.001f)] private float boltWidth = .035f;
        [SerializeField, Min(0)] private float damage = 20f;
        [SerializeField] private LayerMask collisionLayers = ~0;

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private MaterialPropertyBlock warningProperties;
        private Ray aim;
        private Transform owner;
        private float age, distance;
        private bool preview, active, fired, finished;
        internal float Duration => warningSeconds + warningFadeSeconds + range / Mathf.Max(.01f,speed) + 1f/60;

        private void Awake()
        {
            warningLine.enabled = bolt.enabled = false;
        }
        internal void Launch(float floorY, float edgeDistance, Transform shooter, bool previewOnly)
        {
            owner = shooter; preview = previewOnly; active = true;
            age = distance = 0; fired = finished = false;
            aim = new Ray(transform.position, transform.forward);
            warningProperties = new MaterialPropertyBlock();
            warningLine.useWorldSpace = bolt.useWorldSpace = true;
            warningLine.positionCount = bolt.positionCount = 2;
            warningLine.widthMultiplier = warningWidth; bolt.widthMultiplier = boltWidth;
            warningLine.alignment = LineAlignment.TransformZ;
            warningLine.transform.rotation = Quaternion.Euler(90,0,0);
            Vector3 start = new(aim.origin.x,floorY+floorOffset,aim.origin.z);
            warningLine.SetPosition(0,start);
            warningLine.SetPosition(1,start+aim.direction*Mathf.Min(range,Mathf.Max(0,edgeDistance)+warningOvershoot));
            bolt.SetPosition(0,aim.origin); bolt.SetPosition(1,aim.origin);
            DrawWarning();
        }
        private void Update()
        {
            if (active && !preview) Advance(Time.deltaTime);
        }
        internal bool Advance(float seconds)
        {
            if (!active) return false;
            if (finished) { Retire(); return false; }
            age += Mathf.Max(0,seconds);
            DrawWarning();
            float flightTime = age-warningSeconds-warningFadeSeconds;
            if (flightTime < 0) return true;
            if (!fired)
            {
                fired = true;
                // Sphere casts do not reliably report an initial overlap.
                if (!preview)
                    foreach (var collider in Physics.OverlapSphere(aim.origin,boltWidth*.5f,collisionLayers,QueryTriggerInteraction.Collide))
                        if (CanHit(collider)) { Hit(collider); break; }
            }
            float nextDistance = Mathf.Min(range,flightTime*speed);
            if (!preview && !finished && nextDistance > distance)
            {
                RaycastHit nearest = default; float nearestDistance = float.PositiveInfinity;
                foreach (var hit in Physics.SphereCastAll(aim.GetPoint(distance),boltWidth*.5f,aim.direction,
                    nextDistance-distance,collisionLayers,QueryTriggerInteraction.Collide))
                {
                    if (hit.distance >= nearestDistance || !CanHit(hit.collider)) continue;
                    nearest = hit; nearestDistance = hit.distance;
                }
                if (nearestDistance < float.PositiveInfinity)
                {
                    distance += nearest.distance; Hit(nearest.collider);
                }
                else distance = nextDistance;
            }
            else if (!finished) distance = nextDistance;
            bolt.enabled = true;
            bolt.SetPosition(0,aim.GetPoint(Mathf.Max(0,distance-boltLength)));
            bolt.SetPosition(1,aim.GetPoint(distance));
            if (distance >= range) finished = true;
            return true;
        }
        private bool CanHit(Collider other)
        {
            if (owner != null && other.transform.IsChildOf(owner)) return false;
            var player = other.GetComponentInParent<EncounterPlayerDamage>();
            return player != null ? player.isActiveAndEnabled : !other.isTrigger;
        }
        private void Hit(Collider other)
        {
            finished = true;
            other.GetComponentInParent<EncounterPlayerDamage>()?.TakeDamage(damage);
        }
        private void DrawWarning()
        {
            warningLine.enabled = age < warningSeconds+warningFadeSeconds;
            if (!warningLine.enabled) return;
            Color color = Color.Lerp(warningStart,warningEnd,Mathf.Clamp01(age/warningSeconds));
            color.a *= 1-Mathf.InverseLerp(warningSeconds,warningSeconds+warningFadeSeconds,age);
            warningProperties.SetColor(BaseColor,color);
            warningLine.SetPropertyBlock(warningProperties);
        }
        internal void Retire()
        {
            active = false; gameObject.SetActive(false); Destroy(gameObject);
        }
    }
}
