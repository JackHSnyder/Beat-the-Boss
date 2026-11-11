using System.Collections.Generic;
using UnityEngine;

public class BodyPoseRecorder : MonoBehaviour {
    private List<BodyPose> recordings;
    private List<SavedBodyPose> savedBodyPoses;

    public BodyPoseRecorder() {
        recordings = new List<BodyPose>();
        savedBodyPoses = new List<SavedBodyPose>();
    }

    public void recordCurrentPose() {
        foreach(KeyValuePair<BoneEnum, BodyBone> item in Player.instance.playerBody.getBones()) {
            
        }
    }
}