// Draws SceneHandler's destinations as fixed Inspector rows with editable scene references.

using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SceneHandler))]
internal sealed class SceneHandlerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, "scenes");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Scenes", EditorStyles.boldLabel);
        SerializedProperty scenes = serializedObject.FindProperty("scenes");
        for (int i = 0; i < scenes.arraySize; i++)
        {
            SerializedProperty entry = scenes.GetArrayElementAtIndex(i);
            var destination = (SceneDestination)entry.FindPropertyRelative("destination").intValue;
            var label = new GUIContent(ObjectNames.NicifyVariableName(destination.ToString()));
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("scene"), label);
        }

        serializedObject.ApplyModifiedProperties();
    }
}
