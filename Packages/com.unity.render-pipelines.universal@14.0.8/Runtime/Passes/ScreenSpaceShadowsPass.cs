// using System;
// using UnityEngine.Experimental.Rendering;
// using UnityEngine.Experimental.Rendering.RenderGraphModule;
//
// // TODO: 给UI添加Toggle 是否添加这个Pass
// namespace UnityEngine.Rendering.Universal.Internal{
//     public class ScreenSpaceShadowsPass : ScriptableRenderPass{
//         // Profiling tag
//         private static string m_ProfilerTag = "ScreenSpaceShadows";
//         private static ProfilingSampler m_ProfilingSampler = new ProfilingSampler(m_ProfilerTag);
//
//         // Private Variables
//         private ComputeShader m_ComputeShader;
//         private ScreenSpaceShadowsSettings m_CurrentSettings;
//         private RTHandle m_RenderTarget;
//         private RenderTextureDescriptor m_RTDescriptor;
//         private RenderTextureDescriptor m_MainLightShadowmapDescriptor;
//
//         private RTHandle m_MainLightShadowmapTexture;
//
//         // ComputeShader Properties
//         private const string m_KernelName = "GenerateSSShadowmap";
//         private int m_KernelID;
//
//         private static readonly int m_ScreenSpaceShadowmapTextureID = Shader.PropertyToID("_ScreenSpaceShadowmapTexture"),
//             m_MainLightShadowmapTextureID = Shader.PropertyToID("_MainLightShadowmapTexture"),
//             m_CameraDepthTextureID = Shader.PropertyToID("_CameraDepthTexture"),
//             m_ScreenSpaceShadowmapTextureSizeID = Shader.PropertyToID("_ScreenSpaceShadowmapTextureSize"),
//             m_ShadowmapTextureSizeID = Shader.PropertyToID("_ShadowmapTextureSize"),
//             m_ShadowCascadeResolutionID = Shader.PropertyToID("_ShadowCascadeResolution"),
//             m_CascadeShadowSplitSpheresArrayID = Shader.PropertyToID("_CascadeShadowSplitSpheresArray"),
//             m_MainLightWorldToShadowID = Shader.PropertyToID("_MainLightWorldToShadow"),
//             m_CascadeZDistanceArrayID = Shader.PropertyToID("_CascadeZDistanceArray");
//
//         private const int m_ThreadX = 8, m_ThreadY = 8;
//
//         public ScreenSpaceShadowsPass(RenderPassEvent evt) {
//             renderPassEvent = evt;
//         }
//
//         public bool Setup(ref RenderingData renderingData, ref ComputeShader sssCompute, ref RTHandle mainLightShadowmapTexture) {
//             m_ComputeShader = sssCompute;
//             m_MainLightShadowmapTexture = mainLightShadowmapTexture;
//             ConfigureInput(ScriptableRenderPassInput.Depth);
//
//             return m_ComputeShader != null;
//         }
//
//         public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData) {
//             m_RTDescriptor = renderingData.cameraData.cameraTargetDescriptor;
//             m_RTDescriptor.depthBufferBits = 0;
//             m_RTDescriptor.msaaSamples = 1;
//             m_RTDescriptor.graphicsFormat = RenderingUtils.SupportsGraphicsFormat(GraphicsFormat.R8_UNorm, FormatUsage.Linear | FormatUsage.Render)
//                 ? GraphicsFormat.R8_UNorm
//                 : GraphicsFormat.B8G8R8A8_UNorm;
//             m_RTDescriptor.enableRandomWrite = true;
//
//             m_MainLightShadowmapDescriptor = m_MainLightShadowmapTexture.rt.descriptor;
//
//             RenderingUtils.ReAllocateIfNeeded(ref m_RenderTarget, m_RTDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: "_ScreenSpaceShadowmapTexture");
//             cmd.SetGlobalTexture(m_RenderTarget.name, m_RenderTarget.nameID);
//
//             m_KernelID = m_ComputeShader.FindKernel(m_KernelName);
//
//             ConfigureTarget(m_RenderTarget);
//             ConfigureClear(ClearFlag.None, Color.white);
//         }
//
//         public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) {
//             if (m_ComputeShader == null) {
//                 Debug.LogErrorFormat("{0}.Execute(): Missing computeShader. ScreenSpaceShadows pass will not execute. Check for missing reference in the renderer resources.", GetType().Name);
//                 return;
//             }
//
//             Camera camera = renderingData.cameraData.camera;
//
//             var cmd = renderingData.commandBuffer;
//             using (new ProfilingScope(cmd, m_ProfilingSampler)) {
//                 int groupX = Mathf.CeilToInt((float)m_RTDescriptor.width / m_ThreadX), groupY = Mathf.CeilToInt((float)m_RTDescriptor.height / m_ThreadY);
//
//                 cmd.SetComputeTextureParam(m_ComputeShader, m_KernelID, m_ScreenSpaceShadowmapTextureID, m_RenderTarget);
//                 // cmd.SetComputeTextureParam(m_ComputeShader, m_KernelID, m_MainLightShadowmapTextureID, m_MainLightShadowmapTexture);
//                 cmd.SetComputeTextureParam(m_ComputeShader, m_KernelID, m_CameraDepthTextureID, renderingData.cameraData.renderer.cameraDepthTargetHandle);
//                 cmd.SetComputeVectorParam(m_ComputeShader, m_ScreenSpaceShadowmapTextureSizeID, new Vector4(m_RTDescriptor.width, m_RTDescriptor.height, 1.0f / (m_RTDescriptor.width - 1), 1.0f / (m_RTDescriptor.height - 1)));
//                 cmd.SetComputeVectorParam(m_ComputeShader, m_ShadowmapTextureSizeID, new Vector4(m_MainLightShadowmapDescriptor.width, m_MainLightShadowmapDescriptor.height, 1.0f / (m_MainLightShadowmapDescriptor.width - 1), 1.0f / (m_MainLightShadowmapDescriptor.height - 1)));
//                 // cmd.SetComputeVectorParam();
//
//
//                 cmd.DispatchCompute(m_ComputeShader, m_KernelID, groupX, groupY, 1);
//
//                 // Blitter.BlitCameraTexture(cmd, m_RenderTarget, m_RenderTarget, m_Material, 0);
//                 CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadows, false);
//                 CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadowCascades, false);
//                 CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadowScreen, true);
//             }
//         }
//
//
//         public void Dispose() {
//             m_RenderTarget?.Release();
//         }
//     }
//
//     public class ScreenSpaceShadowsPostPass : ScriptableRenderPass{
//         // Profiling tag
//         private static string m_ProfilerTag = "ScreenSpaceShadows Post";
//         private static ProfilingSampler m_ProfilingSampler = new ProfilingSampler(m_ProfilerTag);
//         private static readonly RTHandle k_CurrentActive = RTHandles.Alloc(BuiltinRenderTextureType.CurrentActive);
//
//         public ScreenSpaceShadowsPostPass(RenderPassEvent evt) {
//             renderPassEvent = evt;
//         }
//
//         public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData) {
//             ConfigureTarget(k_CurrentActive);
//         }
//
//         public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) {
//             var cmd = renderingData.commandBuffer;
//             using (new ProfilingScope(cmd, m_ProfilingSampler)) {
//                 ShadowData shadowData = renderingData.shadowData;
//                 int cascadesCount = shadowData.mainLightShadowCascadesCount;
//                 bool mainLightShadows = renderingData.shadowData.supportsMainLightShadows;
//                 bool receiveShadowsNoCascade = mainLightShadows && cascadesCount == 1;
//                 bool receiveShadowsCascades = mainLightShadows && cascadesCount > 1;
//
//                 // Before transparent object pass, force to disable screen space shadow of main light
//                 CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadowScreen, false);
//
//                 // then enable main light shadows with or without cascades
//                 CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadows, receiveShadowsNoCascade);
//                 CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadowCascades, receiveShadowsCascades);
//             }
//         }
//     }
// }