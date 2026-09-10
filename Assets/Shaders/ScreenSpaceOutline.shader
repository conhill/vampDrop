Shader "Vampire/ScreenSpaceOutline"
{
    // Full-screen edge-detection outline for URP 17 (RenderGraph). Detects depth and
    // normal discontinuities and draws a solid outline colour over them — a clean,
    // uniform border around every wall (and any solid geometry) regardless of mesh
    // pivots or normals. Driven by ScreenSpaceOutlineFeature.
    Properties
    {
        _OutlineColor     ("Outline Color", Color)          = (1.0, 0.8, 0.3, 1)
        _Thickness        ("Thickness (px)", Range(1, 4))   = 2.4
        // Relative depth-difference fraction that counts as an edge (robust to distance).
        _DepthThreshold   ("Depth Threshold", Range(0.001, 0.2)) = 0.03
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off
        Cull Off
        ZTest Always

        Pass
        {
            Name "ScreenSpaceOutline"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            float4 _OutlineColor;
            float  _Thickness;
            float  _DepthThreshold;

            float LinearDepth(float2 uv)
            {
                return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float2 uv    = IN.texcoord;
                float2 texel = (1.0 / _ScreenParams.xy) * _Thickness;

                // Roberts-cross sample offsets.
                float2 uvTL = uv + float2(-texel.x,  texel.y);
                float2 uvTR = uv + float2( texel.x,  texel.y);
                float2 uvBL = uv + float2(-texel.x, -texel.y);
                float2 uvBR = uv + float2( texel.x, -texel.y);

                // Depth-only edge, measured RELATIVE to the local depth so it works at any
                // distance and doesn't misfire on flat regions (unlike normal-buffer edges,
                // which read empty background as a discontinuity).
                float dC  = LinearDepth(uv);
                float dTL = LinearDepth(uvTL);
                float dTR = LinearDepth(uvTR);
                float dBL = LinearDepth(uvBL);
                float dBR = LinearDepth(uvBR);
                float diff = abs(dTL - dBR) + abs(dTR - dBL);
                float rel  = diff / max(dC, 0.0001);

                float edge = step(_DepthThreshold, rel);

                half4 sceneColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                return lerp(sceneColor, _OutlineColor, edge * _OutlineColor.a);
            }
            ENDHLSL
        }
    }
}
