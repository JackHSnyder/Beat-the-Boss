using UnityEngine;

public class LogoBob : MonoBehaviour {
    [SerializeField]
    private float bobIntensity = 1f, bobSpeed = 1f, bobSteps = .01f;

    private Vector3 startingPos;

    void Start() {
        startingPos = transform.localPosition;
    }

    void Update() {
        float value = Mathf.Sin(Time.time * bobSpeed) * bobIntensity;
        float valueSteps = Mathf.Round(value / bobSteps) * bobSteps;
        
        transform.localPosition = startingPos + new Vector3(0, valueSteps, 0);
    }
}
