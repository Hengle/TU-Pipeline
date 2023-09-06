using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SSGI{
    [Serializable]
    internal class HierarchicalZBufferSettings{
        [SerializeField] [Range(4, 10)] internal int MipCount = 10;
    }

    class HierarchicalZBufferPass : ScriptableRenderPass{
        private HierarchicalZBufferSettings mSettings;

        private Material mMaterial;

        private ProfilingSampler mProfilingSampler = new ProfilingSampler("Pre-HierarchicalZBuffer");

        private RTHandle mCameraColorTexture;
        private RTHandle mCameraDepthTexture;
        private RTHandle mDestinationTexture;

        private static readonly int mHiZBufferTextureID = Shader.PropertyToID("_HierarchicalZBufferTexture"),
            mHiZBufferFromMiplevelID = Shader.PropertyToID("_HierarchicalZBufferTextureFromMipLevel"),
            mHiZBufferToMiplevelID = Shader.PropertyToID("_HierarchicalZBufferTextureToMipLevel"),
            mSourceSizeID = Shader.PropertyToID("_SourceSize"),
            mMaxHiZBufferTextureipLevelID = Shader.PropertyToID("_MaxHierarchicalZBufferTextureMipLevel");

        private const string mHiZBufferTextureName = "_HierarchicalZBufferTexture",
            mTemporaryTextureName = "_TemporaryHierarchicalZBufferTexture";

        private RTHandle[] mHiZBufferTextures = new RTHandle[10];
        private RTHandle mTemporaryTexture, mHiZBufferTexture;

        private RenderTextureDescriptor[] mHiZBufferDescriptors = new RenderTextureDescriptor[10];
        private RenderTextureDescriptor mHiZBufferDescriptor;

        internal HierarchicalZBufferPass() {
            mSettings = new HierarchicalZBufferSettings();
        }

        internal bool Setup(ref HierarchicalZBufferSettings featureSettings, ref Material material) {
            mMaterial = material;
            mSettings = featureSettings;

            ConfigureInput(ScriptableRenderPassInput.Depth);

            return mMaterial != null;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData) {
            var renderer = renderingData.cameraData.renderer;

            // 分配RTHandle
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            // 把高和宽变换为2的整次幂 然后除以2
            var width = Math.Max((int)Math.Ceiling(Mathf.Log(desc.width, 2) - 1.0f), 1);
            var height = Math.Max((int)Math.Ceiling(Mathf.Log(desc.height, 2) - 1.0f), 1);
            width = 1 << width;
            height = 1 << height;

            width = Math.Max(width, (int)Math.Pow(2, mSettings.MipCount));
            height = Math.Max(height, (int)Math.Pow(2, mSettings.MipCount));

            mHiZBufferDescriptor = new RenderTextureDescriptor(width, height, RenderTextureFormat.RFloat, 0, mSettings.MipCount);
            mHiZBufferDescriptor.msaaSamples = 1;
            mHiZBufferDescriptor.useMipMap = true;
            mHiZBufferDescriptor.sRGB = false; // linear
            RenderingUtils.ReAllocateIfNeeded(ref mHiZBufferTexture, mHiZBufferDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: mHiZBufferTextureName);

            for (int i = 0; i < mSettings.MipCount; i++) {
                mHiZBufferDescriptors[i] = new RenderTextureDescriptor(width, height, RenderTextureFormat.RFloat, 0, 1);
                mHiZBufferDescriptors[i].msaaSamples = 1;
                mHiZBufferDescriptors[i].useMipMap = false;
                mHiZBufferDescriptors[i].sRGB = false; // linear
                RenderingUtils.ReAllocateIfNeeded(ref mHiZBufferTextures[i], mHiZBufferDescriptors[i], FilterMode.Bilinear, TextureWrapMode.Clamp, name: mHiZBufferTextureName + i);
                // generate mipmap
                width = Math.Max(width / 2, 1);
                height = Math.Max(height / 2, 1);
            }
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) {
            if (mMaterial == null) {
                Debug.LogErrorFormat("{0}.Execute(): Missing material. ScreenSpaceAmbientOcclusion pass will not execute. Check for missing reference in the renderer resources.", GetType().Name);
                return;
            }

            var cmd = CommandBufferPool.Get();
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            mCameraColorTexture = renderingData.cameraData.renderer.cameraColorTargetHandle;
            mCameraDepthTexture = renderingData.cameraData.renderer.cameraDepthTargetHandle;
            mDestinationTexture = renderingData.cameraData.renderer.cameraColorTargetHandle;

            using (new ProfilingScope(cmd, mProfilingSampler)) {
                // mip 0
                Blitter.BlitCameraTexture(cmd, mCameraDepthTexture, mHiZBufferTextures[0]);
                cmd.CopyTexture(mHiZBufferTextures[0], 0, 0, mHiZBufferTexture, 0, 0);

                // mip 1~max
                for (int i = 1; i < mSettings.MipCount; i++) {
                    cmd.SetGlobalFloat(mHiZBufferFromMiplevelID, i - 1);
                    cmd.SetGlobalFloat(mHiZBufferToMiplevelID, i);
                    cmd.SetGlobalVector(mSourceSizeID, new Vector4(mHiZBufferDescriptors[i - 1].width, mHiZBufferDescriptors[i - 1].height, 1.0f / mHiZBufferDescriptors[i - 1].width, 1.0f / mHiZBufferDescriptors[i - 1].height));
                    Blitter.BlitCameraTexture(cmd, mHiZBufferTextures[i - 1], mHiZBufferTextures[i], mMaterial, 0);

                    cmd.CopyTexture(mHiZBufferTextures[i], 0, 0, mHiZBufferTexture, 0, i);
                }

                // set global hiz texture
                cmd.SetGlobalFloat(mMaxHiZBufferTextureipLevelID, mSettings.MipCount - 1);
                cmd.SetGlobalTexture(mHiZBufferTextureID, mHiZBufferTexture);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void OnCameraCleanup(CommandBuffer cmd) {
            mCameraColorTexture = null;
            mDestinationTexture = null;
        }

        public void Dispose() {
            // 释放RTHandle
            for (int i = 0; i < 10; i++) {
                mHiZBufferTextures[i]?.Release();
                mHiZBufferTextures[i] = null;
            }
        }
    }
}