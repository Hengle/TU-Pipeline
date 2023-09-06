using System;
using UnityEngine.Experimental.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal{
    /// <summary>
    /// Struct container for shadow slice data.
    /// </summary>
    public struct ShadowSliceData{
        /// <summary>
        /// The view matrix.
        /// </summary>
        public Matrix4x4 viewMatrix;

        /// <summary>
        /// The projection matrix.
        /// </summary>
        public Matrix4x4 projectionMatrix;

        /// <summary>
        /// The shadow transform matrix.
        /// </summary>
        public Matrix4x4 shadowTransform;

        /// <summary>
        /// The X offset to the shadow map.
        /// </summary>
        public int offsetX;

        /// <summary>
        /// The Y offset to the shadow map.
        /// </summary>
        public int offsetY;

        /// <summary>
        /// The maximum tile resolution in an Atlas.
        /// </summary>
        public int resolution;

        /// <summary>
        /// The shadow split data containing culling information.
        /// </summary>
        public ShadowSplitData splitData;

        /// <summary>
        /// Clears and resets the data.
        /// </summary>
        public void Clear() {
            viewMatrix = Matrix4x4.identity;
            projectionMatrix = Matrix4x4.identity;
            shadowTransform = Matrix4x4.identity;
            offsetX = offsetY = 0;
            resolution = 1024;
        }
    }

    /// <summary>
    /// Various utility functions used for shadows.
    /// </summary>
    public static class ShadowUtils{
        internal static readonly bool m_ForceShadowPointSampling;

        static ShadowUtils() {
            m_ForceShadowPointSampling = SystemInfo.graphicsDeviceType == GraphicsDeviceType.Metal &&
                                         GraphicsSettings.HasShaderDefine(Graphics.activeTier, BuiltinShaderDefine.UNITY_METAL_SHADOWS_USE_POINT_FILTERING);
        }

        /// <summary>
        /// Extracts the directional light matrix.
        /// </summary>
        /// <param name="cullResults"></param>
        /// <param name="shadowData"></param>
        /// <param name="shadowLightIndex"></param>
        /// <param name="cascadeIndex"></param>
        /// <param name="shadowmapWidth"></param>
        /// <param name="shadowmapHeight"></param>
        /// <param name="shadowResolution"></param>
        /// <param name="shadowNearPlane"></param>
        /// <param name="cascadeSplitDistance"></param>
        /// <param name="shadowSliceData"></param>
        /// <param name="viewMatrix"></param>
        /// <param name="projMatrix"></param>
        /// <returns></returns>
        public static bool ExtractDirectionalLightMatrix(ref RenderingData renderingData, int shadowLightIndex, int cascadeIndex, int shadowmapWidth, int shadowmapHeight, int shadowResolution, float shadowNearPlane,
            out Vector4 cascadeSplitDistance, out ShadowSliceData shadowSliceData, out float zDistance, out Matrix4x4 viewMatrix, out Matrix4x4 projMatrix) {
            bool result = ExtractDirectionalLightMatrix(ref renderingData, shadowLightIndex, cascadeIndex, shadowmapWidth, shadowmapHeight, shadowResolution, shadowNearPlane, out cascadeSplitDistance, out shadowSliceData, out zDistance);
            viewMatrix = shadowSliceData.viewMatrix;
            projMatrix = shadowSliceData.projectionMatrix;
            return result;
        }

        /// <summary>
        /// Extracts the directional light matrix.
        /// </summary>
        /// <param name="cullResults"></param>
        /// <param name="shadowData"></param>
        /// <param name="shadowLightIndex"></param>
        /// <param name="cascadeIndex"></param>
        /// <param name="shadowmapWidth"></param>
        /// <param name="shadowmapHeight"></param>
        /// <param name="shadowResolution"></param>
        /// <param name="shadowNearPlane"></param>
        /// <param name="cascadeSplitDistance"></param>
        /// <param name="shadowSliceData"></param>
        /// <returns></returns>
        
        // !!Important!! 计算包围球，以及世界空间灯光到shadowmap的变换矩阵
        public static bool ExtractDirectionalLightMatrix(ref RenderingData renderingData, int shadowLightIndex, int cascadeIndex, int shadowmapWidth, int shadowmapHeight, int shadowResolution, float shadowNearPlane,
            out Vector4 cascadeSplitDistance, out ShadowSliceData shadowSliceData, out float zDistance) {
            // bool success = cullResults.ComputeDirectionalShadowMatricesAndCullingPrimitives(shadowLightIndex,
            //     cascadeIndex, shadowData.mainLightShadowCascadesCount, shadowData.mainLightShadowCascadesSplitArray, shadowResolution, shadowNearPlane, out shadowSliceData.viewMatrix, out shadowSliceData.projectionMatrix,
            //     out shadowSliceData.splitData);

            shadowSliceData = new ShadowSliceData();

            Vector4 cullingSphere;
            Light light = renderingData.cullResults.visibleLights.UnsafeElementAt(0).light;

            bool success = ComputeDirectionalShadowMatricesAndCullingSphere(ref renderingData.cameraData, ref renderingData.shadowData, cascadeIndex, light, shadowResolution,
                out cullingSphere, out shadowSliceData.viewMatrix, out shadowSliceData.projectionMatrix, out zDistance);

            shadowSliceData.splitData.cullingSphere = cullingSphere;
            cascadeSplitDistance = shadowSliceData.splitData.cullingSphere;
            shadowSliceData.offsetX = (cascadeIndex % 2) * shadowResolution;
            shadowSliceData.offsetY = (cascadeIndex / 2) * shadowResolution;
            shadowSliceData.resolution = shadowResolution;
            shadowSliceData.shadowTransform = GetShadowTransform(shadowSliceData.projectionMatrix, shadowSliceData.viewMatrix);

            // It is the culling sphere radius multiplier for shadow cascade blending
            // If this is less than 1.0, then it will begin to cull castors across cascades
            shadowSliceData.splitData.shadowCascadeBlendCullingFactor = 1.0f;

            // If we have shadow cascades baked into the atlas we bake cascade transform
            // in each shadow matrix to save shader ALU and L/S
            if (renderingData.shadowData.mainLightShadowCascadesCount > 1)
                ApplySliceTransform(ref shadowSliceData, shadowmapWidth, shadowmapHeight);

            return success;
        }

        static float s_camNear = 0; // 相机近平面深度
        static Vector3 s_camCenter = Vector3.zero; // 相机中心位置
        static Vector3 s_BL_Dir = Vector3.zero; // 相机中心到左下角向量
        static Vector3 s_TR_Dir = Vector3.zero; // 相机中心到右上角向量

        public static bool ComputeDirectionalShadowMatricesAndCullingSphere
        (ref CameraData cameraData, ref ShadowData shadowData, int cascadeIndex, Light light, int shadowResolution
            , out Vector4 cullingSphere, out Matrix4x4 viewMatrix, out Matrix4x4 projMatrix, out float ZDistance) {
            // 每帧的第0级，初始化数据
            if (cascadeIndex == 0) {
                s_camNear = cameraData.camera.nearClipPlane;
                Matrix4x4 VP = cameraData.GetProjectionMatrix() * cameraData.GetViewMatrix();
                Matrix4x4 I_VP = VP.inverse;

                // 把裁剪空间近平面点转换回世界空间中
                Vector3 nearBL = I_VP.MultiplyPoint(new Vector3(-1, -1, -1));
                Vector3 nearTR = I_VP.MultiplyPoint(new Vector3(1, 1, -1));

                // 计算世界空间坐标和向量
                s_camCenter = cameraData.camera.transform.position;
                s_BL_Dir = nearBL - s_camCenter;
                s_TR_Dir = nearTR - s_camCenter;
            }

            // 计算当前Cascade的远近平面深度
            float cascadeFar = s_camNear + cameraData.maxShadowDistance * shadowData.mainLightShadowCascadesSplitArray[cascadeIndex];
            // 计算当前Cascade的开始深度
            float cascadeNear = s_camNear;
            if (cascadeIndex > 0) {
                cascadeNear = s_camNear + cameraData.maxShadowDistance * shadowData.mainLightShadowCascadesSplitArray[cascadeIndex - 1];
            }

            // 利用Cascade远近平面深度重建出世界空间下，左下角和右上角的位置
            Vector3 cascadeNearBL = s_camCenter + s_BL_Dir / s_camNear * cascadeNear;
            Vector3 cascadeNearTR = s_camCenter + s_TR_Dir / s_camNear * cascadeNear;

            Vector3 cascadeFarBL = s_camCenter + s_BL_Dir / s_camNear * cascadeFar;
            Vector3 cascadeFarTR = s_camCenter + s_TR_Dir / s_camNear * cascadeFar;

            // 最大球形包围盒
            float a = Vector3.Distance(cascadeNearBL, cascadeNearTR); // 锥体近平面直径
            float b = Vector3.Distance(cascadeFarBL, cascadeFarTR); // 锥体远平面直径
            float l = cascadeFar - cascadeNear; // 锥体深度
            float x = (b * b - a * a) / (8 * l) + l / 2; // 近平面到原点的深度（勾股定理方程）
            Vector3 sphereCenter = cameraData.camera.transform.position + cameraData.camera.transform.forward * (cascadeNear + x); // 球体中心坐标
            float sphereR = Mathf.Sqrt(x * x + a * a / 4); // 球体半径

            // 对前4个Cascade进行偏移防止抖动
            if (cascadeIndex < 4 && light != null) {
                float squrePixelWidth = 2 * sphereR / shadowResolution;
                Vector3 sphereCenterLS = light.transform.worldToLocalMatrix.MultiplyPoint(sphereCenter);
                sphereCenterLS.x /= squrePixelWidth;
                sphereCenterLS.x = Mathf.Floor(sphereCenterLS.x);
                sphereCenterLS.x *= squrePixelWidth;
                sphereCenterLS.y /= squrePixelWidth;
                sphereCenterLS.y = Mathf.Floor(sphereCenterLS.y);
                sphereCenterLS.y *= squrePixelWidth;
                sphereCenter = light.transform.localToWorldMatrix.MultiplyPoint(sphereCenterLS);
            }

            // 构建包围球数据
            cullingSphere.x = sphereCenter.x;
            cullingSphere.y = sphereCenter.y;
            cullingSphere.z = sphereCenter.z;
            cullingSphere.w = sphereR;

            // 计算阴影正交相机向后偏移的距离 以覆盖所有物体
            float backDistance = sphereR * light.shadowNearPlane * 10;
            // float backDistance = Vector3.Distance(Vector3.zero, sphereCenter) + sphereR * light.shadowNearPlane;

            // 计算平行光的正交相机VP矩阵
            Vector3 shadowMapEye = sphereCenter - light.transform.forward * backDistance;
            Vector3 shadowMapAt = sphereCenter;

            Matrix4x4 lookMatrix = Matrix4x4.LookAt(shadowMapEye, shadowMapAt, light.transform.up);
            // Matrix that mirrors along Z axis, to match the camera space convention.
            Matrix4x4 scaleMatrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(1, 1, -1));
            // Final view matrix is inverse of the LookAt matrix, and then mirrored along Z.
            viewMatrix = scaleMatrix * lookMatrix.inverse;

            // 构造正交投影矩阵
            projMatrix = Matrix4x4.Ortho(-sphereR, sphereR, -sphereR, sphereR, 0.0f, 2.0f * backDistance);
            ZDistance = 2.0f * backDistance;

            return true;
        }

        /// <summary>
        /// Extracts the spot light matrix.
        /// </summary>
        /// <param name="cullResults"></param>
        /// <param name="shadowData"></param>
        /// <param name="shadowLightIndex"></param>
        /// <param name="shadowMatrix"></param>
        /// <param name="viewMatrix"></param>
        /// <param name="projMatrix"></param>
        /// <param name="splitData"></param>
        /// <returns></returns>
        public static bool ExtractSpotLightMatrix(ref CullingResults cullResults, ref ShadowData shadowData, int shadowLightIndex, out Matrix4x4 shadowMatrix, out Matrix4x4 viewMatrix, out Matrix4x4 projMatrix, out ShadowSplitData splitData) {
            bool success = cullResults.ComputeSpotShadowMatricesAndCullingPrimitives(shadowLightIndex, out viewMatrix, out projMatrix, out splitData); // returns false if input parameters are incorrect (rare)
            shadowMatrix = GetShadowTransform(projMatrix, viewMatrix);
            return success;
        }

        /// <summary>
        /// Extracts the spot light matrix.
        /// </summary>
        /// <param name="cullResults"></param>
        /// <param name="shadowData"></param>
        /// <param name="shadowLightIndex"></param>
        /// <param name="cubemapFace"></param>
        /// <param name="fovBias"></param>
        /// <param name="shadowMatrix"></param>
        /// <param name="viewMatrix"></param>
        /// <param name="projMatrix"></param>
        /// <param name="splitData"></param>
        /// <returns></returns>
        public static bool ExtractPointLightMatrix(ref CullingResults cullResults, ref ShadowData shadowData, int shadowLightIndex, CubemapFace cubemapFace, float fovBias, out Matrix4x4 shadowMatrix, out Matrix4x4 viewMatrix, out Matrix4x4 projMatrix, out ShadowSplitData splitData) {
            bool success = cullResults.ComputePointShadowMatricesAndCullingPrimitives(shadowLightIndex, cubemapFace, fovBias, out viewMatrix, out projMatrix, out splitData); // returns false if input parameters are incorrect (rare)

            // In native API CullingResults.ComputeSpotShadowMatricesAndCullingPrimitives there is code that inverts the 3rd component of shadow-casting spot light's "world-to-local" matrix (it was so since its original addition to the code base):
            // https://github.cds.internal.unity3d.com/unity/unity/commit/34813e063526c4be0ef0448dfaae3a911dd8be58#diff-cf0b417fc6bd8ee2356770797e628cd4R331
            // (the same transformation has also always been used in the Built-In Render Pipeline)
            //
            // However native API CullingResults.ComputePointShadowMatricesAndCullingPrimitives does not contain this transformation.
            // As a result, the view matrices returned for a point light shadow face, and for a spot light with same direction as that face, have opposite 3rd component.
            //
            // This causes normalBias to be incorrectly applied to shadow caster vertices during the point light shadow pass.
            // To counter this effect, we invert the point light shadow view matrix component here:
            {
                viewMatrix.m10 = -viewMatrix.m10;
                viewMatrix.m11 = -viewMatrix.m11;
                viewMatrix.m12 = -viewMatrix.m12;
                viewMatrix.m13 = -viewMatrix.m13;
            }

            shadowMatrix = GetShadowTransform(projMatrix, viewMatrix);
            return success;
        }

        /// <summary>
        /// Renders shadows to a shadow slice.
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="context"></param>
        /// <param name="shadowSliceData"></param>
        /// <param name="settings"></param>
        /// <param name="proj"></param>
        /// <param name="view"></param>
        public static void RenderShadowSlice(CommandBuffer cmd, ref ScriptableRenderContext context,
            ref ShadowSliceData shadowSliceData, ref ShadowDrawingSettings settings,
            Matrix4x4 proj, Matrix4x4 view) {
            cmd.SetGlobalDepthBias(1.0f, 2.5f); // these values match HDRP defaults (see https://github.com/Unity-Technologies/Graphics/blob/9544b8ed2f98c62803d285096c91b44e9d8cbc47/com.unity.render-pipelines.high-definition/Runtime/Lighting/Shadow/HDShadowAtlas.cs#L197 )

            cmd.SetViewport(new Rect(shadowSliceData.offsetX, shadowSliceData.offsetY, shadowSliceData.resolution, shadowSliceData.resolution));
            cmd.SetViewProjectionMatrices(view, proj);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            context.DrawShadows(ref settings);
            cmd.DisableScissorRect();
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            cmd.SetGlobalDepthBias(0.0f, 0.0f); // Restore previous depth bias values
        }

        /// <summary>
        /// Renders shadows to a shadow slice.
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="context"></param>
        /// <param name="shadowSliceData"></param>
        /// <param name="settings"></param>
        public static void RenderShadowSlice(CommandBuffer cmd, ref ScriptableRenderContext context,
            ref ShadowSliceData shadowSliceData, ref ShadowDrawingSettings settings) {
            RenderShadowSlice(cmd, ref context, ref shadowSliceData, ref settings,
                shadowSliceData.projectionMatrix, shadowSliceData.viewMatrix);
        }

        /// <summary>
        /// Calculates the maximum tile resolution in an Atlas.
        /// </summary>
        /// <param name="atlasWidth"></param>
        /// <param name="atlasHeight"></param>
        /// <param name="tileCount"></param>
        /// <returns>The maximum tile resolution in an Atlas.</returns>
        public static int GetMaxTileResolutionInAtlas(int atlasWidth, int atlasHeight, int tileCount) {
            int resolution = Mathf.Min(atlasWidth, atlasHeight);

            int currentTileCount = atlasWidth / resolution * atlasHeight / resolution;
            if (currentTileCount < tileCount) {
                resolution = resolution >> 1;
                currentTileCount = atlasWidth / resolution * atlasHeight / resolution;
            }

            return resolution;
        }

        /// <summary>
        /// Used for baking bake cascade transforms in each shadow matrix.
        /// </summary>
        /// <param name="shadowSliceData"></param>
        /// <param name="atlasWidth"></param>
        /// <param name="atlasHeight"></param>
        public static void ApplySliceTransform(ref ShadowSliceData shadowSliceData, int atlasWidth, int atlasHeight) {
            Matrix4x4 sliceTransform = Matrix4x4.identity;
            float oneOverAtlasWidth = 1.0f / atlasWidth;
            float oneOverAtlasHeight = 1.0f / atlasHeight;
            sliceTransform.m00 = shadowSliceData.resolution * oneOverAtlasWidth;
            sliceTransform.m11 = shadowSliceData.resolution * oneOverAtlasHeight;
            sliceTransform.m03 = shadowSliceData.offsetX * oneOverAtlasWidth;
            sliceTransform.m13 = shadowSliceData.offsetY * oneOverAtlasHeight;

            // Apply shadow slice scale and offset
            shadowSliceData.shadowTransform = sliceTransform * shadowSliceData.shadowTransform;
        }

        /// <summary>
        /// Calculates the depth and normal bias from a light.
        /// </summary>
        /// <param name="shadowLight"></param>
        /// <param name="shadowLightIndex"></param>
        /// <param name="shadowData"></param>
        /// <param name="lightProjectionMatrix"></param>
        /// <param name="shadowResolution"></param>
        /// <returns>The depth and normal bias from a visible light.</returns>
        public static Vector4 GetShadowBias(ref VisibleLight shadowLight, int shadowLightIndex, ref ShadowData shadowData, Matrix4x4 lightProjectionMatrix, float shadowResolution) {
            if (shadowLightIndex < 0 || shadowLightIndex >= shadowData.bias.Count) {
                Debug.LogWarning(string.Format("{0} is not a valid light index.", shadowLightIndex));
                return Vector4.zero;
            }

            float frustumSize;
            if (shadowLight.lightType == LightType.Directional) {
                // Frustum size is guaranteed to be a cube as we wrap shadow frustum around a sphere
                frustumSize = 2.0f / lightProjectionMatrix.m00;
            }
            else if (shadowLight.lightType == LightType.Spot) {
                // For perspective projections, shadow texel size varies with depth
                // It will only work well if done in receiver side in the pixel shader. Currently UniversalRP
                // do bias on caster side in vertex shader. When we add shader quality tiers we can properly
                // handle this. For now, as a poor approximation we do a constant bias and compute the size of
                // the frustum as if it was orthogonal considering the size at mid point between near and far planes.
                // Depending on how big the light range is, it will be good enough with some tweaks in bias
                frustumSize = Mathf.Tan(shadowLight.spotAngle * 0.5f * Mathf.Deg2Rad) * shadowLight.range; // half-width (in world-space units) of shadow frustum's "far plane"
            }
            else if (shadowLight.lightType == LightType.Point) {
                // [Copied from above case:]
                // "For perspective projections, shadow texel size varies with depth
                //  It will only work well if done in receiver side in the pixel shader. Currently UniversalRP
                //  do bias on caster side in vertex shader. When we add shader quality tiers we can properly
                //  handle this. For now, as a poor approximation we do a constant bias and compute the size of
                //  the frustum as if it was orthogonal considering the size at mid point between near and far planes.
                //  Depending on how big the light range is, it will be good enough with some tweaks in bias"
                // Note: HDRP uses normalBias both in HDShadowUtils.CalcGuardAnglePerspective and HDShadowAlgorithms/EvalShadow_NormalBias (receiver bias)
                float fovBias = Internal.AdditionalLightsShadowCasterPass.GetPointLightShadowFrustumFovBiasInDegrees((int)shadowResolution, (shadowLight.light.shadows == LightShadows.Soft));
                // Note: the same fovBias was also used to compute ShadowUtils.ExtractPointLightMatrix
                float cubeFaceAngle = 90 + fovBias;
                frustumSize = Mathf.Tan(cubeFaceAngle * 0.5f * Mathf.Deg2Rad) * shadowLight.range; // half-width (in world-space units) of shadow frustum's "far plane"
            }
            else {
                Debug.LogWarning("Only point, spot and directional shadow casters are supported in universal pipeline");
                frustumSize = 0.0f;
            }

            // depth and normal bias scale is in shadowmap texel size in world space
            float texelSize = frustumSize / shadowResolution;
            float depthBias = -shadowData.bias[shadowLightIndex].x * texelSize;
            float normalBias = -shadowData.bias[shadowLightIndex].y * texelSize;

            // The current implementation of NormalBias in Universal RP is the same as in Unity Built-In RP (i.e moving shadow caster vertices along normals when projecting them to the shadow map).
            // This does not work well with Point Lights, which is why NormalBias value is hard-coded to 0.0 in Built-In RP (see value of unity_LightShadowBias.z in FrameDebugger, and native code that sets it: https://github.cds.internal.unity3d.com/unity/unity/blob/a9c916ba27984da43724ba18e70f51469e0c34f5/Runtime/Camera/Shadows.cpp#L1686 )
            // We follow the same convention in Universal RP:
            if (shadowLight.lightType == LightType.Point)
                normalBias = 0.0f;

            if (shadowData.supportsSoftShadows && shadowLight.light.shadows == LightShadows.Soft) {
                SoftShadowQuality softShadowQuality = SoftShadowQuality.Medium;
                if (shadowLight.light.TryGetComponent(out UniversalAdditionalLightData additionalLightData))
                    softShadowQuality = additionalLightData.softShadowQuality;

                // TODO: depth and normal bias assume sample is no more than 1 texel away from shadowmap
                // This is not true with PCF. Ideally we need to do either
                // cone base bias (based on distance to center sample)
                // or receiver place bias based on derivatives.
                // For now we scale it by the PCF kernel size of non-mobile platforms (5x5)
                float kernelRadius = 2.5f;

                switch (softShadowQuality) {
                    case SoftShadowQuality.High:
                        kernelRadius = 3.5f;
                        break; // 7x7
                    case SoftShadowQuality.Medium:
                        kernelRadius = 2.5f;
                        break; // 5x5
                    case SoftShadowQuality.Low:
                        kernelRadius = 1.5f;
                        break; // 3x3
                    default: break;
                }

                depthBias *= kernelRadius;
                normalBias *= kernelRadius;
            }

            return new Vector4(depthBias, normalBias, 0.0f, 0.0f);
        }

        /// <summary>
        /// Extract scale and bias from a fade distance to achieve a linear fading of the fade distance.
        /// </summary>
        /// <param name="fadeDistance">Distance at which object should be totally fade</param>
        /// <param name="border">Normalized distance of fade</param>
        /// <param name="scale">[OUT] Slope of the fading on the fading part</param>
        /// <param name="bias">[OUT] Ordinate of the fading part at abscissa 0</param>
        internal static void GetScaleAndBiasForLinearDistanceFade(float fadeDistance, float border, out float scale, out float bias) {
            // To avoid division from zero
            // This values ensure that fade within cascade will be 0 and outside 1
            if (border < 0.0001f) {
                float multiplier = 1000f; // To avoid blending if difference is in fractions
                scale = multiplier;
                bias = -fadeDistance * multiplier;
                return;
            }

            border = 1 - border;
            border *= border;

            // Fade with distance calculation is just a linear fade from 90% of fade distance to fade distance. 90% arbitrarily chosen but should work well enough.
            float distanceFadeNear = border * fadeDistance;
            scale = 1.0f / (fadeDistance - distanceFadeNear);
            bias = -distanceFadeNear / (fadeDistance - distanceFadeNear);
        }

        /// <summary>
        /// Sets up the shadow bias, light direction and position for rendering.
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="shadowLight"></param>
        /// <param name="shadowBias"></param>
        public static void SetupShadowCasterConstantBuffer(CommandBuffer cmd, ref VisibleLight shadowLight, Vector4 shadowBias) {
            cmd.SetGlobalVector("_ShadowBias", shadowBias);

            // Light direction is currently used in shadow caster pass to apply shadow normal offset (normal bias).
            Vector3 lightDirection = -shadowLight.localToWorldMatrix.GetColumn(2);
            cmd.SetGlobalVector("_LightDirection", new Vector4(lightDirection.x, lightDirection.y, lightDirection.z, 0.0f));

            // For punctual lights, computing light direction at each vertex position provides more consistent results (shadow shape does not change when "rotating the point light" for example)
            Vector3 lightPosition = shadowLight.localToWorldMatrix.GetColumn(3);
            cmd.SetGlobalVector("_LightPosition", new Vector4(lightPosition.x, lightPosition.y, lightPosition.z, 1.0f));
        }

        private static RenderTextureDescriptor GetTemporaryShadowTextureDescriptor(int width, int height, int bits) {
            var format = Experimental.Rendering.GraphicsFormatUtility.GetDepthStencilFormat(bits, 0);
            RenderTextureDescriptor rtd = new RenderTextureDescriptor(width, height, Experimental.Rendering.GraphicsFormat.None, format);
            rtd.shadowSamplingMode = (RenderingUtils.SupportsRenderTextureFormat(RenderTextureFormat.Shadowmap)
                                      && (SystemInfo.graphicsDeviceType != GraphicsDeviceType.OpenGLES2))
                ? ShadowSamplingMode.CompareDepths
                : ShadowSamplingMode.None;
            return rtd;
        }

        /// <summary>
        /// Gets a temporary render texture for shadows.
        /// This function has been deprecated. Use AllocShadowRT or ShadowRTReAllocateIfNeeded instead.
        /// </summary>
        /// <param name="width">The width of the texture.</param>
        /// <param name="height">The height of the texture.</param>
        /// <param name="bits">The number of depth bits.</param>
        /// <returns>A shadow render texture.</returns>
        [Obsolete("Use AllocShadowRT or ShadowRTReAllocateIfNeeded")]
        public static RenderTexture GetTemporaryShadowTexture(int width, int height, int bits) {
            var rtd = GetTemporaryShadowTextureDescriptor(width, height, bits);
            var shadowTexture = RenderTexture.GetTemporary(rtd);
            shadowTexture.filterMode = m_ForceShadowPointSampling ? FilterMode.Point : FilterMode.Bilinear;
            shadowTexture.wrapMode = TextureWrapMode.Clamp;
            return shadowTexture;
        }

        /// <summary>
        /// Return true if handle does not match the requirements
        /// </summary>
        /// <param name="handle">RTHandle to check (can be null).</param>
        /// <param name="width">Width of the RTHandle to match.</param>
        /// <param name="height">Height of the RTHandle to match.</param>
        /// <param name="bits">Depth bits of the RTHandle to match.</param>
        /// <param name="anisoLevel">Anisotropic filtering level of the RTHandle to match.</param>
        /// <param name="mipMapBias">Bias applied to mipmaps during filtering of the RTHandle to match.</param>
        /// <param name="name">Name of the RTHandle of the RTHandle to match.</param>
        /// <returns>If the RTHandle needs to be re-allocated</returns>
        public static bool ShadowRTNeedsReAlloc(RTHandle handle, int width, int height, int bits, int anisoLevel, float mipMapBias, string name) {
            if (handle == null || handle.rt == null)
                return true;
            var descriptor = GetTemporaryShadowTextureDescriptor(width, height, bits);
            if (m_ForceShadowPointSampling) {
                if (handle.rt.filterMode != FilterMode.Point)
                    return true;
            }
            else {
                if (handle.rt.filterMode != FilterMode.Bilinear)
                    return true;
            }

            TextureDesc shadowDesc = RTHandleResourcePool.CreateTextureDesc(descriptor, TextureSizeMode.Explicit, anisoLevel, mipMapBias, m_ForceShadowPointSampling ? FilterMode.Point : FilterMode.Bilinear, TextureWrapMode.Clamp, name);
            return RenderingUtils.RTHandleNeedsReAlloc(handle, shadowDesc, false);
        }

        /// <summary>
        /// Allocate a Shadow Map
        /// </summary>
        /// <param name="width">Width of the Shadow Map.</param>
        /// <param name="height">Height of the Shadow Map.</param>
        /// <param name="bits">Minimum depth bits of the Shadow Map.</param>
        /// <param name="anisoLevel">Anisotropic filtering level of the Shadow Map.</param>
        /// <param name="mipMapBias">Bias applied to mipmaps during filtering of the Shadow Map.</param>
        /// <param name="name">Name of the Shadow Map.</param>
        /// <returns>If an RTHandle for the Shadow Map</returns>
        public static RTHandle AllocShadowRT(int width, int height, int bits, int anisoLevel, float mipMapBias, string name) {
            var rtd = GetTemporaryShadowTextureDescriptor(width, height, bits);
            return RTHandles.Alloc(rtd, m_ForceShadowPointSampling ? FilterMode.Point : FilterMode.Bilinear, TextureWrapMode.Clamp, isShadowMap: true, name: name);
        }

        /// <summary>
        /// Allocate a Shadow Map or re-allocate if it doesn't match requirements.
        /// For use only if the map requirements changes at runtime.
        /// </summary>
        /// <param name="handle">RTHandle to check (can be null).</param>
        /// <param name="width">Width of the Shadow Map.</param>
        /// <param name="height">Height of the Shadow Map.</param>
        /// <param name="bits">Minimum depth bits of the Shadow Map.</param>
        /// <param name="anisoLevel">Anisotropic filtering level of the Shadow Map.</param>
        /// <param name="mipMapBias">Bias applied to mipmaps during filtering of the Shadow Map.</param>
        /// <param name="name">Name of the Shadow Map.</param>
        /// <returns>If the RTHandle was re-allocated</returns>
        public static bool ShadowRTReAllocateIfNeeded(ref RTHandle handle, int width, int height, int bits, int anisoLevel = 1, float mipMapBias = 0, string name = "") {
            if (ShadowRTNeedsReAlloc(handle, width, height, bits, anisoLevel, mipMapBias, name)) {
                handle?.Release();
                handle = AllocShadowRT(width, height, bits, anisoLevel, mipMapBias, name);
                return true;
            }

            return false;
        }

        static Matrix4x4 GetShadowTransform(Matrix4x4 proj, Matrix4x4 view) {
            // Currently CullResults ComputeDirectionalShadowMatricesAndCullingPrimitives doesn't
            // apply z reversal to projection matrix. We need to do it manually here.
            if (SystemInfo.usesReversedZBuffer) {
                proj.m20 = -proj.m20;
                proj.m21 = -proj.m21;
                proj.m22 = -proj.m22;
                proj.m23 = -proj.m23;
            }

            Matrix4x4 worldToShadow = proj * view;

            var textureScaleAndBias = Matrix4x4.identity;
            textureScaleAndBias.m00 = 0.5f;
            textureScaleAndBias.m11 = 0.5f;
            textureScaleAndBias.m22 = 0.5f;
            textureScaleAndBias.m03 = 0.5f;
            textureScaleAndBias.m23 = 0.5f;
            textureScaleAndBias.m13 = 0.5f;
            // textureScaleAndBias maps texture space coordinates from [-1,1] to [0,1]

            // Apply texture scale and offset to save a MAD in shader.
            return textureScaleAndBias * worldToShadow;
        }

        internal static float SoftShadowQualityToShaderProperty(Light light, bool softShadowsEnabled) {
            float softShadows = softShadowsEnabled ? 1.0f : 0.0f;
            if (light.TryGetComponent(out UniversalAdditionalLightData additionalLightData)) {
                var softShadowQuality = (additionalLightData.softShadowQuality == SoftShadowQuality.UsePipelineSettings)
                    ? UniversalRenderPipeline.asset?.softShadowQuality
                    : additionalLightData.softShadowQuality;
                softShadows *= Math.Max((int)softShadowQuality, (int)SoftShadowQuality.Low);
            }

            return softShadows;
        }
    }
}