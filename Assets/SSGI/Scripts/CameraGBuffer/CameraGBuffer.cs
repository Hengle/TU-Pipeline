using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CameraGBuffer{
    [Serializable]
    internal class CameraGBufferSettings{
        [SerializeField] internal bool BaseColor = true;
        [SerializeField] internal bool Specular = true;
        [SerializeField] internal bool Reflection = true;
    }

    [Serializable]
    internal class SpecularGBufferSettings{
        [SerializeField] internal LayerMask LayerMask;
    }

    [Serializable]
    internal class ReflectionGBufferSettings{
        [SerializeField] internal LayerMask LayerMask;
    }

    [Serializable]
    internal class BaseColorGBufferSettings{
        [SerializeField] internal LayerMask LayerMask;
    }

    [DisallowMultipleRendererFeature("CameraGBuffer")]
    public class CameraGBuffer : ScriptableRendererFeature{
        [SerializeField] private CameraGBufferSettings mCameraGBufferSettings = new CameraGBufferSettings();
        [SerializeField] private SpecularGBufferSettings mSpecularGBufferSettings = new SpecularGBufferSettings();
        [SerializeField] private ReflectionGBufferSettings mReflectionGBufferSettings = new ReflectionGBufferSettings();
        [SerializeField] private BaseColorGBufferSettings mBaseColorGBufferSettings = new BaseColorGBufferSettings();

        private SpecularGBufferPass mSpecularGBufferPass;
        private ReflectionGBufferPass mReflectionGBufferPass;
        private BaseColorGBufferPass mBaseColorGBufferPass;

        public override void Create() {
            if (mBaseColorGBufferPass == null) {
                mBaseColorGBufferPass = new BaseColorGBufferPass();
                mBaseColorGBufferPass.renderPassEvent = RenderPassEvent.AfterRenderingPrePasses;
            }

            if (mSpecularGBufferPass == null) {
                mSpecularGBufferPass = new SpecularGBufferPass();
                // 修改注入点
                mSpecularGBufferPass.renderPassEvent = RenderPassEvent.AfterRenderingPrePasses;
            }

            if (mReflectionGBufferPass == null) {
                mReflectionGBufferPass = new ReflectionGBufferPass();
                mReflectionGBufferPass.renderPassEvent = RenderPassEvent.AfterRenderingPrePasses;
            }
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
            if (renderingData.cameraData.postProcessEnabled) {
                if (mCameraGBufferSettings.BaseColor && mBaseColorGBufferPass.Setup(ref mBaseColorGBufferSettings))
                    renderer.EnqueuePass(mBaseColorGBufferPass);

                if (mCameraGBufferSettings.Specular && mSpecularGBufferPass.Setup(ref mSpecularGBufferSettings))
                    renderer.EnqueuePass(mSpecularGBufferPass);

                if (mCameraGBufferSettings.Reflection && mReflectionGBufferPass.Setup(ref mReflectionGBufferSettings))
                    renderer.EnqueuePass(mReflectionGBufferPass);
            }
        }

        protected override void Dispose(bool disposing) {
            mBaseColorGBufferPass?.Dispose();
            mBaseColorGBufferPass = null;

            mSpecularGBufferPass?.Dispose();
            mSpecularGBufferPass = null;

            mReflectionGBufferPass?.Dispose();
            mReflectionGBufferPass = null;
        }
    }
}