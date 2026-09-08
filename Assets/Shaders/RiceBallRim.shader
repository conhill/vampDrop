Shader "Vampire/RiceBallRim"
{
    // Bone/ivory bead with a thin gold Fresnel rim. Designed to be driven by
    // Graphics.DrawMeshInstanced (RiceBallRendererECS) — supports GPU instancing.
    Properties
    {
        _BaseColor  ("Base Color", Color) = (0.84, 0.80, 0.70, 1)   // bone / ivory
        _RimColor   ("Rim Color", Color)  = (0.82, 0.66, 0.32, 1)   // faint gold
        _RimPower   ("Rim Power", Range(0.5, 8)) = 3.2
        _RimStrength("Rim Strength", Range(0, 3)) = 1.15
        _Shade      ("Form Shading", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _RimColor;
                float  _RimPower;
                float  _RimStrength;
                float  _Shade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float3 viewWS      : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 posWS   = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(posWS);
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewWS     = GetWorldSpaceViewDir(posWS);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                float3 N = normalize(IN.normalWS);
                float3 V = normalize(IN.viewWS);

                // Soft form shading from a fixed key direction so the bead has volume
                float ndl = saturate(dot(N, normalize(float3(0.3, 0.85, 0.45))));
                float3 baseCol = _BaseColor.rgb * lerp(1.0, ndl, _Shade);

                // Thin gold border via Fresnel — the "rim"
                float fres = pow(1.0 - saturate(dot(N, V)), _RimPower);
                float3 rim = _RimColor.rgb * fres * _RimStrength;

                return half4(baseCol + rim, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
