using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HBAO{
    [CustomEditor(typeof(HBAO))]
    public class HBAOEditor : Editor{
        #region Serialized Properties

        private SerializedProperty mIntensity;
        private SerializedProperty mRadius;
        private SerializedProperty mMaxRadiusPixels;
        private SerializedProperty mAngleBias;

        #endregion

        private bool mIsIntialized = false;

        private void Init() {
            SerializedProperty settings = serializedObject.FindProperty("mSettings");

            mIntensity = settings.FindPropertyRelative("Intensity");
            mRadius = settings.FindPropertyRelative("Radius");
            mMaxRadiusPixels = settings.FindPropertyRelative("MaxRadiusPixels");
            mAngleBias = settings.FindPropertyRelative("AngleBias");

            mIsIntialized = true;
        }

        public override void OnInspectorGUI() {
            if (!mIsIntialized)
                Init();

            EditorGUILayout.PropertyField(mIntensity, EditorGUIUtility.TrTextContent("Intensity"));
            EditorGUILayout.PropertyField(mRadius,  EditorGUIUtility.TrTextContent("Radius"));
            EditorGUILayout.PropertyField(mMaxRadiusPixels,  EditorGUIUtility.TrTextContent("MaxRadiusPixels"));
            EditorGUILayout.PropertyField(mAngleBias,  EditorGUIUtility.TrTextContent("AngleBias"));
        }
    }
}