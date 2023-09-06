using System;
using UnityEngine.Experimental.Rendering;

namespace UnityEngine.Rendering.Universal{
    [Serializable]
    internal class ScreenSpaceShadowsSettings{
        [SerializeField] internal bool HighQuality = false;
        [SerializeField] [Range(0.0f, 6.0f)] internal float PenumbraScale = 1.0f;
    }

    [DisallowMultipleRendererFeature("Screen Space Shadows")]
    [Tooltip("Screen Space Shadows")]
    internal class ScreenSpaceShadows : ScriptableRendererFeature{
#if UNITY_EDITOR
        [UnityEditor.ShaderKeywordFilter.SelectIf(true, keywordNames: ShaderKeywordStrings.MainLightShadowScreen)]
        private const bool k_RequiresScreenSpaceShadowsKeyword = true;
#endif

        // Serialized Fields
        [SerializeField, HideInInspector] private Shader m_Shader = null;
        [SerializeField] private ScreenSpaceShadowsSettings m_Settings = new ScreenSpaceShadowsSettings();

        // Private Fields
        private Material m_Material;
        private ScreenSpaceShadowsPass m_SSShadowsPass = null;
        private ScreenSpaceShadowsPostPass m_SSShadowsPostPass = null;

        // 添加一个自定义SSShadowsPass
        // private ComputeShader m_ComputeShader;d
        // private const string m_ComputeShaderName = "ScreenSpaceShadowsCompute";
        private Shader m_HighQualityShader;
        private Material m_HighQualityMaterial;
        private ScreenSpaceShadowsHighQualityPass m_SSShadowsHighQualityPass = null;

        // Constants
        private const string k_ShaderName = "Hidden/Universal Render Pipeline/ScreenSpaceShadows";
        private const string k_HighQualityShaderName = "Hidden/Universal Render Pipeline/ScreenSpaceShadowsHighQuality";

        /// <inheritdoc/>
        public override void Create() {
            if (m_SSShadowsPass == null)
                m_SSShadowsPass = new ScreenSpaceShadowsPass();
            if (m_SSShadowsPostPass == null)
                m_SSShadowsPostPass = new ScreenSpaceShadowsPostPass();
            if (m_SSShadowsHighQualityPass == null)
                m_SSShadowsHighQualityPass = new ScreenSpaceShadowsHighQualityPass();

            LoadMaterial();

            m_SSShadowsPass.renderPassEvent = RenderPassEvent.AfterRenderingGbuffer;
            m_SSShadowsHighQualityPass.renderPassEvent = RenderPassEvent.AfterRenderingGbuffer;
            m_SSShadowsPostPass.renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
        }

        /// <inheritdoc/>
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
            if (!LoadMaterial()) {
                Debug.LogErrorFormat(
                    "{0}.AddRenderPasses(): Missing material. {1} render pass will not be added. Check for missing reference in the renderer resources.",
                    GetType().Name, name);
                return;
            }

            var cameraType = renderingData.cameraData.camera.cameraType;

            bool allowMainLightShadows = renderingData.shadowData.supportsMainLightShadows && renderingData.lightData.mainLightIndex != -1;
            bool useHighQuality = m_Settings.HighQuality && (cameraType == CameraType.Game || cameraType == CameraType.SceneView);
            bool shouldEnqueue = allowMainLightShadows && useHighQuality ? m_SSShadowsHighQualityPass.Setup(m_Settings, ref m_HighQualityMaterial) : m_SSShadowsPass.Setup(m_Settings, m_Material);

            if (shouldEnqueue) {
                bool isDeferredRenderingMode = renderer is UniversalRenderer && ((UniversalRenderer)renderer).renderingModeRequested == RenderingMode.Deferred;

                m_SSShadowsPass.renderPassEvent = isDeferredRenderingMode
                    ? RenderPassEvent.AfterRenderingGbuffer
                    : RenderPassEvent.AfterRenderingPrePasses;

                if (useHighQuality)
                    renderer.EnqueuePass(m_SSShadowsHighQualityPass);
                else
                    renderer.EnqueuePass(m_SSShadowsPass);

                renderer.EnqueuePass(m_SSShadowsPostPass);
            }
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing) {
            m_SSShadowsPass?.Dispose();
            m_SSShadowsPass = null;
            m_SSShadowsHighQualityPass?.Dispose();
            m_SSShadowsHighQualityPass = null;
            CoreUtils.Destroy(m_Material);
        }

        private bool LoadMaterial() {
            if (m_Shader == null) {
                m_Shader = Shader.Find(k_ShaderName);
            }

            if (m_Material == null && m_Shader != null) {
                m_Material = CoreUtils.CreateEngineMaterial(m_Shader);
            }

            if (m_HighQualityShader == null) {
                m_HighQualityShader = Shader.Find(k_HighQualityShaderName);
            }

            if (m_HighQualityMaterial == null && m_HighQualityShader != null) {
                m_HighQualityMaterial = CoreUtils.CreateEngineMaterial(m_HighQualityShader);
            }

            return m_Material != null && m_HighQualityMaterial != null;
        }


        private class ScreenSpaceShadowsPass : ScriptableRenderPass{
            // Profiling tag
            private static string m_ProfilerTag = "ScreenSpaceShadows";
            private static ProfilingSampler m_ProfilingSampler = new ProfilingSampler(m_ProfilerTag);

            // Private Variables
            private Material m_Material;
            private ScreenSpaceShadowsSettings m_CurrentSettings;
            private RTHandle m_RenderTarget;

            internal ScreenSpaceShadowsPass() {
                m_CurrentSettings = new ScreenSpaceShadowsSettings();
            }

            public void Dispose() {
                m_RenderTarget?.Release();
            }

            internal bool Setup(ScreenSpaceShadowsSettings featureSettings, Material material) {
                m_CurrentSettings = featureSettings;
                m_Material = material;
                ConfigureInput(ScriptableRenderPassInput.Depth);

                return m_Material != null;
            }

            /// <inheritdoc/>
            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData) {
                var desc = renderingData.cameraData.cameraTargetDescriptor;
                desc.depthBufferBits = 0;
                desc.msaaSamples = 1;
                desc.graphicsFormat = RenderingUtils.SupportsGraphicsFormat(GraphicsFormat.R8_UNorm, FormatUsage.Linear | FormatUsage.Render)
                    ? GraphicsFormat.R8_UNorm
                    : GraphicsFormat.B8G8R8A8_UNorm;

                RenderingUtils.ReAllocateIfNeeded(ref m_RenderTarget, desc, FilterMode.Point, TextureWrapMode.Clamp, name: "_ScreenSpaceShadowmapTexture");
                cmd.SetGlobalTexture(m_RenderTarget.name, m_RenderTarget.nameID);

                ConfigureTarget(m_RenderTarget);
                ConfigureClear(ClearFlag.None, Color.white);
            }

            /// <inheritdoc/>
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) {
                if (m_Material == null) {
                    Debug.LogErrorFormat("{0}.Execute(): Missing material. ScreenSpaceShadows pass will not execute. Check for missing reference in the renderer resources.", GetType().Name);
                    return;
                }

                Camera camera = renderingData.cameraData.camera;

                var cmd = renderingData.commandBuffer;
                using (new ProfilingScope(cmd, m_ProfilingSampler)) {
                    Blitter.BlitCameraTexture(cmd, m_RenderTarget, m_RenderTarget, m_Material, 0);
                    CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadows, false);
                    CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadowCascades, false);
                    CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadowScreen, true);
                }
            }
        }

        internal class ScreenSpaceShadowsHighQualityPass : ScriptableRenderPass{
            private enum ShaderPass{
                PreScreenSpaceShadowmap,
                Blur,
                ScreenSpaceShadowmap,
            };

            // Profiling tag
            private static string m_ProfilerTag = "ScreenSpaceShadowsHighQuality";
            private static ProfilingSampler m_ProfilingSampler = new ProfilingSampler(m_ProfilerTag);

            // Private Variables
            private Material m_Material;
            private ScreenSpaceShadowsSettings m_CurrentSettings;
            private RTHandle m_ScreenSpaceShadowmapTexture, m_ScreenSpaceShadowmapTexture2;
            private RTHandle m_ScreenSpaceShadowmapMaskTexture, m_ScreenSpaceShadowmapMaskTexture2; // two blur
            private RTHandle m_CameraDepthTexture;
            private RenderTextureDescriptor m_ScreenSpaceShadowmapTextureDescriptor, m_ScreenSpaceShadowmapMaskTextureDescriptor;

            // ComputeShader Properties
            private const string m_KernelName = "GenerateSSShadowmap";
            private int m_KernelID;

            // private static readonly RTHandle k_CurrentActive = RTHandles.Alloc(BuiltinRenderTextureType.CurrentActive);

            private static readonly int m_ScreenSpaceShadowmapTextureSizeID = Shader.PropertyToID("_ScreenSpaceShadowmapTextureSize"),
                m_SSShadowmapBlurRadiusID = Shader.PropertyToID("_SSShadowmapBlurRadius"),
                m_ShadowPenumbraScaleID = Shader.PropertyToID("_ShadowPenumbraScale");

            public ScreenSpaceShadowsHighQualityPass() {
                m_CurrentSettings = new ScreenSpaceShadowsSettings();
            }

            public bool Setup(ScreenSpaceShadowsSettings featureSettings, ref Material material) {
                m_CurrentSettings = featureSettings;
                m_Material = material;
                ConfigureInput(ScriptableRenderPassInput.Depth);

                return m_Material != null;
            }

            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData) {
                m_ScreenSpaceShadowmapTextureDescriptor = renderingData.cameraData.cameraTargetDescriptor;
                m_ScreenSpaceShadowmapTextureDescriptor.depthBufferBits = 0;
                m_ScreenSpaceShadowmapTextureDescriptor.msaaSamples = 1;
                m_ScreenSpaceShadowmapTextureDescriptor.graphicsFormat = RenderingUtils.SupportsGraphicsFormat(GraphicsFormat.R8_UNorm, FormatUsage.Linear | FormatUsage.Render)
                    ? GraphicsFormat.R16_SNorm
                    : GraphicsFormat.B8G8R8A8_UNorm;
                m_ScreenSpaceShadowmapTextureDescriptor.enableRandomWrite = true;

                m_ScreenSpaceShadowmapMaskTextureDescriptor = m_ScreenSpaceShadowmapTextureDescriptor;
                m_ScreenSpaceShadowmapMaskTextureDescriptor.width = Math.Max(1, m_ScreenSpaceShadowmapMaskTextureDescriptor.width);
                m_ScreenSpaceShadowmapMaskTextureDescriptor.height = Math.Max(1, m_ScreenSpaceShadowmapMaskTextureDescriptor.height);


                RenderingUtils.ReAllocateIfNeeded(ref m_ScreenSpaceShadowmapTexture, m_ScreenSpaceShadowmapTextureDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: "_ScreenSpaceShadowmapTexture");
                cmd.SetGlobalTexture(m_ScreenSpaceShadowmapTexture.name, m_ScreenSpaceShadowmapTexture.nameID);

                RenderingUtils.ReAllocateIfNeeded(ref m_ScreenSpaceShadowmapTexture2, m_ScreenSpaceShadowmapTextureDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: "_ScreenSpaceShadowmapTexture2");
                cmd.SetGlobalTexture(m_ScreenSpaceShadowmapTexture2.name, m_ScreenSpaceShadowmapTexture2.nameID);

                RenderingUtils.ReAllocateIfNeeded(ref m_ScreenSpaceShadowmapMaskTexture, m_ScreenSpaceShadowmapMaskTextureDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_ScreenSpaceShadowmapMaskTexture");
                cmd.SetGlobalTexture(m_ScreenSpaceShadowmapMaskTexture.name, m_ScreenSpaceShadowmapMaskTexture.nameID);

                RenderingUtils.ReAllocateIfNeeded(ref m_ScreenSpaceShadowmapMaskTexture2, m_ScreenSpaceShadowmapMaskTextureDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_ScreenSpaceShadowmapMaskTexture2");
                cmd.SetGlobalTexture(m_ScreenSpaceShadowmapMaskTexture2.name, m_ScreenSpaceShadowmapMaskTexture2.nameID);

                m_Material.SetFloat(m_ShadowPenumbraScaleID, m_CurrentSettings.PenumbraScale);

                ConfigureTarget(m_ScreenSpaceShadowmapTexture);
                ConfigureClear(ClearFlag.None, Color.white);
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) {
                if (m_Material == null) {
                    Debug.LogErrorFormat("{0}.Execute(): Missing material. ScreenSpaceShadows pass will not execute. Check for missing reference in the renderer resources.", GetType().Name);
                    return;
                }

                Camera camera = renderingData.cameraData.camera;

                var cmd = renderingData.commandBuffer;
                using (new ProfilingScope(cmd, m_ProfilingSampler)) {
                    cmd.SetGlobalVector(m_ScreenSpaceShadowmapTextureSizeID, new Vector4(m_ScreenSpaceShadowmapTextureDescriptor.width, m_ScreenSpaceShadowmapTextureDescriptor.height, 1.0f / m_ScreenSpaceShadowmapTextureDescriptor.width, 1.0f / m_ScreenSpaceShadowmapTextureDescriptor.height));

                    // Pre-Shadowmapping
                    Blitter.BlitCameraTexture(cmd, m_ScreenSpaceShadowmapMaskTexture, m_ScreenSpaceShadowmapMaskTexture, m_Material, (int)ShaderPass.PreScreenSpaceShadowmap);
                    // Blur
                    float blurRadius = 4.0f;
                    cmd.SetGlobalVector(m_SSShadowmapBlurRadiusID, new Vector4(blurRadius, 0.0f, 0.0f, 0.0f));
                    Blitter.BlitCameraTexture(cmd, m_ScreenSpaceShadowmapMaskTexture, m_ScreenSpaceShadowmapMaskTexture2, m_Material, (int)ShaderPass.Blur);
                    cmd.SetGlobalVector(m_SSShadowmapBlurRadiusID, new Vector4(0.0f, blurRadius, 0.0f, 0.0f));
                    Blitter.BlitCameraTexture(cmd, m_ScreenSpaceShadowmapMaskTexture2, m_ScreenSpaceShadowmapMaskTexture, m_Material, (int)ShaderPass.Blur);

                    // ScreenSpaceShadowmap
                    Blitter.BlitCameraTexture(cmd, m_ScreenSpaceShadowmapTexture, m_ScreenSpaceShadowmapTexture, m_Material, (int)ShaderPass.ScreenSpaceShadowmap);
                    // Blur
                    blurRadius = 0.5f;
                    cmd.SetGlobalVector(m_SSShadowmapBlurRadiusID, new Vector4(blurRadius, 0.0f, 0.0f, 0.0f));
                    Blitter.BlitCameraTexture(cmd, m_ScreenSpaceShadowmapTexture, m_ScreenSpaceShadowmapTexture2, m_Material, (int)ShaderPass.Blur);
                    cmd.SetGlobalVector(m_SSShadowmapBlurRadiusID, new Vector4(0.0f, blurRadius, 0.0f, 0.0f));
                    Blitter.BlitCameraTexture(cmd, m_ScreenSpaceShadowmapTexture2, m_ScreenSpaceShadowmapTexture, m_Material, (int)ShaderPass.Blur);

                    CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadows, false);
                    CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadowCascades, false);
                    CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadowScreen, true);
                }
            }


            public void Dispose() {
                m_ScreenSpaceShadowmapTexture?.Release();
                m_ScreenSpaceShadowmapTexture2?.Release();
                m_ScreenSpaceShadowmapMaskTexture?.Release();
                m_ScreenSpaceShadowmapMaskTexture2?.Release();
            }
        }

        private class ScreenSpaceShadowsPostPass : ScriptableRenderPass{
            // Profiling tag
            private static string m_ProfilerTag = "ScreenSpaceShadows Post";
            private static ProfilingSampler m_ProfilingSampler = new ProfilingSampler(m_ProfilerTag);
            private static readonly RTHandle k_CurrentActive = RTHandles.Alloc(BuiltinRenderTextureType.CurrentActive);

            public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor) {
                ConfigureTarget(k_CurrentActive);
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) {
                var cmd = renderingData.commandBuffer;
                using (new ProfilingScope(cmd, m_ProfilingSampler)) {
                    ShadowData shadowData = renderingData.shadowData;
                    int cascadesCount = shadowData.mainLightShadowCascadesCount;
                    bool mainLightShadows = renderingData.shadowData.supportsMainLightShadows;
                    bool receiveShadowsNoCascade = mainLightShadows && cascadesCount == 1;
                    bool receiveShadowsCascades = mainLightShadows && cascadesCount > 1;

                    // Before transparent object pass, force to disable screen space shadow of main light
                    CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadowScreen, false);

                    // then enable main light shadows with or without cascades
                    CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadows, receiveShadowsNoCascade);
                    CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadowCascades, receiveShadowsCascades);
                }
            }
        }
    }
}