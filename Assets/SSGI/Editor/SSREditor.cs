using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SSGI{
    [CustomEditor(typeof(SSR))]
    public class SSREditor : Editor{
        #region Serialized Properties

        private SerializedProperty mIntensity;
        private SerializedProperty mAccelerateType;
        private SerializedProperty mMaxDistance;
        private SerializedProperty mStride;
        private SerializedProperty mStepCount;
        private SerializedProperty mThickness;
        private SerializedProperty mBinaryCount;
        private SerializedProperty mJitterDither;
        private SerializedProperty mBlendMode;
        private SerializedProperty mBlurRadius;
        private SerializedProperty mMipCount;

        #endregion

        private bool mIsIntialized = false;

        private void Init() {
            SerializedProperty ssrSettings = serializedObject.FindProperty("mSSRSettings");
            SerializedProperty hizSettings = serializedObject.FindProperty("mHiZSettings");

            mIntensity = ssrSettings.FindPropertyRelative("Intensity");
            mAccelerateType = ssrSettings.FindPropertyRelative("AccelerateType");
            mMaxDistance = ssrSettings.FindPropertyRelative("MaxDistance");
            mStride = ssrSettings.FindPropertyRelative("Stride");
            mStepCount = ssrSettings.FindPropertyRelative("StepCount");
            mThickness = ssrSettings.FindPropertyRelative("Thickness");
            mBinaryCount = ssrSettings.FindPropertyRelative("BinaryCount");
            mJitterDither = ssrSettings.FindPropertyRelative("JitterDither");
            mBlendMode = ssrSettings.FindPropertyRelative("BlendMode");
            mBlurRadius = ssrSettings.FindPropertyRelative("BlurRadius");

            mMipCount = hizSettings.FindPropertyRelative("MipCount");

            mIsIntialized = true;
        }

        public override void OnInspectorGUI() {
            if (!mIsIntialized)
                Init();

            bool HiZ = mAccelerateType.intValue == (int)SSRAccelerateType.HierarchicalZBuffer;

            EditorGUILayout.PropertyField(mIntensity, EditorGUIUtility.TrTextContent("Intensity"));
            EditorGUILayout.PropertyField(mAccelerateType, EditorGUIUtility.TrTextContent("AccelerateType"));
            EditorGUILayout.PropertyField(mMaxDistance, EditorGUIUtility.TrTextContent("MaxDistance"));
            EditorGUILayout.PropertyField(mStride, EditorGUIUtility.TrTextContent("Stride"));
            EditorGUILayout.PropertyField(mStepCount, EditorGUIUtility.TrTextContent("StepCount"));
            EditorGUILayout.PropertyField(mThickness, EditorGUIUtility.TrTextContent("Thickness"));
            if (!HiZ) EditorGUILayout.PropertyField(mBinaryCount, EditorGUIUtility.TrTextContent("BinaryCount"));
            EditorGUILayout.PropertyField(mJitterDither, EditorGUIUtility.TrTextContent("JitterDither"));
            EditorGUILayout.PropertyField(mBlendMode, EditorGUIUtility.TrTextContent("BlendMode"));
            EditorGUILayout.PropertyField(mBlurRadius, EditorGUIUtility.TrTextContent("BlurRadius"));
            
            if(HiZ) EditorGUILayout.PropertyField(mMipCount, EditorGUIUtility.TrTextContent("HiZMipCount"));
        }
    }
}