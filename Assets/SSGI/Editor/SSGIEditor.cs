using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SSGI{
    [CustomEditor(typeof(SSGI))]
    public class SSGIEditor : Editor{
        #region Serialized Properties

        // SSGI
        private SerializedProperty mSSGISettings;
        private SerializedProperty mIndirectDiffuse;
        private SerializedProperty mSSR;
        private SerializedProperty mAO;
        private SerializedProperty mAntiAliasing;
        private SerializedProperty mCameraGBuffer;

        // CameraGBuffer
        private SerializedProperty mCameraGBufferSettings;
        private SerializedProperty mBaseColorGBuffer;
        private SerializedProperty mSpecularGBuffer;
        private SerializedProperty mReflectionGBuffer;
        private SerializedProperty mBaseColorGBufferSettings;
        private SerializedProperty mSpecularGBufferSettings;
        private SerializedProperty mReflectionGBufferSettings;

        // HiZBuffer
        private SerializedProperty mHiZBufferSettings;

        // ScreenSpaceIndirect
        private SerializedProperty mScreenSpaceIndirectDiffuseSettings;

        // SSR
        private SerializedProperty mSSRType;
        private SerializedProperty mSSRSettings;
        private SerializedProperty mSSRAccelerateType;
        private SerializedProperty mSSSRSettings;
        private SerializedProperty mSSPRSettings;

        // AO
        private SerializedProperty mAOType;
        private SerializedProperty mSSAOSettings;
        private SerializedProperty mHBAOSettings;

        // Anti-Aliasing
        private SerializedProperty mAntiAliasingType;
        private SerializedProperty mFXAASettings;
        private SerializedProperty mTAASettings;

        #endregion

        private bool mIsIntialized = false;

        private bool mSSGIFoldout = true;
        private bool mCameraGBufferFoldout = false;

        private const int mIndentLevel = 1;
        private const int mSpaceSize = 10;

        private void Init() {
            // SSGI
            mSSGISettings = serializedObject.FindProperty("mSSGISettings");
            mIndirectDiffuse = mSSGISettings.FindPropertyRelative("IndirectDiffuse");
            mSSR = mSSGISettings.FindPropertyRelative("SSR");
            mAO = mSSGISettings.FindPropertyRelative("AO");
            mAntiAliasing = mSSGISettings.FindPropertyRelative("AntiAliasing");
            mCameraGBuffer = mSSGISettings.FindPropertyRelative("CameraGBuffer");

            // CameraGBuffer
            mCameraGBufferSettings = serializedObject.FindProperty("mCameraGBufferSettings");
            mBaseColorGBuffer = mCameraGBufferSettings.FindPropertyRelative("BaseColor");
            mSpecularGBuffer = mCameraGBufferSettings.FindPropertyRelative("Specular");
            mReflectionGBuffer = mCameraGBufferSettings.FindPropertyRelative("Reflection");

            mBaseColorGBufferSettings = serializedObject.FindProperty("mBaseColorGBufferSettings");
            mSpecularGBufferSettings = serializedObject.FindProperty("mSpecularGBufferSettings");
            mReflectionGBufferSettings = serializedObject.FindProperty("mReflectionGBufferSettings");

            // ScreenSpaceIndirect
            mScreenSpaceIndirectDiffuseSettings = serializedObject.FindProperty("mScreenSpaceIndirectDiffuseSettings");

            // HiZBuffer
            mHiZBufferSettings = serializedObject.FindProperty("mHiZBufferSettings");

            // SSR
            mSSRType = serializedObject.FindProperty("mSSRType");
            mSSRSettings = serializedObject.FindProperty("mSSRSettings");
            mSSRAccelerateType = mSSRSettings.FindPropertyRelative("AccelerateType");
            mSSSRSettings = serializedObject.FindProperty("mSSSRSettings");
            mSSPRSettings = serializedObject.FindProperty("mSSPRSettings");

            // AO
            mAOType = serializedObject.FindProperty("mAOType");
            mSSAOSettings = serializedObject.FindProperty("mSSAOSettings");
            mHBAOSettings = serializedObject.FindProperty("mHBAOSettings");

            // Anti-Aliasing
            mAntiAliasingType = serializedObject.FindProperty("mAntiAliasingType");
            mFXAASettings = serializedObject.FindProperty("mFXAASettings");
            mTAASettings = serializedObject.FindProperty("mTAASettings");

            mIsIntialized = true;
        }

        public override void OnInspectorGUI() {
            if (!mIsIntialized)
                Init();

            DoSSGI();
            DoScreenSpaceIndirectDiffuse();
            DoSSR();
            DoAO();
            DoAntiAliasing();
            DoHiZBuffer();
            DoCameraGBuffer();
        }

        private void DoSSGI() {
            mSSGIFoldout = EditorGUILayout.Foldout(mSSGIFoldout, EditorGUIUtility.TrTextContent("SSGI"), true);
            if (mSSGIFoldout) {
                EditorGUI.indentLevel += mIndentLevel;

                EditorGUILayout.PropertyField(mIndirectDiffuse, EditorGUIUtility.TrTextContent("Indirect Diffuse"));

                EditorGUILayout.PropertyField(mSSR, EditorGUIUtility.TrTextContent("SSR"));
                if (mSSR.boolValue) {
                    EditorGUI.indentLevel += mIndentLevel;
                    EditorGUILayout.PropertyField(mSSRType, EditorGUIUtility.TrTextContent("Type"));
                    EditorGUI.indentLevel -= mIndentLevel;
                }

                EditorGUILayout.PropertyField(mAO, EditorGUIUtility.TrTextContent("AO"));
                if (mAO.boolValue) {
                    EditorGUI.indentLevel += mIndentLevel;
                    EditorGUILayout.PropertyField(mAOType, EditorGUIUtility.TrTextContent("Type"));
                    EditorGUI.indentLevel -= mIndentLevel;
                }

                EditorGUILayout.PropertyField(mAntiAliasing, EditorGUIUtility.TrTextContent("Anti-Aliasing"));
                if (mAntiAliasing.boolValue) {
                    EditorGUI.indentLevel += mIndentLevel;
                    EditorGUILayout.PropertyField(mAntiAliasingType, EditorGUIUtility.TrTextContent("Type"));
                    EditorGUI.indentLevel -= mIndentLevel;
                }

                EditorGUILayout.PropertyField(mCameraGBuffer, EditorGUIUtility.TrTextContent("Camera GBuffer"));

                EditorGUI.indentLevel -= mIndentLevel;
            }

            EditorGUILayout.Space(mSpaceSize);
            GUILayout.Button("", GUILayout.Height(2));
            EditorGUILayout.Space(mSpaceSize);
        }


        private void DoScreenSpaceIndirectDiffuse() {
            if (mIndirectDiffuse.boolValue) {
                EditorGUILayout.PropertyField(mScreenSpaceIndirectDiffuseSettings, EditorGUIUtility.TrTextContent("Indirect Diffuse"));
                EditorGUILayout.Space(mSpaceSize);
                GUILayout.Button("", GUILayout.Height(2));
                EditorGUILayout.Space(mSpaceSize);
            }
        }

        private void DoSSR() {
            if (mSSR.boolValue) {
                switch (mSSRType.intValue) {
                    case (int)SSRType.SSR:
                        EditorGUILayout.PropertyField(mSSRSettings, EditorGUIUtility.TrTextContent("SSR"));
                        break;

                    case (int)SSRType.StochasticSSR:
                        EditorGUILayout.PropertyField(mSSSRSettings, EditorGUIUtility.TrTextContent("Stochastic SSR"));
                        break;

                    case (int)SSRType.SSPR:
                        EditorGUILayout.PropertyField(mSSPRSettings, EditorGUIUtility.TrTextContent("SSPR"));
                        break;
                }

                EditorGUILayout.Space(mSpaceSize);
                GUILayout.Button("", GUILayout.Height(2));
                EditorGUILayout.Space(mSpaceSize);
            }
        }

        private void DoAO() {
            if (mAO.boolValue) {
                switch (mAOType.intValue) {
                    case (int)AOType.SSAO:
                        EditorGUILayout.PropertyField(mSSAOSettings, EditorGUIUtility.TrTextContent("SSAO"));
                        break;

                    case (int)AOType.HBAO:
                        EditorGUILayout.PropertyField(mHBAOSettings, EditorGUIUtility.TrTextContent("HBAO"));
                        break;
                }

                EditorGUILayout.Space(mSpaceSize);
                GUILayout.Button("", GUILayout.Height(2));
                EditorGUILayout.Space(mSpaceSize);
            }
        }

        private void DoAntiAliasing() {
            if (mAntiAliasing.boolValue) {
                switch (mAntiAliasingType.intValue) {
                    case (int)AntiAliasingType.FXAA:
                        EditorGUILayout.PropertyField(mFXAASettings, EditorGUIUtility.TrTextContent("FXAA"));
                        break;

                    case (int)AntiAliasingType.TAA:
                        EditorGUILayout.PropertyField(mTAASettings, EditorGUIUtility.TrTextContent("TAA"));
                        break;
                }

                EditorGUILayout.Space(mSpaceSize);
                GUILayout.Button("", GUILayout.Height(2));
                EditorGUILayout.Space(mSpaceSize);
            }
        }

        private void DoCameraGBuffer() {
            if (mCameraGBuffer.boolValue) {
                mCameraGBufferFoldout = EditorGUILayout.Foldout(mCameraGBufferFoldout, EditorGUIUtility.TrTextContent("Camera GBuffers"), true);
                if (mCameraGBufferFoldout) {
                    EditorGUI.indentLevel += mIndentLevel;
                    EditorGUILayout.PropertyField(mBaseColorGBuffer, EditorGUIUtility.TrTextContent("Base Color"));

                    if (mBaseColorGBuffer.boolValue) {
                        EditorGUI.indentLevel += mIndentLevel;
                        EditorGUILayout.PropertyField(mBaseColorGBufferSettings, EditorGUIUtility.TrTextContent("Settings"));
                        EditorGUI.indentLevel -= mIndentLevel;
                    }

                    EditorGUILayout.PropertyField(mSpecularGBuffer, EditorGUIUtility.TrTextContent("Specular"));
                    if (mSpecularGBuffer.boolValue) {
                        EditorGUI.indentLevel += mIndentLevel;
                        EditorGUILayout.PropertyField(mSpecularGBufferSettings, EditorGUIUtility.TrTextContent("Settings"));
                        EditorGUI.indentLevel -= mIndentLevel;
                    }


                    EditorGUILayout.PropertyField(mReflectionGBuffer, EditorGUIUtility.TrTextContent("Reflection"));
                    if (mReflectionGBuffer.boolValue) {
                        EditorGUI.indentLevel += mIndentLevel;
                        EditorGUILayout.PropertyField(mReflectionGBufferSettings, EditorGUIUtility.TrTextContent("Settings"));
                        EditorGUI.indentLevel -= mIndentLevel;
                    }

                    EditorGUI.indentLevel -= mIndentLevel;
                }

                EditorGUILayout.Space(mSpaceSize);
                GUILayout.Button("", GUILayout.Height(2));
                EditorGUILayout.Space(mSpaceSize);
            }
        }


        private void DoHiZBuffer() {
            if (!ShouldAddHizPass()) return;
            EditorGUILayout.PropertyField(mHiZBufferSettings, EditorGUIUtility.TrTextContent("Hierarchical ZBuffer"));
            EditorGUILayout.Space(mSpaceSize);
            GUILayout.Button("", GUILayout.Height(2));
            EditorGUILayout.Space(mSpaceSize);
        }

        private bool ShouldAddHizPass() => (mSSR.boolValue &&
                                            ((mSSRType.intValue == (int)SSRType.SSR && mSSRAccelerateType.intValue == (int)SSRAccelerateType.HierarchicalZBuffer) ||
                                             (mSSRType.intValue == (int)SSRType.StochasticSSR))) ||
                                           (mIndirectDiffuse.boolValue);
    }
}