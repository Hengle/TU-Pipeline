using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FXAA{
    [CustomEditor(typeof(FXAA))]
    public class FXAAEditor : Editor{
        #region Serialized Properties

        private SerializedProperty mContrastThreshold;
        private SerializedProperty mRelativeThreshold;
        private SerializedProperty mSubpixelBlending;
        private SerializedProperty mLowQuality;

        #endregion

        private bool mIsIntialized = false;

        private void Init() {
            SerializedProperty settings = serializedObject.FindProperty("mSettings");

            mContrastThreshold = settings.FindPropertyRelative("ContrastThreshold");
            mRelativeThreshold = settings.FindPropertyRelative("RelativeThreshold");
            mSubpixelBlending = settings.FindPropertyRelative("SubpixelBlending");
            mLowQuality = settings.FindPropertyRelative("LowQuality");

            mIsIntialized = true;
        }

        public override void OnInspectorGUI() {
            if (!mIsIntialized)
                Init();

            EditorGUILayout.PropertyField(mContrastThreshold, EditorGUIUtility.TrTextContent("ContrastThreshold"));
            EditorGUILayout.PropertyField(mRelativeThreshold,  EditorGUIUtility.TrTextContent("RelativeThreshold"));
            EditorGUILayout.PropertyField(mSubpixelBlending,  EditorGUIUtility.TrTextContent("SubpixelBlending"));
            EditorGUILayout.PropertyField(mLowQuality,  EditorGUIUtility.TrTextContent("LowQuality"));
        }
    }
}