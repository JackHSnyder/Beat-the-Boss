using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace BeatTheBoss.Poses
{
    // Encounter score is independent of the timeline cursor, so looping never resets it.
    [DefaultExecutionOrder(500)]
    [RequireComponent(typeof(BossRig))]
    public sealed class BossScore : MonoBehaviour
    {
        [SerializeField, Min(.05f)] private float chestTargetRadius = .3f;
        [SerializeField] private Vector3 chestOffset = new(0, 0, .04f);
        [SerializeField, Min(0)] private float meterHeightAboveHead = .32f;
        [SerializeField, Min(.1f)] private float popupSeconds = 1.4f;
        [SerializeField, Min(0)] private float popupRise = .65f;
        [SerializeField] private UnityEvent onTargetReached = new();

        public long CurrentPoints { get; private set; }
        public int TargetPoints => boss != null ? Mathf.Max(1, boss.targetPoints) : 1000;
        public bool TargetReached { get; private set; }
        public FiringTarget ChestTarget { get; private set; }

        private BossController boss;
        private BossRig rig;
        private Transform meter;
        private TMP_Text count;
        private RectTransform fill;
        private UnityEngine.UI.Image fillImage;
        private ScoreMeterView meterView;
        private SphereCollider hitCollider;
        private readonly List<Popup> popups = new();
        private sealed class Popup
        {
            internal Transform root;
            internal CanvasGroup group;
            internal Vector3 start;
            internal float age;
        }

        private void Start()
        {
            boss = GetComponent<BossController>(); rig = GetComponent<BossRig>();
            if (boss == null || boss.previewMode) { enabled = false; return; }
            // Replace targets owned by individual visuals with one persistent target.
            foreach (var oldTarget in GetComponentsInChildren<FiringTarget>(true)) oldTarget.enabled = false;
            var target = new GameObject("Boss chest firing target");
            target.transform.SetParent(transform, false);
            hitCollider = target.AddComponent<SphereCollider>();
            ChestTarget = target.AddComponent<FiringTarget>();
            ChestTarget.ConfigureScore(this);
            BuildMeter();
            ResetScore();
            PositionTarget();
        }

        public void AwardHit(Vector3 hitPosition)
        {
            if (!isActiveAndEnabled || boss == null || boss.previewMode || count == null) return;
            if (boss.GetComponent<EncounterCompletion>()?.HasEnded == true) return;
            int awarded = Mathf.Max(1, boss.pointsPerHit);
            CurrentPoints = CurrentPoints > long.MaxValue - awarded ? long.MaxValue : CurrentPoints + awarded;
            ShowPopup(awarded, hitPosition);
            UpdateMeter();
            if (!TargetReached && CurrentPoints >= TargetPoints)
            {
                TargetReached = true;
                onTargetReached.Invoke();
            }
        }

        public void ResetScore()
        {
            CurrentPoints = 0; TargetReached = false;
            foreach (var popup in popups) if (popup.root != null) Destroy(popup.root.gameObject);
            popups.Clear();
            UpdateMeter();
        }

        private void BuildMeter()
        {
            meter = WorldCanvas("Boss score meter", new Vector2(420, 112));
            meterView = ScoreMeterView.Create(meter);
            count = meterView.Count; fill = meterView.Fill; fillImage = meterView.FillImage;
        }

        private void UpdateMeter()
        {
            if (count == null) return;
            meterView.SetScore(CurrentPoints, TargetPoints);
        }

        private void PositionTarget()
        {
            if (ChestTarget == null) return;
            Transform chest = rig.Joint("Chest") ?? rig.Joint("Spine");
            ChestTarget.transform.position = chest != null ? chest.TransformPoint(chestOffset) : transform.position + transform.up * 1.25f;
            Vector3 scale = ChestTarget.transform.lossyScale;
            hitCollider.radius = chestTargetRadius / Mathf.Max(.001f, Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        }

        private void LateUpdate()
        {
            if (meter == null) return;
            PositionTarget();
            Transform head = rig.Joint("Head");
            meter.position = (head != null ? head.position : transform.position + Vector3.up * 1.8f) + Vector3.up * meterHeightAboveHead;
            FacePlayer(meter);
            for (int i = popups.Count - 1; i >= 0; i--)
            {
                Popup popup = popups[i]; popup.age += Time.deltaTime;
                float t = Mathf.Clamp01(popup.age / Mathf.Max(.1f, popupSeconds));
                if (t >= 1 || popup.root == null)
                {
                    if (popup.root != null) Destroy(popup.root.gameObject);
                    popups.RemoveAt(i); continue;
                }
                popup.root.position = popup.start + Vector3.up * (popupRise * t);
                popup.group.alpha = 1 - Mathf.SmoothStep(0, 1, t);
                FacePlayer(popup.root);
            }
        }

        private void ShowPopup(int points, Vector3 position)
        {
            if (popups.Count >= 32) { Destroy(popups[0].root.gameObject); popups.RemoveAt(0); }
            Transform root = WorldCanvas("Hit points +" + points, new Vector2(240, 70));
            var group = root.gameObject.AddComponent<CanvasGroup>();
            group.interactable = group.blocksRaycasts = false;
            Label(root, "+" + points.ToString("N0"), Vector2.zero, new Vector2(240, 70), 42, new Color(1, .9f, .3f));
            Vector3 towardPlayer = boss.player != null ? (boss.player.position - position).normalized * .12f : Vector3.zero;
            Vector3 start = position + towardPlayer + Vector3.up * .12f;
            root.position = start; FacePlayer(root);
            popups.Add(new Popup { root = root, group = group, start = start });
        }

        private void FacePlayer(Transform display)
        {
            if (boss.player == null) return;
            Vector3 direction = display.position - boss.player.position;
            if (direction.sqrMagnitude > .0001f) display.rotation = Quaternion.LookRotation(direction, Vector3.up);
        }

        private Transform WorldCanvas(string title, Vector2 size)
        {
            var root = new GameObject(title, typeof(RectTransform), typeof(Canvas));
            root.transform.SetParent(transform, false);
            var rect = (RectTransform)root.transform; rect.sizeDelta = size;
            rect.localScale = Vector3.one * .002f;
            root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            return root.transform;
        }
        private static RectTransform Rect(string title, Transform parent, Vector2 position, Vector2 size)
        {
            var rect = (RectTransform)new GameObject(title, typeof(RectTransform)).transform;
            rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = position; rect.sizeDelta = size; return rect;
        }
        private static TMP_Text Label(Transform parent, string value, Vector2 position, Vector2 size, float fontSize, Color color)
        {
            var rect = Rect(value == "" ? "Score count" : value, parent, position, size);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            var style = MenuButtonStyle.Shared;
            if (style != null && style.font != null) text.font = style.font;
            text.text = value; text.fontSize = fontSize; text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center; text.color = color; text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.enableAutoSizing = true; text.fontSizeMin = 16; text.fontSizeMax = fontSize;
            return text;
        }

        private void OnDisable()
        {
            if (ChestTarget != null) ChestTarget.gameObject.SetActive(false);
            if (meter != null) meter.gameObject.SetActive(false);
            foreach (var popup in popups) if (popup.root != null) Destroy(popup.root.gameObject);
            popups.Clear();
        }
        private void OnEnable()
        {
            if (ChestTarget != null) ChestTarget.gameObject.SetActive(true);
            if (meter != null) meter.gameObject.SetActive(true);
        }
    }
}
