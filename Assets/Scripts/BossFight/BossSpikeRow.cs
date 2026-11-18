using UnityEngine;

namespace BeatTheBoss.Poses
{
    [RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
    public sealed class BossSpikeRow : MonoBehaviour
    {
        [SerializeField] private MeshRenderer warningBox;
        [SerializeField] private Mesh spikeMesh;
        [SerializeField] private Material spikeMaterial;
        [SerializeField, Min(0.1f)] private float width = 1.68f;
        [SerializeField, Min(0.1f)] private float height = 1.8f;
        [SerializeField, Min(0f)] private float overshoot = 0.6f;
        [SerializeField, Min(0.1f)] private float spikeSpacing = 0.78f;
        [SerializeField, Min(0.01f)] private float warningSeconds = 1.8f;
        [SerializeField, Min(0.01f)] private float warningFadeSeconds = 0.12f;
        [SerializeField, Min(0.01f)] private float riseSeconds = 0.2f;
        [SerializeField, Min(0f)] private float holdSeconds = 0.55f;
        [SerializeField, Min(0.01f)] private float retractSeconds = 0.25f;
        [SerializeField, Min(0f)] private float damage = 30f;
        [SerializeField] private Color warningStart = new(0.48f, 0.035f, 0.045f, 0.16f);
        [SerializeField] private Color warningEnd = new(1f, 0.025f, 0.04f, 0.69f);

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private BoxCollider hitbox;
        private MeshRenderer spikes;
        private Mesh combinedMesh;
        private MaterialPropertyBlock warningProperties;
        private float age;
        private float length;
        private bool preview;
        private bool active;
        private bool hitPlayer;

        internal float Duration => warningSeconds + warningFadeSeconds + riseSeconds + holdSeconds + retractSeconds;

        internal void Launch(float edgeDistance, bool previewOnly)
        {
            preview = previewOnly;
            active = true;
            length = edgeDistance + overshoot;
            hitbox = GetComponent<BoxCollider>();
            hitbox.enabled = false;
            warningProperties = new MaterialPropertyBlock();
            warningBox.transform.localPosition = new Vector3(0f, 0.14f, length * 0.5f);
            warningBox.transform.localScale = new Vector3(width, 0.22f, length);
            BuildSpikes();
            DrawAtAge();
        }

        private void BuildSpikes()
        {
            const int columns = 3;
            int rows = Mathf.Max(1, Mathf.CeilToInt(length / spikeSpacing));
            float columnWidth = width / columns;
            float rowLength = length / rows;
            var instances = new CombineInstance[rows * columns];
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    float spikeHeight = height * ((row + column) % 2 == 0 ? 1f : 0.83f);
                    float stagger = column == 1 ? 0.11f : -0.11f;
                    Vector3 position = new((column - 1) * columnWidth, 0f, (row + 0.5f + stagger) * rowLength);
                    Vector3 scale = new(columnWidth, spikeHeight, rowLength * 0.74f);
                    instances[row * columns + column] = new CombineInstance
                    {
                        mesh = spikeMesh,
                        transform = Matrix4x4.TRS(position, Quaternion.identity, scale)
                    };
                }
            }

            combinedMesh = new Mesh { name = "Raised spike row" };
            combinedMesh.CombineMeshes(instances);
            var spikeObject = new GameObject("Spikes", typeof(MeshFilter), typeof(MeshRenderer));
            spikeObject.transform.SetParent(transform, false);
            spikeObject.GetComponent<MeshFilter>().sharedMesh = combinedMesh;
            spikes = spikeObject.GetComponent<MeshRenderer>();
            spikes.sharedMaterial = spikeMaterial;
        }

        private void Update()
        {
            if (active && !preview)
                Advance(Time.deltaTime);
        }

        internal bool Advance(float seconds)
        {
            age += seconds;
            if (age >= Duration)
            {
                Retire();
                return false;
            }
            DrawAtAge();
            return true;
        }

        private void DrawAtAge()
        {
            float strikeAge = age - warningSeconds - warningFadeSeconds;
            bool warningVisible = strikeAge < 0f;
            warningBox.enabled = warningVisible;
            if (warningVisible)
            {
                Color color = Color.Lerp(warningStart, warningEnd, age / warningSeconds);
                color.a *= 1f - Mathf.InverseLerp(warningSeconds, warningSeconds + warningFadeSeconds, age);
                warningProperties.SetColor(BaseColor, color);
                warningBox.SetPropertyBlock(warningProperties);
            }

            spikes.enabled = !warningVisible;
            float startingHeight = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(strikeAge / riseSeconds));
            float retreat = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(riseSeconds + holdSeconds,
                riseSeconds + holdSeconds + retractSeconds, strikeAge));
            float exposedHeight = height * startingHeight * (1f - retreat);
            spikes.transform.localPosition = Vector3.down * (height - exposedHeight);

            bool dangerous = !preview && !hitPlayer && exposedHeight > 0.03f && strikeAge < riseSeconds + holdSeconds;
            hitbox.enabled = dangerous;
            if (dangerous)
            {
                hitbox.size = new Vector3(width, exposedHeight, length);
                hitbox.center = new Vector3(0f, exposedHeight * 0.5f, length * 0.5f);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!active || preview || hitPlayer || !hitbox.enabled ||
                !other.TryGetComponent(out EncounterPlayerDamage player))
                return;
            hitPlayer = true;
            hitbox.enabled = false;
            player.TakeDamage(damage);
        }

        internal void Retire()
        {
            active = false;
            gameObject.SetActive(false);
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (combinedMesh != null)
                Destroy(combinedMesh);
        }
    }
}
