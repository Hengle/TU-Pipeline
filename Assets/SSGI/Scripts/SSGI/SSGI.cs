using System;
using CameraGBuffer;
using FXAA;
using HBAO;
using SSAO;
using SSPR;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SSGI{
    [Serializable]
    internal class SSGISettings{
        // 填当前feature的参数
        [SerializeField] internal bool IndirectDiffuse = false;
        [SerializeField] internal bool SSR = false;
        [SerializeField] internal bool AO = false;
        [SerializeField] internal bool AntiAliasing = false;
        [SerializeField] internal bool CameraGBuffer = false;
    }

    [Serializable]
    public enum SSRType{
        SSR,
        StochasticSSR,
        SSPR
    }

    [Serializable]
    public enum AOType{
        SSAO,
        HBAO
    }

    [Serializable]
    public enum AntiAliasingType{
        FXAA,
        TAA
    }

    [DisallowMultipleRendererFeature("SSGI")]
    public class SSGI : ScriptableRendererFeature{
        [SerializeField] private SSGISettings mSSGISettings = new SSGISettings();

        //
        // CameraGBuffer
        //
        [SerializeField] private CameraGBufferSettings mCameraGBufferSettings = new CameraGBufferSettings();

        // BaseColor
        [SerializeField] private BaseColorGBufferSettings mBaseColorGBufferSettings = new BaseColorGBufferSettings();
        private BaseColorGBufferPass mBaseColorGBufferPass;

        // SpecularColor
        [SerializeField] private SpecularGBufferSettings mSpecularGBufferSettings = new SpecularGBufferSettings();
        private SpecularGBufferPass mSpecularGBufferPass;

        // Reflection
        [SerializeField] private ReflectionGBufferSettings mReflectionGBufferSettings = new ReflectionGBufferSettings();
        private ReflectionGBufferPass mReflectionGBufferPass;

        //
        // HiZ
        //
        [SerializeField] private HierarchicalZBufferSettings mHiZBufferSettings = new HierarchicalZBufferSettings();
        private Shader mHiZBufferShader;
        private const string mHiZBufferShaderName = "Hidden/SSGI/HierarchicalZBuffer";
        private Material mHiZBufferMaterial;
        private HierarchicalZBufferPass mHiZBufferPass;

        //
        // ScreenSpaceIndirectDiffuse
        //
        [SerializeField] private ScreenSpaceIndirectDiffuseSettings mScreenSpaceIndirectDiffuseSettings = new ScreenSpaceIndirectDiffuseSettings();
        private Shader mSSGIShader;
        private const string mSSGIShaderName = "Hidden/SSGI/SSGI";
        private Material mSSGIMaterial;
        private SSGIPass mSSGIPass;

        //
        // SSR
        //
        [SerializeField] private SSRType mSSRType = SSRType.SSR;

        // SSR
        [SerializeField] private SSRSettings mSSRSettings = new SSRSettings();
        private Shader mSSRShader;
        private const string mSSRShaderName = "Hidden/SSGI/SSR/SSR";
        private Material mSSRMaterial;
        private SSRPass mSSRPass;

        // SSSR
        [SerializeField] private SSSRSettings mSSSRSettings = new SSSRSettings();
        private Shader mSSSRShader;
        private const string mSSSRShaderName = "Hidden/SSGI/SSR/SSSR";
        private Material mSSSRMaterial;
        private SSSRPass mSSSRPass;

        // SSPR
        [SerializeField] private SSPRSettings mSSPRSettings = new SSPRSettings();
        private SSPRPass mSSPRPass;
        private const string mSSPRComputeShaderName = "SSPR";
        private ComputeShader mSSPRComputeShader;

        //
        // AO
        //
        [SerializeField] private AOType mAOType = AOType.SSAO;

        // SSAO
        [SerializeField] private SSAOSettings mSSAOSettings = new SSAOSettings();
        private Shader mSSAOShader;
        private const string mSSAOShaderName = "Hidden/SSGI/AO/SSAO";
        private Material mSSAOMaterial;
        private SSAOPass mSSAOPass;

        // HBAO
        [SerializeField] private HBAOSettings mHBAOSettings = new HBAOSettings();
        private Shader mHBAOShader;
        private const string mHBAOShaderName = "Hidden/SSGI/AO/HBAO";
        private Material mHBAOMaterial;
        private HBAOPass mHBAOPass;

        //
        // Anti-Aliasing
        //
        [SerializeField] private AntiAliasingType mAntiAliasingType = AntiAliasingType.FXAA;

        // FXAA
        [SerializeField] private FXAASettings mFXAASettings = new FXAASettings();
        private Shader mFXAAShader;
        private const string mFXAAShaderName = "Hidden/Anti-Aliasing/FXAA";
        private Material mFXAAMaterial;
        private FXAAPass mFXAAPass;

        // TAA
        [SerializeField] private TAASettings mTAASettings = new TAASettings();
        private Shader mTAAShader;
        private const string mTAAShaderName = "Hidden/Anti-Aliasing/TAA";
        private Material mTAAMaterial;
        private TAAPass mTAAPass;
        private JitterPass mJitterPass;

        public override void Create() {
            CreateCameraGBufferPasses();

            if (mHiZBufferPass == null) {
                mHiZBufferPass = new HierarchicalZBufferPass();
                mHiZBufferPass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            }

            if (mSSGIPass == null) {
                mSSGIPass = new SSGIPass();
                mSSGIPass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            }

            if (mSSRPass == null) {
                mSSRPass = new SSRPass();
                mSSRPass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            }

            if (mSSSRPass == null) {
                mSSSRPass = new SSSRPass();
                mSSSRPass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            }

            if (mSSPRPass == null) {
                mSSPRPass = new SSPRPass();
                mSSPRPass.renderPassEvent = RenderPassEvent.BeforeRenderingSkybox;
            }

            if (mSSAOPass == null) {
                mSSAOPass = new SSAOPass();
                mSSAOPass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            }

            if (mHBAOPass == null) {
                mHBAOPass = new HBAOPass();
                mHBAOPass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            }

            if (mFXAAPass == null) {
                mFXAAPass = new FXAAPass();
                mFXAAPass.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            }

            if (mJitterPass == null) {
                mJitterPass = new JitterPass();
                // 在渲染场景前抖动相机
                mJitterPass.renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
            }

            if (mTAAPass == null) {
                mTAAPass = new TAAPass();
                mTAAPass.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            }
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
            if (renderingData.cameraData.postProcessEnabled) {
                if (!GetMaterials()) {
                    Debug.LogErrorFormat("{0}.AddRenderPasses(): Missing material. {1} render pass will not be added.", GetType().Name, name);
                    return;
                }

                if (!GetComputeShaders()) {
                    Debug.LogErrorFormat("{0}.AddRenderPasses(): Missing computeShader. {1} render pass will not be added.", GetType().Name, name);
                    return;
                }

                // CameraGBuffer
                AddCameraGBufferPasses(ref renderer);

                // HiZ Buffer
                if (ShouldAddHizPass() && mHiZBufferPass.Setup(ref mHiZBufferSettings, ref mHiZBufferMaterial)) {
                    renderer.EnqueuePass(mHiZBufferPass);
                }

                // ScreenSpaceIndirectDiffuse
                if (mSSGISettings.IndirectDiffuse && mSSGIPass.Setup(ref mScreenSpaceIndirectDiffuseSettings, ref mSSGIMaterial)) {
                    renderer.EnqueuePass(mSSGIPass);
                }

                // SSR
                if (mSSGISettings.SSR) {
                    Shader.EnableKeyword("_SSR_ON");

                    if (mSSRType == SSRType.SSR && mSSRPass.Setup(ref mSSRSettings, ref mSSRMaterial)) {
                        renderer.EnqueuePass(mSSRPass);
                    }

                    if (mSSRType == SSRType.StochasticSSR && mSSSRPass.Setup(ref mSSSRSettings, ref mSSSRMaterial)) {
                        renderer.EnqueuePass(mSSSRPass);
                    }

                    if (mSSRType == SSRType.SSPR && mSSPRPass.Setup(ref mSSPRSettings, ref mSSPRComputeShader)) {
                        renderer.EnqueuePass(mSSPRPass);
                        Shader.EnableKeyword("_SSPR_ON");
                    }
                    else {
                        Shader.DisableKeyword("_SSPR_ON");
                    }
                }
                else {
                    Shader.DisableKeyword("_SSR_ON");
                    Shader.DisableKeyword("_SSPR_ON"); 
                }

                // AO
                if (mSSGISettings.AO) {
                    if (mAOType == AOType.SSAO && mSSAOPass.Setup(ref mSSAOSettings, ref renderer, ref mSSAOMaterial)) {
                        renderer.EnqueuePass(mSSAOPass);
                    }

                    if (mAOType == AOType.HBAO && mHBAOPass.Setup(ref mHBAOSettings, ref mHBAOMaterial)) {
                        renderer.EnqueuePass(mHBAOPass);
                    }
                }

                // Anti-Aliasing
                if (mSSGISettings.AntiAliasing) {
                    if (mAntiAliasingType == AntiAliasingType.FXAA && mFXAAPass.Setup(ref mFXAASettings, ref mFXAAMaterial)) {
                        renderer.EnqueuePass(mFXAAPass);
                    }

                    if (mAntiAliasingType == AntiAliasingType.TAA) {
                        if (mJitterPass.Setup(ref mTAASettings)) {
                            renderer.EnqueuePass(mJitterPass);
                        }

                        if (mTAAPass.Setup(ref mTAASettings, ref mTAAMaterial)) {
                            renderer.EnqueuePass(mTAAPass);
                        }
                    }
                }
            }
        }

        private bool GetMaterials() {
            // HiZBuffer
            if (ShouldAddHizPass()) {
                if (mHiZBufferShader == null)
                    mHiZBufferShader = Shader.Find(mHiZBufferShaderName);
                if (mHiZBufferMaterial == null && mHiZBufferShader != null)
                    mHiZBufferMaterial = CoreUtils.CreateEngineMaterial(mHiZBufferShader);
            }

            // ScreenSpaceIndirectDiffuse
            if (mSSGISettings.IndirectDiffuse) {
                if (mSSGIShader == null)
                    mSSGIShader = Shader.Find(mSSGIShaderName);
                if (mSSGIMaterial == null && mSSGIShader != null)
                    mSSGIMaterial = CoreUtils.CreateEngineMaterial(mSSGIShader);
            }

            // SSR
            if (mSSGISettings.SSR) {
                if (mSSRType == SSRType.SSR) {
                    if (mSSRShader == null)
                        mSSRShader = Shader.Find(mSSRShaderName);
                    if (mSSRMaterial == null && mSSRShader != null)
                        mSSRMaterial = CoreUtils.CreateEngineMaterial(mSSRShader);
                }

                if (mSSRType == SSRType.StochasticSSR) {
                    if (mSSSRShader == null)
                        mSSSRShader = Shader.Find(mSSSRShaderName);
                    if (mSSSRMaterial == null && mSSSRShader != null)
                        mSSSRMaterial = CoreUtils.CreateEngineMaterial(mSSSRShader);
                }
            }

            // AO
            if (mSSGISettings.AO) {
                if (mAOType == AOType.SSAO) {
                    if (mSSAOShader == null)
                        mSSAOShader = Shader.Find(mSSAOShaderName);
                    if (mSSAOMaterial == null && mSSAOShader != null)
                        mSSAOMaterial = CoreUtils.CreateEngineMaterial(mSSAOShader);
                }

                if (mAOType == AOType.HBAO) {
                    if (mHBAOShader == null)
                        mHBAOShader = Shader.Find(mHBAOShaderName);
                    if (mHBAOMaterial == null && mHBAOShader != null)
                        mHBAOMaterial = CoreUtils.CreateEngineMaterial(mHBAOShader);
                }
            }

            // Anti-Aliasing
            if (mSSGISettings.AntiAliasing) {
                if (mAntiAliasingType == AntiAliasingType.FXAA) {
                    if (mFXAAShader == null)
                        mFXAAShader = Shader.Find(mFXAAShaderName);
                    if (mFXAAMaterial == null && mFXAAShader != null)
                        mFXAAMaterial = CoreUtils.CreateEngineMaterial(mFXAAShader);
                }

                if (mAntiAliasingType == AntiAliasingType.TAA) {
                    if (mTAAShader == null)
                        mTAAShader = Shader.Find(mTAAShaderName);
                    if (mTAAMaterial == null && mTAAShader != null)
                        mTAAMaterial = CoreUtils.CreateEngineMaterial(mTAAShader);
                }
            }

            return (!ShouldAddHizPass() || mHiZBufferMaterial != null) &&
                   (!mSSGISettings.IndirectDiffuse || mSSGIMaterial != null) &&
                   (!mSSGISettings.SSR || (mSSRMaterial != null || mSSSRMaterial != null) || (mSSRMaterial == null && mSSSRMaterial == null)) &&
                   (!mSSGISettings.AO || mSSAOMaterial != null || mHBAOMaterial != null) &&
                   (!mSSGISettings.AntiAliasing || mFXAAMaterial != null || mTAAMaterial != null);
        }

        protected override void Dispose(bool disposing) {
            // CameraGBuffer
            DisposeCameraGBufferPasses();

            // HiZ Buffer
            CoreUtils.Destroy(mHiZBufferMaterial);
            mHiZBufferPass?.Dispose();
            mHiZBufferPass = null;

            // SSGI
            CoreUtils.Destroy(mSSGIMaterial);
            mSSGIPass?.Dispose();
            mSSGIPass = null;

            // SSR
            CoreUtils.Destroy(mSSRMaterial);
            mSSRPass?.Dispose();
            mSSRPass = null;

            // SSSR
            CoreUtils.Destroy(mSSSRMaterial);
            mSSSRPass?.Dispose();
            mSSSRPass = null;

            // SSPR
            mSSPRPass?.Dispose();
            mSSPRPass = null;

            // SSAO
            CoreUtils.Destroy(mSSAOMaterial);
            mSSAOPass?.Dispose();
            mSSAOPass = null;

            // HBAO
            CoreUtils.Destroy(mHBAOMaterial);
            mHBAOPass?.Dispose();
            mHBAOPass = null;

            // FXAA
            CoreUtils.Destroy(mFXAAMaterial);
            mFXAAPass?.Dispose();
            mFXAAPass = null;

            // TAA
            CoreUtils.Destroy(mTAAMaterial);
            mTAAPass?.Dispose();
            mJitterPass?.Dispose();
            mTAAPass = null;
            mJitterPass = null;
        }


        private bool GetComputeShaders() {
            if (mSSPRComputeShader == null)
                mSSPRComputeShader = (ComputeShader)Resources.Load(mSSPRComputeShaderName);
            return mSSPRComputeShader != null;
        }

        private void CreateCameraGBufferPasses() {
            if (mBaseColorGBufferPass == null) {
                mBaseColorGBufferPass = new BaseColorGBufferPass();
                mBaseColorGBufferPass.renderPassEvent = RenderPassEvent.AfterRenderingPrePasses;
            }

            if (mSpecularGBufferPass == null) {
                mSpecularGBufferPass = new SpecularGBufferPass();
                mSpecularGBufferPass.renderPassEvent = RenderPassEvent.AfterRenderingPrePasses;
            }

            if (mReflectionGBufferPass == null) {
                mReflectionGBufferPass = new ReflectionGBufferPass();
                mReflectionGBufferPass.renderPassEvent = RenderPassEvent.AfterRenderingPrePasses;
            }
        }

        private void AddCameraGBufferPasses(ref ScriptableRenderer renderer) {
            if (mSSGISettings.CameraGBuffer) {
                if (mCameraGBufferSettings.BaseColor && mBaseColorGBufferPass.Setup(ref mBaseColorGBufferSettings))
                    renderer.EnqueuePass(mBaseColorGBufferPass);

                if (mCameraGBufferSettings.Specular && mSpecularGBufferPass.Setup(ref mSpecularGBufferSettings))
                    renderer.EnqueuePass(mSpecularGBufferPass);

                if (mCameraGBufferSettings.Reflection && mReflectionGBufferPass.Setup(ref mReflectionGBufferSettings))
                    renderer.EnqueuePass(mReflectionGBufferPass);
            }
        }

        private void DisposeCameraGBufferPasses() {
            mBaseColorGBufferPass?.Dispose();
            mBaseColorGBufferPass = null;

            mSpecularGBufferPass?.Dispose();
            mSpecularGBufferPass = null;

            mReflectionGBufferPass?.Dispose();
            mReflectionGBufferPass = null;
        }

        private bool ShouldAddHizPass() => (mSSGISettings.SSR &&
                                            ((mSSRType == SSRType.SSR && mSSRSettings.AccelerateType == SSRAccelerateType.HierarchicalZBuffer) ||
                                             (mSSRType == SSRType.StochasticSSR))) ||
                                           (mSSGISettings.IndirectDiffuse);
    }
}