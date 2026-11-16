using System;
using UnityEngine;

public enum SceneDestination {
    MainMenu,
    Recording,
    Boss
}

public class SceneLoadTrigger : MonoBehaviour
{
    [SerializeField]
    private SceneDestination sceneDestination;

    public void Load() {
        SceneHandler.TransitionTo(sceneDestination);
    }
}
