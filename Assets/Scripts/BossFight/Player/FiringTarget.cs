using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class FiringTarget : MonoBehaviour
{
    [SerializeField, Range(0f, 180f), Tooltip("Maximum aim error in degrees.")]
    private float angularTolerance = 12f;
    [SerializeField, Tooltip("Optional point to aim at. Uses this object's position when empty.")]
    private Transform aimPoint;
    [SerializeField] private bool returnToMainMenuOnHit;

    private Coroutine pendingReturn;
    private BeatTheBoss.Poses.BossScore score;
    private static readonly HashSet<FiringTarget> activeTargets = new();
    internal static IReadOnlyCollection<FiringTarget> ActiveTargets => activeTargets;

    public float AngularTolerance => angularTolerance;
    public Vector3 WorldAimPoint => aimPoint != null ? aimPoint.position : transform.position;

    internal void ConfigureScore(BeatTheBoss.Poses.BossScore value)
    { score = value; returnToMainMenuOnHit = false; }

    internal void ReceiveLaserHit() => ReceiveLaserHit(WorldAimPoint);

    internal void ReceiveLaserHit(Vector3 hitPosition)
    {
        if (!isActiveAndEnabled) return;
        if (score != null) { score.AwardHit(hitPosition); return; }
        if (!isActiveAndEnabled || !returnToMainMenuOnHit || pendingReturn != null)
            return;

        if (SceneHandler.IsTransitioning)
            pendingReturn = StartCoroutine(ReturnAfterTransition());
        else
            SceneHandler.TransitionTo(SceneDestination.MainMenu);
    }

    private IEnumerator ReturnAfterTransition()
    {
        yield return new WaitUntil(() => !SceneHandler.IsTransitioning);
        pendingReturn = null;
        SceneHandler.TransitionTo(SceneDestination.MainMenu);
    }

    private void OnEnable() => activeTargets.Add(this);

    private void OnDisable()
    {
        activeTargets.Remove(this);
        if (pendingReturn != null)
        {
            StopCoroutine(pendingReturn);
            pendingReturn = null;
        }
    }
}
