using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SSGI{
    internal static class Jitter{
        static internal float GetHalton(int index, int radix) {
            float result = 0.0f;
            float fraction = 1.0f / radix;
            while (index > 0) {
                result += (index % radix) * fraction;

                index /= radix;
                fraction /= radix;
            }

            return result;
        }

        // get [-0.5, 0.5] jitter vector2
        static internal Vector2 CalculateJitter(int frameIndex) {
            float jitterX = GetHalton((frameIndex & 1023) + 1, 2) - 0.5f;
            float jitterY = GetHalton((frameIndex & 1023) + 1, 3) - 0.5f;

            return new Vector2(jitterX, jitterY);
        }

        static internal Matrix4x4 CalculateJitterProjectionMatrix(ref CameraData cameraData, float jitterScale = 1.0f) {
            Matrix4x4 mat = cameraData.GetProjectionMatrix();

            int taaFrameIndex = Time.frameCount;

            float actualWidth = cameraData.camera.pixelWidth;
            float actualHeight = cameraData.camera.pixelHeight;

            Vector2 jitter = CalculateJitter(taaFrameIndex) * jitterScale;

            mat.m02 += jitter.x * (2.0f / actualWidth);
            mat.m12 += jitter.y * (2.0f / actualHeight);

            return mat;
        }
    }

    internal enum MotionType{
        Static,
        CameraOnly,
        Object
    }

    [Serializable]
    internal class TAASettings{
        // 填当前feature的参数
        [SerializeField] internal float JitterScale = 1.0f;
        [SerializeField] internal float FrameInfluence = 0.05f;
        [SerializeField] internal MotionType motionType;
    }


    [DisallowMultipleRendererFeature("TAA")]
    public class TAA : ScriptableRendererFeature{
        [SerializeField] private TAASettings mSettings = new TAASettings();

        private Shader mTaaShader;

        private const string mTaaShaderName = "Hidden/Anti-Aliasing/TAA";

        private TAAPass mTaaPass;
        private JitterPass mJitterPass;
        private Material mTaaMaterial;

        public override void Create() {
            if (mJitterPass == null) {
                mJitterPass = new JitterPass();
                // 在渲染场景前抖动相机
                mJitterPass.renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
            }

            if (mTaaPass == null) {
                mTaaPass = new TAAPass();
                // 修改注入点 这里先不考虑tonemapping和bloom等
                mTaaPass.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            }
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
            // if (renderingData.cameraData.camera.cameraType == CameraType.Game) {
            if (renderingData.cameraData.postProcessEnabled) {
                if (!GetMaterials()) {
                    Debug.LogErrorFormat("{0}.AddRenderPasses(): Missing material. {1} render pass will not be added.", GetType().Name, name);
                    return;
                }

                bool shouldAdd = mJitterPass.Setup(ref mSettings) && mTaaPass.Setup(ref mSettings, ref mTaaMaterial);

                if (shouldAdd) {
                    renderer.EnqueuePass(mJitterPass);
                    renderer.EnqueuePass(mTaaPass);
                }
            }
        }

        protected override void Dispose(bool disposing) {
            CoreUtils.Destroy(mTaaMaterial);

            mJitterPass?.Dispose();

            mTaaPass?.Dispose();
            mTaaPass = null;
        }

        private bool GetMaterials() {
            if (mTaaShader == null)
                mTaaShader = Shader.Find(mTaaShaderName);
            if (mTaaMaterial == null && mTaaShader != null)
                mTaaMaterial = CoreUtils.CreateEngineMaterial(mTaaShader);

            return mTaaMaterial != null;
        }
    }

    internal class TAAPass : ScriptableRenderPass{
        private TAASettings mSettings;

        private Material mTaaMaterial;
        private Material mCameraMotionVectorsMaterial;

        private ProfilingSampler mProfilingSampler = new ProfilingSampler("TAA");
        private RenderTextureDescriptor mTAADescriptor;

        private RTHandle mSourceTexture;
        private RTHandle mDestinationTexture;

        private const string mMotionCameraKeyword = "_MOTION_CAMERA",
            mMotionObjectKeyword = "_MOTION_OBJECT";

        private static readonly int mTaaAccumulationTexID = Shader.PropertyToID("_TaaAccumulationTexture"),
            mPrevViewProjMatrixID = Shader.PropertyToID("_LastViewProjMatrix"),
            mViewProjMatrixWithoutJitterID = Shader.PropertyToID("_ViewProjMatrixWithoutJitter"),
            mFrameInfluenceID = Shader.PropertyToID("_FrameInfluence");

        private const string mAccumulationTextureName = "_TaaAccumulationTexture",
            mTaaTemporaryTextureName = "_TaaTemporaryTexture";

        private RTHandle mAccumulationTexture;

        private RTHandle mTaaTemporaryTexture;

        private bool mResetHistoryFrames;

        private Matrix4x4 mPrevViewProjMatrix, mViewProjMatrix;


        internal TAAPass() {
            mSettings = new TAASettings();
        }

        internal bool Setup(ref TAASettings featureSettings, ref Material taaMaterial) {
            mTaaMaterial = taaMaterial;
            mSettings = featureSettings;

            ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Motion);

            return mTaaMaterial != null;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData) {
            renderingData.cameraData.camera.depthTextureMode |= DepthTextureMode.Depth | DepthTextureMode.MotionVectors;

            mTAADescriptor = renderingData.cameraData.cameraTargetDescriptor;
            mTAADescriptor.msaaSamples = 1;
            mTAADescriptor.depthBufferBits = 0;

            // 设置Material属性
            mTaaMaterial.SetVector("_SourceSize", new Vector4(mTAADescriptor.width, mTAADescriptor.height, 1.0f / mTAADescriptor.width, 1.0f / mTAADescriptor.height));

            // 分配RTHandle
            mResetHistoryFrames = RenderingUtils.ReAllocateIfNeeded(ref mAccumulationTexture, mTAADescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: mAccumulationTextureName);

            var cameraData = renderingData.cameraData;
            if (mResetHistoryFrames) {
                // 初始化上一帧的vp矩阵
                mPrevViewProjMatrix = cameraData.GetProjectionMatrix() * cameraData.GetViewMatrix();
            }

            RenderingUtils.ReAllocateIfNeeded(ref mTaaTemporaryTexture, mTAADescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: mTaaTemporaryTextureName);

            // 配置目标和清除
            var renderer = renderingData.cameraData.renderer;
            // ConfigureTarget(renderer.cameraColorTargetHandle);
            // ConfigureClear(ClearFlag.None, Color.white);

            // 计算本帧没有jitter的vp矩阵
            mViewProjMatrix = cameraData.GetProjectionMatrix() * cameraData.GetViewMatrix();

            // 设置Material属性

            // 设置Keyword
            Setkeyword(mMotionCameraKeyword, mSettings.motionType == MotionType.CameraOnly);
            Setkeyword(mMotionObjectKeyword, mSettings.motionType == MotionType.Object);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) {
            if (mTaaMaterial == null) {
                Debug.LogErrorFormat("{0}.Execute(): Missing material. ScreenSpaceAmbientOcclusion pass will not execute. Check for missing reference in the renderer resources.", GetType().Name);
                return;
            }

            var cmd = CommandBufferPool.Get();
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            mSourceTexture = renderingData.cameraData.renderer.cameraColorTargetHandle;
            mDestinationTexture = renderingData.cameraData.renderer.cameraColorTargetHandle;

            using (new ProfilingScope(cmd, mProfilingSampler)) {
                // TAA
                cmd.SetGlobalTexture(mTaaAccumulationTexID, mAccumulationTexture); // history texture
                cmd.SetGlobalFloat(mFrameInfluenceID, mResetHistoryFrames ? 1.0f : mSettings.FrameInfluence); // frame influence
                cmd.SetGlobalMatrix(mPrevViewProjMatrixID, mPrevViewProjMatrix); // 上一帧没有jitter的vp矩阵
                cmd.SetGlobalMatrix(mViewProjMatrixWithoutJitterID, mViewProjMatrix); // 这一帧没有Jitter的vp矩阵
                Blitter.BlitCameraTexture(cmd, mSourceTexture, mTaaTemporaryTexture, mTaaMaterial, 0);

                // Copy History
                Blitter.BlitCameraTexture(cmd, mTaaTemporaryTexture, mAccumulationTexture);

                // Final Pass
                Blitter.BlitCameraTexture(cmd, mTaaTemporaryTexture, mDestinationTexture);

                // 迭代上一帧没有vp矩阵
                mPrevViewProjMatrix = mViewProjMatrix;

                mResetHistoryFrames = false;
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        private void Setkeyword(string keyword, bool enabled = true) {
            if (enabled) mTaaMaterial.EnableKeyword(keyword);
            else mTaaMaterial.DisableKeyword(keyword);
        }

        public override void OnCameraCleanup(CommandBuffer cmd) {
            mSourceTexture = null;
            mDestinationTexture = null;
        }

        public void Dispose() {
            // 释放RTHandle
            mAccumulationTexture?.Release();
            mAccumulationTexture = null;

            mTaaTemporaryTexture?.Release();
            mTaaTemporaryTexture = null;
        }
    }

    internal class JitterPass : ScriptableRenderPass{
        private TAASettings mSettings;

        private ProfilingSampler mProfilingSampler = new ProfilingSampler("Jitter");

        public Matrix4x4 NonJitterProjectionMatrix;

        internal JitterPass() {
            mSettings = new TAASettings();
        }

        internal bool Setup(ref TAASettings featureSettings) {
            mSettings = featureSettings;
            NonJitterProjectionMatrix = Matrix4x4.identity;

            return true;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) {
            var cmd = CommandBufferPool.Get();
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            using (new ProfilingScope(cmd, mProfilingSampler)) {
                NonJitterProjectionMatrix = renderingData.cameraData.GetProjectionMatrix();
                cmd.SetViewProjectionMatrices(renderingData.cameraData.GetViewMatrix(), Jitter.CalculateJitterProjectionMatrix(ref renderingData.cameraData, mSettings.JitterScale));
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void Dispose() {
        }
    }
}