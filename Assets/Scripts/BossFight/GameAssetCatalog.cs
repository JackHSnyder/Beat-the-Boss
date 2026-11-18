using System;
using UnityEngine;

namespace BeatTheBoss.Poses
{
    // Build-time references only. Level JSON stores the stable IDs, never these Unity objects.
    [CreateAssetMenu(menuName = "Beat the Boss/Game asset catalog")]
    public sealed class GameAssetCatalog : ScriptableObject
    {
        [Serializable] public sealed class Song { public string id, title; public AudioClip clip; }
        [Serializable] public sealed class Character { public string id, title; public GameObject prefab; public bool protectedAsset; }
        public Song[] songs = Array.Empty<Song>();
        public Character[] characters = Array.Empty<Character>();
    }
}
