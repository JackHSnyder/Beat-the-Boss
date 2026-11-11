using System.Collections.Generic;

public class BodyPose {
    //private static BoneEnum[] leftHandBones = [];

    private readonly Dictionary<BoneEnum, BodyBone> bones = new Dictionary<BoneEnum, BodyBone>();

    public Dictionary<BoneEnum, BodyBone> getBones() {
        return bones;
    }
}
