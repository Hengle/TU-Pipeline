using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CameraGBuffer{
    public class BaseColorGBufferPass : ScriptableRenderPass{
        private BaseColorGBufferSettings mSettings;

        private const string mCommandBufferName = "BaseColorGBuffer";
        private ProfilingSampler mProfilingSampler = new ProfilingSampler("BaseColorGBuffer");
        private RenderTextureDescriptor mBaseColorGBufferDescriptor, mDepthBufferDescriptor;

        private List<ShaderTagId> mShaderTagIdList = new List<ShaderTagId>() {
            new ShaderTagId("BaseColorGBuffer"),
        };

        private FilteringSettings mFilteringSettings;
        private RenderStateBlock mRenderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);

        private static readonly int mBaseColorGBufferID = Shader.PropertyToID("_BaseColorGBuffer");

        private RTHandle mBaseColorGBuffer, mDepthBuffer;

        private const string mBaseColorGBufferName = "_BaseColorGBuffer",
            mDepthBufferName = "_BaseColorGBufferDepth";

        internal BaseColorGBufferPass() {
            mSettings = new BaseColorGBufferSettings();
        }

        internal bool Setup(ref BaseColorGBufferSettings featureSettings) {
            mSettings = featureSettings;

            mFilteringSettings = new FilteringSettings(RenderQueueRange.opaque, mSettings.LayerMask);

            ConfigureInput(ScriptableRenderPassInput.Color | ScriptableRenderPassInput.Depth);

            return true;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData) {
            var renderer = renderingData.cameraData.renderer;

            // 分配RTHandle
            mBaseColorGBufferDescriptor = renderingData.cameraData.cameraTargetDescriptor;
            mBaseColorGBufferDescriptor.msaaSamples = 1;
            mBaseColorGBufferDescriptor.depthBufferBits = 0;
            mBaseColorGBufferDescriptor.colorFormat = RenderTextureFormat.ARGB64;
            RenderingUtils.ReAllocateIfNeeded(ref mBaseColorGBuffer, mBaseColorGBufferDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: mBaseColorGBufferName);

            mDepthBufferDescriptor = renderingData.cameraData.cameraTargetDescriptor;
            mDepthBufferDescriptor.msaaSamples = 1;
            mDepthBufferDescriptor.colorFormat = RenderTextureFormat.Depth;
            RenderingUtils.ReAllocateIfNeeded(ref mDepthBuffer, mDepthBufferDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: mDepthBufferName);
            // 设置Material属性

            // 配置目标和清除
            ConfigureTarget(mBaseColorGBuffer, mDepthBuffer);
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

                cmd.SetGlobalTexture(mBaseColorGBufferID, mBaseColorGBuffer);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void OnCameraCleanup(CommandBuffer cmd) {
        }

        public void Dispose() {
            // 释放RTHandle
            mBaseColorGBuffer?.Release();
            mBaseColorGBuffer = null;

            mDepthBuffer?.Release();
            mDepthBuffer = null;
        }
    }
}