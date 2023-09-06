using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FXAA{
    [Serializable]
    internal class FXAASettings{
        // 填当前feature的参数
        // Trims the algorithm from processing darks.
        //   0.0833 - upper limit (default, the start of visible unfiltered edges)
        //   0.0625 - high quality (faster)
        //   0.0312 - visible limit (slower)
        [SerializeField] [Range(0.0312f, 0.0833f)]
        internal float ContrastThreshold = 0.0312f;

        // The minimum amount of local contrast required to apply algorithm.
        //   0.333 - too little (faster)
        //   0.250 - low quality
        //   0.166 - default
        //   0.125 - high quality 
        //   0.063 - overkill (slower)
        [SerializeField] [Range(0.063f, 0.333f)]
        internal float RelativeThreshold = 0.063f;

        // Choose the amount of sub-pixel aliasing removal.
        // This can effect sharpness.
        //   1.00 - upper limit (softer)
        //   0.75 - default amount of filtering
        //   0.50 - lower limit (sharper, less sub-pixel aliasing removal)
        //   0.25 - almost off
        //   0.00 - completely off
        [SerializeField] [Range(0.0f, 1.0f)] internal float SubpixelBlending = 1.0f;

        [SerializeField] internal bool LowQuality = false;
    }


    [DisallowMultipleRendererFeature("FXAA")]
    public class FXAA : ScriptableRendererFeature{
        [SerializeField] private FXAASettings mSettings = new FXAASettings();

        private Shader mShader;
        private const string mShaderName = "Hidden/Anti-Aliasing/FXAA";

        private FXAAPass mRenderPass;
        private Material mMaterial;

        public override void Create() {
            if (mRenderPass == null) {
                mRenderPass = new FXAAPass();
                // 修改注入点
                mRenderPass.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
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
    }

    internal class FXAAPass : ScriptableRenderPass{
        private enum ShaderPass{
            LuminancePrefilter,
            FXAA
        }

        private FXAASettings mSettings;

        private Material mMaterial;

        private ProfilingSampler mProfilingSampler = new ProfilingSampler("FXAA");
        private RenderTextureDescriptor mFXAADescriptor;

        private RTHandle mSourceTexture;
        private RTHandle mDestinationTexture;

        private static readonly int mSourceSizeID = Shader.PropertyToID("_SourceSize"),
            mFXAAParamsID = Shader.PropertyToID("_FXAAParams");

        private const string mLowQualityKeyword = "_LOW_QUALITY";

        private RTHandle mFXAATexture0, mFXAATexture1;
        private const string mFXAATexture0Name = "_FXAATexture0", mFXAATexture1Name = "_FXAATexture1";

        internal FXAAPass() {
            mSettings = new FXAASettings();
        }

        internal bool Setup(ref FXAASettings featureSettings, ref Material material) {
            mMaterial = material;
            mSettings = featureSettings;

            ConfigureInput(ScriptableRenderPassInput.Normal);

            return mMaterial != null;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData) {
            var renderer = renderingData.cameraData.renderer;

            // 设置Material属性
            mMaterial.SetVector(mFXAAParamsID, new Vector4(mSettings.ContrastThreshold, mSettings.RelativeThreshold, mSettings.SubpixelBlending, 0.0f));


            // 设置Material Keyword
            if (mSettings.LowQuality)
                mMaterial.EnableKeyword(mLowQualityKeyword);
            else
                mMaterial.DisableKeyword(mLowQualityKeyword);

            // 分配RTHandle
            mFXAADescriptor = renderingData.cameraData.cameraTargetDescriptor;
            mFXAADescriptor.msaaSamples = 1;
            mFXAADescriptor.depthBufferBits = 0;
            mFXAADescriptor.colorFormat = RenderTextureFormat.ARGB32;
            RenderingUtils.ReAllocateIfNeeded(ref mFXAATexture0, mFXAADescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: mFXAATexture0Name);
            RenderingUtils.ReAllocateIfNeeded(ref mFXAATexture1, mFXAADescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: mFXAATexture1Name);

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
                cmd.SetGlobalVector(mSourceSizeID, new Vector4(mFXAADescriptor.width, mFXAADescriptor.height, 1.0f / mFXAADescriptor.width, 1.0f / mFXAADescriptor.height));

                // Luminance Prefilter
                Blitter.BlitCameraTexture(cmd, mSourceTexture, mFXAATexture0, mMaterial, (int)ShaderPass.LuminancePrefilter);
                Blitter.BlitCameraTexture(cmd, mFXAATexture0, mFXAATexture1, mMaterial, (int)ShaderPass.FXAA);

                Blitter.BlitCameraTexture(cmd, mFXAATexture1, mDestinationTexture);
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
            mFXAATexture0?.Release();
            mFXAATexture1?.Release();
        }
    }
}