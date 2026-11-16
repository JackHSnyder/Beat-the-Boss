using System.Collections.Generic;
using UnityEngine;

public sealed class FiringTarget : MonoBehaviour
{
    [SerializeField, Range(0f, 180f), Tooltip("Maximum aim error in degrees.")]
    private float angularTolerance = 12f;
    [SerializeField, Tooltip("Optional point to aim at. Uses this object's position when empty.")]
    private Transform aimPoint;

    private static readonly HashSet<FiringTarget> activeTargets = new();
    internal static IReadOnlyCollection<FiringTarget> ActiveTargets => activeTargets;

    public float AngularTolerance => angularTolerance;
    public Vector3 WorldAimPoint => aimPoint != null ? aimPoint.position : transform.position;

    private void OnEnable() => activeTargets.Add(this);
    private void OnDisable() => activeTargets.Remove(this);
}
