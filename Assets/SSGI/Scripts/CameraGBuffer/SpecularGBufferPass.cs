using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CameraGBuffer{
    public class SpecularGBufferPass : ScriptableRenderPass{
        private SpecularGBufferSettings mSettings;

        private const string mCommandBufferName = "SpecularGBuffer";
        private ProfilingSampler mProfilingSampler = new ProfilingSampler("SpecularGBuffer");
        private RenderTextureDescriptor mReflectionGBufferDescriptor, mDepthBufferDescriptor;

        List<ShaderTagId> mShaderTagIdList = new List<ShaderTagId>() {
            new ShaderTagId("SpecularGBuffer"),
        };

        private FilteringSettings mFilteringSettings;
        private RenderStateBlock mRenderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);

        private static readonly int mSpecularGBufferID = Shader.PropertyToID("_SpecularGBuffer");

        private RTHandle mSpecularGBuffer, mDepthBuffer;

        private const string mSpecularGBufferName = "_SpecularGBuffer",
            mDepthBufferName = "_SpecularGBufferDepth";

        internal SpecularGBufferPass() {
            mSettings = new SpecularGBufferSettings();
        }

        internal bool Setup(ref SpecularGBufferSettings featureSettings) {
            mSettings = featureSettings;

            mFilteringSettings = new FilteringSettings(RenderQueueRange.opaque, mSettings.LayerMask);

            ConfigureInput(ScriptableRenderPassInput.Color | ScriptableRenderPassInput.Depth);

            return true;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData) {
            var renderer = renderingData.cameraData.renderer;

            // 分配RTHandle
            mReflectionGBufferDescriptor = renderingData.cameraData.cameraTargetDescriptor;
            mReflectionGBufferDescriptor.msaaSamples = 1;
            mReflectionGBufferDescriptor.depthBufferBits = 0;
            mReflectionGBufferDescriptor.colorFormat = RenderTextureFormat.ARGB64;
            RenderingUtils.ReAllocateIfNeeded(ref mSpecularGBuffer, mReflectionGBufferDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: mSpecularGBufferName);

            mDepthBufferDescriptor = renderingData.cameraData.cameraTargetDescriptor;
            mDepthBufferDescriptor.msaaSamples = 1;
            mDepthBufferDescriptor.colorFormat = RenderTextureFormat.Depth;
            RenderingUtils.ReAllocateIfNeeded(ref mDepthBuffer, mDepthBufferDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: mDepthBufferName);
            // 设置Material属性

            // 配置目标和清除
            ConfigureTarget(mSpecularGBuffer, mDepthBuffer);
            ConfigureClear(ClearFlag.All, Color.black);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) {
            var cmd = CommandBufferPool.Get(mCommandBufferName);

            using (new ProfilingScope(cmd, mProfilingSampler)) {
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                var sortFlags = renderingData.cameraData.defaultOpaqueSortFlags;
                var drawSettings = RenderingUtils.CreateDrawingSettings(mShaderTagIdList, ref renderingData, sortFlags);

                var filteringSettings = mFilteringSettings;
                context.DrawRenderers(renderingData.cullResults, ref drawSettings, ref filteringSettings);

                cmd.SetGlobalTexture(mSpecularGBufferID, mSpecularGBuffer);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void OnCameraCleanup(CommandBuffer cmd) {
        }

        public void Dispose() {
            // 释放RTHandle
            mSpecularGBuffer?.Release();
            mSpecularGBuffer = null;

            mDepthBuffer?.Release();
            mDepthBuffer = null;
        }
    }
}