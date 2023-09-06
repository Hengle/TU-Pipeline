using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SSGI{
    internal enum SSRBlendMode{
        Addtive,
        Balance
    }

    public enum SSRAccelerateType{
        BinarySearch,
        HierarchicalZBuffer,
    }

    [Serializable]
    internal class SSRSettings{
        // 填当前feature的参数
        [SerializeField] [Range(0.0f, 1.0f)] internal float Intensity = 0.8f;
        [SerializeField] internal SSRAccelerateType AccelerateType = SSRAccelerateType.HierarchicalZBuffer;
        [SerializeField] internal float MaxDistance = 10.0f;
        [SerializeField] internal int Stride = 2;
        [SerializeField] internal int StepCount = 40;
        [SerializeField] internal float Thickness = 0.5f;
        [SerializeField] internal int BinaryCount = 6;
        [SerializeField] internal bool JitterDither = true;
        [SerializeField] internal SSRBlendMode BlendMode = SSRBlendMode.Addtive;
        [SerializeField] internal float BlurRadius = 1.0f;
        [SerializeField] internal bool ApplyCameraGBuffer = false;
    }


    [DisallowMultipleRendererFeature("SSR")]
    public class SSR : ScriptableRendererFeature{
        [SerializeField] private SSRSettings mSSRSettings = new SSRSettings();
        [SerializeField] private HierarchicalZBufferSettings mHiZSettings = new HierarchicalZBufferSettings();

        private Shader mSSRShader;
        private const string mSSRShaderName = "Hidden/SSGI/SSR/SSR";
        private Material mSSRMaterial;

        private Shader mHiZShader;
        private const string mHiZShaderName = "Hidden/SSGI/HierarchicalZBuffer";
        private Material mHiZMaterial;

        private SSRPass mSSRPass;
        private HierarchicalZBufferPass mHiZPass;


        public override void Create() {
            if (mHiZPass == null) {
                mHiZPass = new HierarchicalZBufferPass();
                mHiZPass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            }

            if (mSSRPass == null) {
                mSSRPass = new SSRPass();
                mSSRPass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            }
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
            if (renderingData.cameraData.postProcessEnabled) {
                if (!GetMaterials()) {
                    Debug.LogErrorFormat("{0}.AddRenderPasses(): Missing material. {1} render pass will not be added.", GetType().Name, name);
                    return;
                }

                bool shouldAdd = mSSRPass.Setup(ref mSSRSettings, ref mSSRMaterial) && mHiZPass.Setup(ref mHiZSettings, ref mHiZMaterial);

                if (shouldAdd) {
                    if (mSSRSettings.AccelerateType == SSRAccelerateType.HierarchicalZBuffer)
                        renderer.EnqueuePass(mHiZPass);
                    renderer.EnqueuePass(mSSRPass);
                }
            }
        }

        protected override void Dispose(bool disposing) {
            CoreUtils.Destroy(mSSRMaterial);

            mSSRPass?.Dispose();
            mSSRPass = null;

            mHiZPass?.Dispose();
            mHiZPass = null;
        }

        private bool GetMaterials() {
            if (mSSRShader == null)
                mSSRShader = Shader.Find(mSSRShaderName);
            if (mSSRMaterial == null && mSSRShader != null)
                mSSRMaterial = CoreUtils.CreateEngineMaterial(mSSRShader);

            if (mHiZShader == null)
                mHiZShader = Shader.Find(mHiZShaderName);
            if (mHiZMaterial == null && mHiZShader != null)
                mHiZMaterial = CoreUtils.CreateEngineMaterial(mHiZShader);

            return mSSRMaterial != null && mHiZMaterial != null;
        }
    }
    
    class SSRPass : ScriptableRenderPass{
        internal enum ShaderPass{
            Raymarching,
            Blur,
            Addtive,
            Balance,
        }

        private SSRSettings mSettings;

        private Material mMaterial;

        private ProfilingSampler mProfilingSampler = new ProfilingSampler("SSR");
        private RenderTextureDescriptor mSSRDescriptor;

        private RTHandle mSourceTexture;
        private RTHandle mDestinationTexture;

        private static readonly int mProjectionParams2ID = Shader.PropertyToID("_ProjectionParams2"),
            mCameraViewTopLeftCornerID = Shader.PropertyToID("_CameraViewTopLeftCorner"),
            mCameraViewXExtentID = Shader.PropertyToID("_CameraViewXExtent"),
            mCameraViewYExtentID = Shader.PropertyToID("_CameraViewYExtent"),
            mSourceSizeID = Shader.PropertyToID("_SourceSize"),
            mSSRParams0ID = Shader.PropertyToID("_SSRParams0"),
            mSSRParams1ID = Shader.PropertyToID("_SSRParams1"),
            mBlurRadiusID = Shader.PropertyToID("_SSRBlurRadius");

        private const string mJitterKeyword = "_JITTER_ON",
            mHiZKeyword = "_HIZ_ON",
            mApplyGBufferKeyword = "_GBUFFER_ON";

        private RTHandle mSSRTexture0, mSSRTexture1;

        private const string mSSRTexture0Name = "_SSRTexture0",
            mSSRTexture1Name = "_SSRTexture1";

        internal SSRPass() {
            mSettings = new SSRSettings();
        }

        internal bool Setup(ref SSRSettings featureSettings, ref Material material) {
            mMaterial = material;
            mSettings = featureSettings;

            ConfigureInput(ScriptableRenderPassInput.Normal);

            return mMaterial != null;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData) {
            var renderer = renderingData.cameraData.renderer;

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
            // Vector4 topLeftCorner = cviewProjInv * new Vector4(-near, near, -near, near);
            // Vector4 topRightCorner = cviewProjInv * new Vector4(near, near, -near, near);
            // Vector4 bottomLeftCorner = cviewProjInv * new Vector4(-near, -near, -near, near);
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

            mMaterial.SetVector(mSourceSizeID, new Vector4(mSSRDescriptor.width, mSSRDescriptor.height, 1.0f / mSSRDescriptor.width, 1.0f / mSSRDescriptor.height));

            // 发送SSR参数
            mMaterial.SetVector(mSSRParams0ID, new Vector4(mSettings.MaxDistance, mSettings.Stride, mSettings.StepCount, mSettings.Thickness));
            mMaterial.SetVector(mSSRParams1ID, new Vector4(mSettings.BinaryCount, mSettings.Intensity, 0.0f, 0.0f));

            // 设置全局keyword
            SetKeyword(mJitterKeyword, mSettings.JitterDither);
            SetKeyword(mHiZKeyword, mSettings.AccelerateType == SSRAccelerateType.HierarchicalZBuffer);
            SetKeyword(mApplyGBufferKeyword, mSettings.ApplyCameraGBuffer);

            // 分配RTHandle
            mSSRDescriptor = renderingData.cameraData.cameraTargetDescriptor;
            mSSRDescriptor.msaaSamples = 1;
            mSSRDescriptor.depthBufferBits = 0;
            RenderingUtils.ReAllocateIfNeeded(ref mSSRTexture0, mSSRDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: mSSRTexture0Name);
            RenderingUtils.ReAllocateIfNeeded(ref mSSRTexture1, mSSRDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: mSSRTexture1Name);

            // 配置目标和清除
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
                Blitter.BlitCameraTexture(cmd, mSourceTexture, mSSRTexture0, mMaterial, (int)ShaderPass.Raymarching);

                // Horizontal Blur
                cmd.SetGlobalVector(mBlurRadiusID, new Vector4(mSettings.BlurRadius, 0.0f, 0.0f, 0.0f));
                Blitter.BlitCameraTexture(cmd, mSSRTexture0, mSSRTexture1, mMaterial, (int)ShaderPass.Blur);

                // Vertical Blur
                cmd.SetGlobalVector(mBlurRadiusID, new Vector4(0.0f, mSettings.BlurRadius, 0.0f, 0.0f));
                Blitter.BlitCameraTexture(cmd, mSSRTexture1, mSSRTexture0, mMaterial, (int)ShaderPass.Blur);

                // Additive Pass
                Blitter.BlitCameraTexture(cmd, mSSRTexture0, mDestinationTexture, mMaterial, mSettings.BlendMode == SSRBlendMode.Addtive ? (int)ShaderPass.Addtive : (int)ShaderPass.Balance);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        private void SetKeyword(string keyword, bool enabled = true) {
            if (enabled) mMaterial.EnableKeyword(keyword);
            else mMaterial.DisableKeyword(keyword);
        }

        public override void OnCameraCleanup(CommandBuffer cmd) {
            mSourceTexture = null;
            mDestinationTexture = null;
        }

        public void Dispose() {
            // 释放RTHandle
            mSSRTexture0?.Release();
            mSSRTexture1?.Release();
        }
    }
}