using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SSGI{
    [Serializable]
    internal class SSSRSettings{
        // 填当前feature的参数
        [SerializeField] internal float MaxDistance = 10.0f;
        [SerializeField] internal int Stride = 30;
        [SerializeField] internal int StepCount = 12;
        [SerializeField] internal float Thickness = 0.5f;
        [SerializeField] internal bool JitterDither = true;
        [SerializeField] internal uint SPP = 3;
        [SerializeField] [Range(0.0f, 1.0f)] internal float ScreenFade = 0.5f;
        [SerializeField] [Range(0.0f, 1.0f)] internal float BRDFBias = 0.7f;

        [SerializeField] internal MotionType motionType;

        [SerializeField] [Range(0.001f, 0.99f)]
        internal float FrameInfluence = 0.01f;

        [SerializeField] internal Texture2D BlueNoise;
        [SerializeField] internal Texture2D PreintegratedGF_LUT;
    }


    [DisallowMultipleRendererFeature("SSSR")]
    public class SSSR : ScriptableRendererFeature{
        [SerializeField] private SSSRSettings mSSSRSettings = new SSSRSettings();
        [SerializeField] private HierarchicalZBufferSettings mHiZSettings = new HierarchicalZBufferSettings();

        private Shader mSSSRShader;
        private const string mSSSRShaderName = "Hidden/SSGI/SSR /SSSR";
        private Material mSSSRMaterial;

        private Shader mHiZShader;
        private const string mHiZShaderName = "Hidden/SSGI/HierarchicalZBuffer";
        private Material mHiZMaterial;

        private SSSRPass mSSSRPass;
        private HierarchicalZBufferPass mHiZPass;

        public override void Create() {
            if (mHiZPass == null) {
                mHiZPass = new HierarchicalZBufferPass();
                mHiZPass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            }

            if (mSSSRPass == null) {
                mSSSRPass = new SSSRPass();
                mSSSRPass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            }
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
            if (renderingData.cameraData.postProcessEnabled) {
                if (!GetMaterials()) {
                    Debug.LogErrorFormat("{0}.AddRenderPasses(): Missing material. {1} render pass will not be added.", GetType().Name, name);
                    return;
                }

                bool shouldAdd = mSSSRPass.Setup(ref mSSSRSettings, ref mSSSRMaterial) && mHiZPass.Setup(ref mHiZSettings, ref mHiZMaterial);

                if (shouldAdd) {
                    renderer.EnqueuePass(mHiZPass);
                    renderer.EnqueuePass(mSSSRPass);
                }
            }
        }

        protected override void Dispose(bool disposing) {
            CoreUtils.Destroy(mSSSRMaterial);
            CoreUtils.Destroy(mHiZMaterial);

            mSSSRPass?.Dispose();
            mSSSRPass = null;

            mHiZPass?.Dispose();
            mHiZPass = null;
        }

        private bool GetMaterials() {
            if (mSSSRShader == null)
                mSSSRShader = Shader.Find(mSSSRShaderName);
            if (mSSSRMaterial == null && mSSSRShader != null)
                mSSSRMaterial = CoreUtils.CreateEngineMaterial(mSSSRShader);

            if (mHiZShader == null)
                mHiZShader = Shader.Find(mHiZShaderName);
            if (mHiZMaterial == null && mHiZShader != null)
                mHiZMaterial = CoreUtils.CreateEngineMaterial(mHiZShader);

            return mSSSRMaterial != null && mHiZMaterial != null;
        }
    }

    class SSSRPass : ScriptableRenderPass{
        internal enum ShaderPass{
            Raymarching,
            SpatioFilter,
            TemporalAA,
            CombinePass,
        }

        private SSSRSettings mSettings;

        private Material mMaterial;

        private ProfilingSampler mProfilingSampler = new ProfilingSampler("Stochastic SSR");
        private RenderTextureDescriptor mSSSRDescriptor;

        private RTHandle mSourceTexture;
        private RTHandle mDestinationTexture;

        // SSR
        private static readonly int mProjectionParams2ID = Shader.PropertyToID("_ProjectionParams2"),
            mCameraViewTopLeftCornerID = Shader.PropertyToID("_CameraViewTopLeftCorner"),
            mCameraViewXExtentID = Shader.PropertyToID("_CameraViewXExtent"),
            mCameraViewYExtentID = Shader.PropertyToID("_CameraViewYExtent"),
            mSourceSizeID = Shader.PropertyToID("_SourceSize"),
            mSSSRParams0ID = Shader.PropertyToID("_SSSRParams0"),
            mSSSRParams1ID = Shader.PropertyToID("_SSSRParams1"),
            mBlueNoiseID = Shader.PropertyToID("_BlueNoise"),
            mSSRColorPDFTextureID = Shader.PropertyToID("_SSRColorPDFTexture"),
            mMaskDepthHitUVTexutreID = Shader.PropertyToID("_MaskDepthHitUVTexture"),
            mSSSRSpatioFilteredTextureID = Shader.PropertyToID("_SSSRSpatioFilteredTexture"),
            mSSSRJitterID = Shader.PropertyToID("_SSSRJitter"),
            mPreintegratedGF_LUTID = Shader.PropertyToID("_PreintegratedGF_LUT"),
            mSSSRBlueNoiseSizeID = Shader.PropertyToID("_SSSRBlueNoiseSize");

        // Temporal AA
        private static readonly int mPrevViewProjMatrixID = Shader.PropertyToID("_LastViewProjMatrix"),
            mViewProjMatrixWithoutJitterID = Shader.PropertyToID("_ViewProjMatrixWithoutJitter"),
            mAccumulationTextureID = Shader.PropertyToID("_SSSRAccumulationTexture"),
            mFrameInfluenceID = Shader.PropertyToID("_FrameInfluence"),
            mTemporalAATextureID = Shader.PropertyToID("_SSSRTemporalAATexture");

        private const string mJitterKeyword = "_JITTER_ON",
            mMotionCameraKeyword = "_MOTION_CAMERA",
            mMotionObjectKeyword = "_MOTION_OBJECT";

        private const string mSSRColorPDFTextureName = "_SSRColorPDFTexture",
            mMaskDepthHitUVTextureName = "_MaskDepthHitUVTexture",
            mSSSRSpatioFilteredTextureName = "_SSSRSpatioFilteredTexture",
            mAccumulationTextureName = "_SSSRAccumulationTexture",
            mTemporalAATextureName = "_TemporalAATexture";

        private RTHandle mSSRColorPDFTexture, mMaskDepthHitUVTexture, mSSSRSpatioFilteredTexture;
        private RTHandle mAccumulationTexture, mTemporalAATexture;
        private RenderTargetIdentifier[] mRaymarchingTarget;

        private bool mResetHistory;

        private Matrix4x4 mPrevViewProjMatrix, mViewProjMatrix;

        internal SSSRPass() {
            mSettings = new SSSRSettings();
        }

        internal bool Setup(ref SSSRSettings featureSettings, ref Material material) {
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

            mMaterial.SetVector(mSourceSizeID, new Vector4(mSSSRDescriptor.width, mSSSRDescriptor.height, 1.0f / mSSSRDescriptor.width, 1.0f / mSSSRDescriptor.height));

            mMaterial.SetTexture(mBlueNoiseID, mSettings.BlueNoise);
            mMaterial.SetTexture(mPreintegratedGF_LUTID, mSettings.PreintegratedGF_LUT);

            if (mSettings.BlueNoise != null)
                mMaterial.SetVector(mSSSRBlueNoiseSizeID, new Vector4(mSettings.BlueNoise.width, mSettings.BlueNoise.height, 1.0f / mSettings.BlueNoise.width, 1.0f / mSettings.BlueNoise.height));

            // 发送SSR参数
            mMaterial.SetVector(mSSSRParams0ID, new Vector4(mSettings.MaxDistance, mSettings.Stride, mSettings.StepCount, mSettings.Thickness));
            mMaterial.SetVector(mSSSRParams1ID, new Vector4(0.0f, mSettings.SPP, mSettings.BRDFBias, mSettings.ScreenFade));
            mMaterial.SetVector(mSSSRJitterID, Jitter.CalculateJitter(Time.frameCount));

            // 设置全局keyword
            if (mSettings.JitterDither)
                mMaterial.EnableKeyword(mJitterKeyword);
            else
                mMaterial.DisableKeyword(mJitterKeyword);

            // 分配RTHandle
            mSSSRDescriptor = renderingData.cameraData.cameraTargetDescriptor;
            mSSSRDescriptor.msaaSamples = 1;
            mSSSRDescriptor.depthBufferBits = 0;
            mSSSRDescriptor.colorFormat = RenderTextureFormat.ARGB64;

            RenderingUtils.ReAllocateIfNeeded(ref mSSRColorPDFTexture, mSSSRDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: mSSRColorPDFTextureName);
            RenderingUtils.ReAllocateIfNeeded(ref mMaskDepthHitUVTexture, mSSSRDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: mMaskDepthHitUVTextureName);
            RenderingUtils.ReAllocateIfNeeded(ref mSSSRSpatioFilteredTexture, mSSSRDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: mSSSRSpatioFilteredTextureName);
            mResetHistory = RenderingUtils.ReAllocateIfNeeded(ref mAccumulationTexture, mSSSRDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: mAccumulationTextureName);
            RenderingUtils.ReAllocateIfNeeded(ref mTemporalAATexture, mSSSRDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: mTemporalAATextureName);

            mRaymarchingTarget = new RenderTargetIdentifier[] { mSSRColorPDFTexture, mMaskDepthHitUVTexture };

            if (mResetHistory) {
                // 初始化上一帧的vp矩阵
                mPrevViewProjMatrix = renderingData.cameraData.GetProjectionMatrix() * renderingData.cameraData.GetViewMatrix();
            }

            // 计算本帧没有jitter的vp矩阵
            mViewProjMatrix = renderingData.cameraData.GetProjectionMatrix() * renderingData.cameraData.GetViewMatrix();

            // 设置Keyword
            Setkeyword(mMotionCameraKeyword, mSettings.motionType == MotionType.CameraOnly);
            Setkeyword(mMotionObjectKeyword, mSettings.motionType == MotionType.Object);
            
            ConfigureTarget(renderingData.cameraData.renderer.cameraColorTargetHandle);
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
                // SSR
                cmd.SetRenderTarget(mRaymarchingTarget, renderingData.cameraData.renderer.cameraDepthTargetHandle);
                Blitter.BlitTexture(cmd, mSourceTexture, Vector2.one, mMaterial, (int)ShaderPass.Raymarching);

                // Spatio Filter
                cmd.SetGlobalTexture(mSSRColorPDFTextureID, mSSRColorPDFTexture);
                cmd.SetGlobalTexture(mMaskDepthHitUVTexutreID, mMaskDepthHitUVTexture);
                CoreUtils.SetRenderTarget(cmd, mSSSRSpatioFilteredTexture);
                Blitter.BlitTexture(cmd, mSSSRSpatioFilteredTexture, Vector2.one, mMaterial, (int)ShaderPass.SpatioFilter);
                // cmd.DrawProcedural(Matrix4x4.identity, mMaterial, (int)ShaderPass.SpatioFilter, MeshTopology.Triangles, 3, 1);

                // Temporal AA
                cmd.SetGlobalMatrix(mPrevViewProjMatrixID, mPrevViewProjMatrix); // 上一帧没有jitter的vp矩阵
                cmd.SetGlobalMatrix(mViewProjMatrixWithoutJitterID, mViewProjMatrix); // 这一帧没有Jitter的vp矩阵
                cmd.SetGlobalFloat(mFrameInfluenceID, mResetHistory ? 1.0f : mSettings.FrameInfluence);
                cmd.SetGlobalTexture(mAccumulationTextureID, mAccumulationTexture);
                Blitter.BlitCameraTexture(cmd, mSSSRSpatioFilteredTexture, mTemporalAATexture, mMaterial, (int)ShaderPass.TemporalAA);
                Blitter.BlitCameraTexture(cmd, mTemporalAATexture, mAccumulationTexture);

                // Combine Pass
                cmd.SetGlobalTexture(mTemporalAATextureID, mTemporalAATexture);
                Blitter.BlitCameraTexture(cmd, mSourceTexture, mSSRColorPDFTexture, mMaterial, (int)ShaderPass.CombinePass);

                // Final Pass
                Blitter.BlitCameraTexture(cmd, mSSRColorPDFTexture, mDestinationTexture);

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
            mSSRColorPDFTexture?.Release();
            mSSRColorPDFTexture = null;

            mMaskDepthHitUVTexture?.Release();
            mMaskDepthHitUVTexture = null;

            mSSSRSpatioFilteredTexture?.Release();
            mSSSRSpatioFilteredTexture = null;

            mAccumulationTexture?.Release();
            mAccumulationTexture = null;

            mTemporalAATexture?.Release();
            mTemporalAATexture = null;
        }
    }
}