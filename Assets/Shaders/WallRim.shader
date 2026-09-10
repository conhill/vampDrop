Shader "Vampire/WallRim"
{
    // Textured, normal-mapped wall with:
    //   • an OUTLINE pass (inverted hull) — a solid accent border around each wall's
    //     silhouette so walls pop off a dark background, and
    //   • a fresnel RIM on the lit face for edge sheen.
    // Drop-in for URP/Lit — reuses _BaseMap/_BumpMap so swapping an existing Lit
    // material's shader to this keeps its textures.
    Properties
    {
        _BaseMap      ("Base Map", 2D)              = "white" {}
        _BumpMap      ("Normal Map", 2D)            = "bump" {}
        _BaseColor    ("Base Color", Color)         = (0.55, 0.55, 0.60, 1)
        _NormalScale  ("Normal Scale", Range(0,2))  = 1
        _RimColor     ("Rim Color", Color)          = (1.0, 0.78, 0.35, 1)
        _RimPower     ("Rim Tightness", Range(0.5,10)) = 3.5
        _RimStrength  ("Rim Strength", Range(0,6))   = 2.0
        _Ambient      ("Ambient Fill", Range(0,1))   = 0.30
        _OutlineColor ("Outline Color", Color)       = (1.0, 0.80, 0.30, 1)
        _OutlineWidth ("Outline Width (world units)", Range(0,0.6)) = 0.18
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }

        // ── Pass 1: OUTLINE (inverted hull) ──────────────────────────────────
        // Renders back faces expanded outward along their screen-space normal, in a
        // flat colour. The lit pass then draws the wall on top, leaving only the
        // expanded edge visible → a constant-width border.
        Pass
        {
            Name "Outline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vertOutline
            #pragma fragment fragOutline
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float4 _RimColor;
                float  _NormalScale;
                float  _RimPower;
                float  _RimStrength;
                float  _Ambient;
                float4 _OutlineColor;
                float  _OutlineWidth;
            CBUFFER_END

            struct AttributesO { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct VaryingsO   { float4 positionHCS : SV_POSITION; };

            VaryingsO vertOutline (AttributesO IN)
            {
                VaryingsO OUT;
                // Push each vertex a fixed WORLD distance away from the mesh's centre.
                // Works on hard-edged boxes (unlike normal-based hull, whose back-face
                // normals point away from the camera and don't move on screen).
                float3 posWS    = TransformObjectToWorld(IN.positionOS.xyz);
                float3 centerWS = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
                float3 dir      = posWS - centerWS;
                float  len      = length(dir);
                if (len > 1e-4) posWS += (dir / len) * _OutlineWidth;
                OUT.positionHCS = TransformWorldToHClip(posWS);
                return OUT;
            }

            half4 fragOutline (VaryingsO IN) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }

        // ── Pass 2: LIT FACE + fresnel rim ───────────────────────────────────
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float3 tangentWS   : TEXCOORD2;
                float3 bitangentWS : TEXCOORD3;
                float3 positionWS  : TEXCOORD4;
            };

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float4 _RimColor;
                float  _NormalScale;
                float  _RimPower;
                float  _RimStrength;
                float  _Ambient;
                float4 _OutlineColor;
                float  _OutlineWidth;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs posn = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   norm = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);
                OUT.positionHCS = posn.positionCS;
                OUT.positionWS  = posn.positionWS;
                OUT.normalWS    = norm.normalWS;
                OUT.tangentWS   = norm.tangentWS;
                OUT.bitangentWS = norm.bitangentWS;
                OUT.uv          = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).rgb * _BaseColor.rgb;

                half3 nTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, IN.uv), _NormalScale);
                float3x3 TBN = float3x3(IN.tangentWS, IN.bitangentWS, IN.normalWS);
                float3 N = normalize(mul(nTS, TBN));

                float3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));

                Light mainLight = GetMainLight();
                half ndotl = saturate(dot(N, mainLight.direction));
                half3 lit  = albedo * (mainLight.color * ndotl + _Ambient);

                half rim = pow(1.0 - saturate(dot(N, V)), _RimPower) * _RimStrength;
                lit += _RimColor.rgb * rim;

                return half4(lit, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
