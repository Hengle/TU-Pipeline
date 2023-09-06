using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FeatureName{
    [Serializable]
    internal class MotionVectorsSettings{
        // 填当前feature的参数
        [SerializeField] internal bool Camera;
        [SerializeField] internal bool Object;
    }


    [DisallowMultipleRendererFeature("MotionVectors")]
    public class MotionVectors : ScriptableRendererFeature{
        [SerializeField] private MotionVectorsSettings mSettings = new MotionVectorsSettings();

        private Shader mShader;
        private const string mShaderName = "ShaderName";

        private RenderPass mRenderPass;
        private Material mMaterial;


        public override void Create() {
            if (mRenderPass == null) {
                mRenderPass = new RenderPass();
                // 修改注入点
                mRenderPass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            }
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
            if (renderingData.cameraData.postProcessEnabled) {
                if (!GetMaterials()) {
                    Debug.LogErrorFormat("{0}.AddRenderPasses(): Missing material. {1} render pass will not be added.", GetType().Name, name);
                    return;
                }

                bool shouldAdd = mRenderPass.Setup(ref mSettings, ref mMaterial);

                if (shouldAdd)
                    renderer.EnqueuePass(mRenderPass);
            }
        }

        protected override void Dispose(bool disposing) {
            CoreUtils.Destroy(mMaterial);

            mRenderPass?.Dispose();
            mRenderPass = null;
        }

        private bool GetMaterials() {
            if (mShader == null)
                mShader = Shader.Find(mShaderName);
            if (mMaterial == null && mShader != null)
                mMaterial = CoreUtils.CreateEngineMaterial(mShader);
            return mMaterial != null;
        }

        class RenderPass : ScriptableRenderPass{
            private MotionVectorsSettings mSettings;

            private Material mMaterial;

            private ProfilingSampler mProfilingSampler = new ProfilingSampler("ProfilingName");

            private RTHandle mSourceTexture;
            private RTHandle mDestinationTexture;

            internal RenderPass() {
                mSettings = new MotionVectorsSettings();
            }

            internal bool Setup(ref MotionVectorsSettings featureSettings, ref Material material) {
                mMaterial = material;
                mSettings = featureSettings;

                ConfigureInput(ScriptableRenderPassInput.Normal);

                return mMaterial != null;
            }

            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData) {
                var renderer = renderingData.cameraData.renderer;

                // 分配RTHandle
                // 设置Material属性

                // 配置目标和清除
                ConfigureTarget(renderer.cameraColorTargetHandle);
                ConfigureClear(ClearFlag.None, Color.white);
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) {
                if (mMaterial == null) {
                    Debug.LogErrorFormat("{0}.Execute(): Missing material. ScreenSpaceAmbientOcclusion pass will not execute. Check for missing reference in the renderer resources.", GetType().Name);
                    return;
                }

                var cmd = CommandBufferPool.Get();
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                mSourceTexture = renderingData.cameraData.renderer.cameraColorTargetHandle;
                mDestinationTexture = renderingData.cameraData.renderer.cameraColorTargetHandle;

                using (new ProfilingScope(cmd, mProfilingSampler)) {
                    // Blit
                }

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }

            public override void OnCameraCleanup(CommandBuffer cmd) {
                mSourceTexture = null;
                mDestinationTexture = null;
            }

            public void Dispose() {
                // 释放RTHandle
            }
        }
    }
}