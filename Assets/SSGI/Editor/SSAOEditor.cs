using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SSAO{
    [CustomEditor(typeof(SSAO))]
    public class SSAOEditor : Editor{
        #region Serialized Properties

        private SerializedProperty mIntensity;
        private SerializedProperty mRadius;
        private SerializedProperty mFalloff;

        #endregion

        private bool mIsIntialized = false;

        private void Init() {
            SerializedProperty settings = serializedObject.FindProperty("mSettings");

            mIntensity = settings.FindPropertyRelative("Intensity");
            mRadius = settings.FindPropertyRelative("Radius");
            mFalloff = settings.FindPropertyRelative("Falloff");

            mIsIntialized = true;
        }

        public override void OnInspectorGUI() {
            if (!mIsIntialized)
                Init();

            EditorGUILayout.PropertyField(mIntensity, EditorGUIUtility.TrTextContent("Intensity"));
            EditorGUILayout.PropertyField(mRadius,  EditorGUIUtility.TrTextContent("Radius"));
            EditorGUILayout.PropertyField(mFalloff,  EditorGUIUtility.TrTextContent("Falloff"));
        }
    }
}