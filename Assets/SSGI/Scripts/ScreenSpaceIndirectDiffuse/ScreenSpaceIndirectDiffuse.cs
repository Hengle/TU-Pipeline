using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SSGI{
    [Serializable]
    internal class ScreenSpaceIndirectDiffuseSettings{
        // 填当前feature的参数
        [SerializeField] internal float Intensity = 1.0f;
        [SerializeField] internal float MaxDistance = 100.0f;
        [SerializeField] internal int Stride = 2;
        [SerializeField] internal int StepCount = 60;
        [SerializeField] internal float Thickness = 0.5f;
        [SerializeField] internal bool JitterDither = true;
        [SerializeField] internal uint SPP = 1;
        [SerializeField] [Range(0.0f, 1.0f)] internal float ScreenFade = 0.1f;

        [SerializeField] internal MotionType motionType;

        [SerializeField] [Range(0.001f, 0.99f)]
        internal float FrameInfluence = 0.01f;

        [SerializeField] internal Texture2D BlueNoise;
    }


    [DisallowMultipleRendererFeature("ScreenSpaceIndirectDiffuse")]
    public class ScreenSpaceIndirectDiffuse : ScriptableRendererFeature{
        [SerializeField] private ScreenSpaceIndirectDiffuseSettings mSSGISettings = new ScreenSpaceIndirectDiffuseSettings();
        [SerializeField] private HierarchicalZBufferSettings mHiZSettings = new HierarchicalZBufferSettings();

        private Shader mSSGIShader;
        private const string mSSGIShaderName = "Hidden/SSGI/SSGI";
        private Material mSSGIMaterial;

        private Shader mHiZShader;
        private const string mHiZShaderName = "Hidden/SSGI/HierarchicalZBuffer";
        private Material mHiZMaterial;

        private SSGIPass mSSGIPass;
        private HierarchicalZBufferPass mHiZPass;

        public override void Create() {
            if (mHiZPass == null) {
                mHiZPass = new HierarchicalZBufferPass();
                mHiZPass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            }

            if (mSSGIPass == null) {
                mSSGIPass = new SSGIPass();
                mSSGIPass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            }
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
            if (renderingData.cameraData.postProcessEnabled) {
                if (!GetMaterials()) {
                    Debug.LogErrorFormat("{0}.AddRenderPasses(): Missing material. {1} render pass will not be added.", GetType().Name, name);
                    return;
                }

                bool shouldAdd = mSSGIPass.Setup(ref mSSGISettings, ref mSSGIMaterial) && mHiZPass.Setup(ref mHiZSettings, ref mHiZMaterial);

                if (shouldAdd) {
                    renderer.EnqueuePass(mHiZPass);
                    renderer.EnqueuePass(mSSGIPass);
                }
            }
        }

        protected override void Dispose(bool disposing) {
            CoreUtils.Destroy(mSSGIMaterial);
            CoreUtils.Destroy(mHiZMaterial);

            mSSGIPass?.Dispose();
            mSSGIPass = null;

            mHiZPass?.Dispose();
            mHiZPass = null;
        }

        private bool GetMaterials() {
            if (mSSGIShader == null)
                mSSGIShader = Shader.Find(mSSGIShaderName);
            if (mSSGIMaterial == null && mSSGIShader != null)
                mSSGIMaterial = CoreUtils.CreateEngineMaterial(mSSGIShader);

            if (mHiZShader == null)
                mHiZShader = Shader.Find(mHiZShaderName);
            if (mHiZMaterial == null && mHiZShader != null)
                mHiZMaterial = CoreUtils.CreateEngineMaterial(mHiZShader);

            return mSSGIMaterial != null && mHiZMaterial != null;
        }
    }

    class SSGIPass : ScriptableRenderPass{
        internal enum ShaderPass{
            Raymarching,
            TemporalAA,
            BilateralFilter,
            CombinePass,
        }

        private ScreenSpaceIndirectDiffuseSettings mSettings;

        private Material mMaterial;

        private ProfilingSampler mProfilingSampler = new ProfilingSampler("ScreenSpaceIndirectDiffuse");
        private RenderTextureDescriptor mSSGIDescriptor;

        private RTHandle mSourceTexture;
        private RTHandle mDestinationTexture;

        // SSGI
        private static readonly int mProjectionParams2ID = Shader.PropertyToID("_ProjectionParams2"),
            mCameraViewTopLeftCornerID = Shader.PropertyToID("_CameraViewTopLeftCorner"),
            mCameraViewXExtentID = Shader.PropertyToID("_CameraViewXExtent"),
            mCameraViewYExtentID = Shader.PropertyToID("_CameraViewYExtent"),
            mSourceSizeID = Shader.PropertyToID("_SourceSize"),
            mSSGIParams0ID = Shader.PropertyToID("_SSGIParams0"),
            mSSGIParams1ID = Shader.PropertyToID("_SSGIParams1"),
            mBlueNoiseID = Shader.PropertyToID("_BlueNoise"),
            mSSGIJitterID = Shader.PropertyToID("_SSGIJitter"),
            mSSGIBlueNoiseSizeID = Shader.PropertyToID("_SSGIBlueNoiseSize"),
            mSSGIBilateralFilterSizeID = Shader.PropertyToID("_SSGIBilateralFilterSize");

        // Temporal AA
        private static readonly int mPrevViewProjMatrixID = Shader.PropertyToID("_LastViewProjMatrix"),
            mViewProjMatrixWithoutJitterID = Shader.PropertyToID("_ViewProjMatrixWithoutJitter"),
            mAccumulationTextureID = Shader.PropertyToID("_SSGIAccumulationTexture"),
            mFrameInfluenceID = Shader.PropertyToID("_FrameInfluence"),
            mTemporalAATextureID = Shader.PropertyToID("_SSGITemporalAATexture"),
            mSSGIBilateralFilteredTextureID = Shader.PropertyToID("_SSGIBilateralFilteredTexture");

        private const string mJitterKeyword = "_JITTER_ON",
            mMotionCameraKeyword = "_MOTION_CAMERA",
            mMotionObjectKeyword = "_MOTION_OBJECT";

        private const string mSSGISceneColorTextureName = "_SSGISceneColorTexture",
            mBilateralXFilteredTextureName = "_BilateralXFilteredTexture",
            mBilateralYFilteredTextureName = "_BilateralYFilteredTexture",
            mAccumulationTextureName = "_SSGIAccumulationTexture",
            mTemporalAATextureName = "_TemporalAATexture";

        private RTHandle mSSGISceneColorTexture, mBilateralXFilteredTexture, mBilateralYFilteredTexture;
        private RTHandle mAccumulationTexture, mTemporalAATexture;

        private bool mResetHistory;

        private Matrix4x4 mPrevViewProjMatrix, mViewProjMatrix;

        internal SSGIPass() {
            mSettings = new ScreenSpaceIndirectDiffuseSettings();
        }

        internal bool Setup(ref ScreenSpaceIndirectDiffuseSettings featureSettings, ref Material material) {
            mMaterial = material;
            mSettings = featureSettings;

            ConfigureInput(ScriptableRenderPassInput.Normal | ScriptableRenderPassInput.Motion);

            return mMaterial != null;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData) {
            // 发送参数
            Matrix4x4 view = renderingData.cameraData.GetViewMatrix();
            Matrix4x4 proj = renderingData.cameraData.GetProjectionMatrix();
            Matrix4x4 vp = proj * view;

            // 将camera view space 的平移置为0，用来计算world space下相对于相机的vector
            Matrix4x4 cview = view;
            cview.SetColumn(3, new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
            Matrix4x4 cviewProj = proj * cview;

            // 计算viewProj逆矩阵，即从裁剪空间变换到世界空间
            Matrix4x4 cviewProjInv = cviewProj.inverse;

            // 计算世界空间下，近平面四个角的坐标
            var near = renderingData.cameraData.camera.nearClipPlane;
            Vector4 topLeftCorner = cviewProjInv.MultiplyPoint(new Vector4(-1.0f, 1.0f, -1.0f, 1.0f));
            Vector4 topRightCorner = cviewProjInv.MultiplyPoint(new Vector4(1.0f, 1.0f, -1.0f, 1.0f));
            Vector4 bottomLeftCorner = cviewProjInv.MultiplyPoint(new Vector4(-1.0f, -1.0f, -1.0f, 1.0f));

            // 计算相机近平面上方向向量
            Vector4 cameraXExtent = topRightCorner - topLeftCorner;
            Vector4 cameraYExtent = bottomLeftCorner - topLeftCorner;

            near = renderingData.cameraData.camera.nearClipPlane;


            // 发送ReconstructViewPos参数
            mMaterial.SetVector(mCameraViewTopLeftCornerID, topLeftCorner);
            mMaterial.SetVector(mCameraViewXExtentID, cameraXExtent);
            mMaterial.SetVector(mCameraViewYExtentID, cameraYExtent);
            mMaterial.SetVector(mProjectionParams2ID, new Vector4(1.0f / near, renderingData.cameraData.worldSpaceCameraPos.x, renderingData.cameraData.worldSpaceCameraPos.y, renderingData.cameraData.worldSpaceCameraPos.z));

            mMaterial.SetVector(mSourceSizeID, new Vector4(mSSGIDescriptor.width, mSSGIDescriptor.height, 1.0f / mSSGIDescriptor.width, 1.0f / mSSGIDescriptor.height));

            mMaterial.SetTexture(mBlueNoiseID, mSettings.BlueNoise);

            if (mSettings.BlueNoise != null)
                mMaterial.SetVector(mSSGIBlueNoiseSizeID, new Vector4(mSettings.BlueNoise.width, mSettings.BlueNoise.height, 1.0f / mSettings.BlueNoise.width, 1.0f / mSettings.BlueNoise.height));

            // 发送SSGI参数
            mMaterial.SetVector(mSSGIParams0ID, new Vector4(mSettings.MaxDistance, mSettings.Stride, mSettings.StepCount, mSettings.Thickness));
            mMaterial.SetVector(mSSGIParams1ID, new Vector4(mSettings.Intensity, mSettings.SPP, 0.0f, mSettings.ScreenFade));
            mMaterial.SetVector(mSSGIJitterID, Jitter.CalculateJitter(Time.frameCount));

            // 设置全局keyword
            if (mSettings.JitterDither)
                mMaterial.EnableKeyword(mJitterKeyword);
            else
                mMaterial.DisableKeyword(mJitterKeyword);

            // 分配RTHandle
            mSSGIDescriptor = renderingData.cameraData.cameraTargetDescriptor;
            mSSGIDescriptor.msaaSamples = 1;
            mSSGIDescriptor.depthBufferBits = 0;
            mSSGIDescriptor.colorFormat = RenderTextureFormat.ARGB64;

            mResetHistory = RenderingUtils.ReAllocateIfNeeded(ref mAccumulationTexture, mSSGIDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: mAccumulationTextureName);
            RenderingUtils.ReAllocateIfNeeded(ref mTemporalAATexture, mSSGIDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: mTemporalAATextureName);

            RenderingUtils.ReAllocateIfNeeded(ref mSSGISceneColorTexture, mSSGIDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: mSSGISceneColorTextureName);
            RenderingUtils.ReAllocateIfNeeded(ref mBilateralXFilteredTexture, mSSGIDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: mBilateralXFilteredTextureName);
            RenderingUtils.ReAllocateIfNeeded(ref mBilateralYFilteredTexture, mSSGIDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: mBilateralYFilteredTextureName);

            if (mResetHistory) {
                // 初始化上一帧的vp矩阵
                mPrevViewProjMatrix = renderingData.cameraData.GetProjectionMatrix() * renderingData.cameraData.GetViewMatrix();
            }

            // 计算本帧没有jitter的vp矩阵
            mViewProjMatrix = renderingData.cameraData.GetProjectionMatrix() * renderingData.cameraData.GetViewMatrix();

            // 设置Keyword
            Setkeyword(mMotionCameraKeyword, mSettings.motionType == MotionType.CameraOnly);
            Setkeyword(mMotionObjectKeyword, mSettings.motionType == MotionType.Object);
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
                // Ray Tracing
                Blitter.BlitCameraTexture(cmd, mSourceTexture, mSSGISceneColorTexture, mMaterial, (int)ShaderPass.Raymarching);

                // Temporal AA
                cmd.SetGlobalMatrix(mPrevViewProjMatrixID, mPrevViewProjMatrix); // 上一帧没有jitter的vp矩阵
                cmd.SetGlobalMatrix(mViewProjMatrixWithoutJitterID, mViewProjMatrix); // 这一帧没有Jitter的vp矩阵
                cmd.SetGlobalFloat(mFrameInfluenceID, mResetHistory ? 1.0f : mSettings.FrameInfluence);
                cmd.SetGlobalTexture(mAccumulationTextureID, mAccumulationTexture);
                Blitter.BlitCameraTexture(cmd, mSSGISceneColorTexture, mTemporalAATexture, mMaterial, (int)ShaderPass.TemporalAA);
                Blitter.BlitCameraTexture(cmd, mTemporalAATexture, mAccumulationTexture);

                // Bilateral Filter Sampler
                cmd.SetGlobalVector(mSSGIBilateralFilterSizeID, new Vector2(1.0f, 0.0f));
                Blitter.BlitCameraTexture(cmd, mTemporalAATexture, mBilateralXFilteredTexture, mMaterial, (int)ShaderPass.BilateralFilter);
                cmd.SetGlobalVector(mSSGIBilateralFilterSizeID, new Vector2(0.0f, 1.0f));
                Blitter.BlitCameraTexture(cmd, mBilateralXFilteredTexture, mBilateralYFilteredTexture, mMaterial, (int)ShaderPass.BilateralFilter);

                cmd.SetGlobalTexture(mSSGIBilateralFilteredTextureID, mBilateralYFilteredTexture);
                Blitter.BlitCameraTexture(cmd, mSourceTexture, mBilateralXFilteredTexture, mMaterial, (int)ShaderPass.CombinePass);

                // Final Pass
                Blitter.BlitCameraTexture(cmd, mBilateralXFilteredTexture, mDestinationTexture);

                // 迭代上一帧没有vp矩阵
                mPrevViewProjMatrix = mViewProjMatrix;
                mResetHistory = false;
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        private void Setkeyword(string keyword, bool enabled = true) {
            if (enabled) mMaterial.EnableKeyword(keyword);
            else mMaterial.DisableKeyword(keyword);
        }

        public override void OnCameraCleanup(CommandBuffer cmd) {
            mSourceTexture = null;
            mDestinationTexture = null;
        }

        public void Dispose() {
            // 释放RTHandle
            mAccumulationTexture?.Release();
            mAccumulationTexture = null;

            mTemporalAATexture?.Release();
            mTemporalAATexture = null;

            mSSGISceneColorTexture?.Release();
            mSSGISceneColorTexture = null;

            mBilateralXFilteredTexture?.Release();
            mBilateralXFilteredTexture = null;

            mBilateralYFilteredTexture?.Release();
            mBilateralYFilteredTexture = null;
        }
    }
}