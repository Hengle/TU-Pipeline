using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CameraGBuffer{
    public class ReflectionGBufferPass : ScriptableRenderPass{
        private ReflectionGBufferSettings mSettings;

        private const string mCommandBufferName = "ReflectionGBuffer";
        private ProfilingSampler mProfilingSampler = new ProfilingSampler("ReflectionGBuffer");
        private RenderTextureDescriptor mReflectionGBufferDescriptor, mDepthBufferDescriptor;

        List<ShaderTagId> mShaderTagIdList = new List<ShaderTagId>() {
            new ShaderTagId("ReflectionGBuffer"),
        };

        private FilteringSettings mFilteringSettings;
        private RenderStateBlock mRenderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);

        private static readonly int mReflectionGBufferID = Shader.PropertyToID("_ReflectionGBuffer");

        private RTHandle mReflectionGBuffer, mDepthBuffer;

        private const string mSpecularGBufferName = "_ReflectionGBuffer",
            mDepthBufferName = "_ReflectionGBufferDepth";

        internal ReflectionGBufferPass() {
            mSettings = new ReflectionGBufferSettings();
        }

        internal bool Setup(ref ReflectionGBufferSettings featureSettings) {
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
            RenderingUtils.ReAllocateIfNeeded(ref mReflectionGBuffer, mReflectionGBufferDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: mSpecularGBufferName);

            mDepthBufferDescriptor = renderingData.cameraData.cameraTargetDescriptor;
            mDepthBufferDescriptor.msaaSamples = 1;
            mDepthBufferDescriptor.colorFormat = RenderTextureFormat.Depth;
            RenderingUtils.ReAllocateIfNeeded(ref mDepthBuffer, mDepthBufferDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: mDepthBufferName);
            // 设置Material属性

            // 配置目标和清除
            ConfigureTarget(mReflectionGBuffer, mDepthBuffer);
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

                cmd.SetGlobalTexture(mReflectionGBufferID, mReflectionGBuffer);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void OnCameraCleanup(CommandBuffer cmd) {
        }

        public void Dispose() {
            // 释放RTHandle
            mReflectionGBuffer?.Release();
            mReflectionGBuffer = null;

            mDepthBuffer?.Release();
            mDepthBuffer = null;
        }
    }
}