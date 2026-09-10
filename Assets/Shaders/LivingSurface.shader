Shader "Vampire/LivingSurface"
{
    // One shader, layered looks — mix per material via toggles:
    //   • GRITTY  : PBR albedo + normal + mask (metallic/AO/smoothness), lit with
    //               URP main light + shadows + additional lights + SH ambient + SSAO.
    //   • DUNGEON : fresnel RIM sheen + inverted-hull OUTLINE pass (widths 0 to drop).
    //   • WEAR    : STATIC, world-space, procedural wear & tear — gravity water streaks,
    //               damp blotchy stains (darker AND glossier), base-of-wall grime, and
    //               cavity dirt from SSAO. Drives albedo AND smoothness so light reacts.
    //   • LIVING  : optional pulsing EMISSIVE layer (glowing panels / hazard strips).
    // Triplanar mapping (toggle) stops textures stretching on big untextured walls.
    // Reuses URP Lit property names so it slots onto existing Lit materials.
    //
    // Properties and the UnityPerMaterial CBUFFER live in LivingSurfaceInput.hlsl so all
    // five passes share one layout (an SRP Batcher requirement).
    Properties
    {
        [Header(Base PBR)]
        _BaseMap        ("Base Map", 2D)                 = "white" {}
        _BaseColor      ("Base Color", Color)            = (0.85, 0.85, 0.88, 1)
        _BumpMap        ("Normal Map", 2D)               = "bump" {}
        _NormalScale    ("Normal Scale", Range(0,3))     = 1
        _MaskMap        ("Mask (R metal,G AO,A smooth)", 2D) = "white" {}
        _Metallic       ("Metallic", Range(0,1))         = 0
        _Smoothness     ("Smoothness", Range(0,1))       = 0.3
        _OcclusionStr   ("Occlusion Strength", Range(0,1)) = 1

        [Header(Triplanar)]
        [Toggle(_TRIPLANAR)] _Triplanar ("Triplanar Mapping", Float) = 0
        _TriplanarScale ("Triplanar Tiling", Float)      = 0.35
        _TriplanarSharp ("Triplanar Blend Sharpness", Range(1,16)) = 6

        [Header(Stone Blockwork   stylised masonry courses)]
        [Toggle(_BLOCKS)] _BlocksOn ("Enable Stone Blocks", Float) = 0
        _MortarColor    ("Mortar Color", Color)          = (0.15, 0.14, 0.13, 1)
        _BlockScale     ("Blocks Per World Unit", Float) = 0.55
        _BlockAspect    ("Block Aspect (lower is wider)", Float) = 0.5
        _BlockOffset    ("Course Offset (running bond)", Range(0,1)) = 0.5
        _MortarWidth    ("Mortar Width", Range(0.01,0.4)) = 0.07
        _BlockIrregular ("Edge Irregularity", Range(0,0.3)) = 0.07
        _BlockEdgeScale ("Edge Noise Scale", Float)      = 1.5
        _BlockToneVar   ("Per Block Tone Variation", Range(0,1)) = 0.5
        _BlockBevelStrength ("Chamfer Strength", Range(0,2)) = 1.2
        _BlockBevel     ("Chamfer Width", Range(0.005,0.3)) = 0.05
        _BlockAO        ("Joint Contact Shadow", Range(0,1)) = 0.35
        _BlockAOWidth   ("Contact Shadow Width", Range(0.01,0.5)) = 0.22
        _BlockRowRand   ("Course Wander", Range(0,1))     = 0.35
        _BlockWidthRand ("Stone Width Variation", Range(0,1)) = 0.6
        _BlockHeightVar ("Per Stone Height Variation", Range(0,1)) = 0.4

        [Header(Parallax Depth   real self occluding relief)]
        [Toggle(_PARALLAX)] _ParallaxOn ("Enable Parallax Depth", Float) = 0
        _ParallaxDepth  ("Parallax Depth", Range(0,0.4))  = 0.12
        _ParallaxSteps  ("Parallax Quality (steps)", Range(4,64)) = 24

        [Header(Wear and Tear   needs a grayscale noise in Grime Map)]
        [Toggle(_WEAR)] _WearOn ("Enable Wear", Float)   = 0
        _GrimeMap       ("Grime / Noise Map (grayscale)", 2D) = "white" {}
        _WearAmount     ("Wear Amount", Range(0,1))      = 1
        _GrimeMaxDarken ("Max Albedo Darkening", Range(0,1)) = 0.7
        _GrimeColor     ("Grime Darken Tint", Color)     = (0.35, 0.32, 0.28, 1)
        _StainColor     ("Water Stain Tint", Color)      = (0.26, 0.21, 0.15, 1)
        _StainScale     ("Stain Blotch Scale", Float)    = 0.12
        _StainStrength  ("Stain Strength", Range(0,1))   = 0.6
        _StreakStrength ("Water Streak Strength", Range(0,1)) = 0.5
        _StreakTiling   ("Streak Density", Float)        = 0.5
        _StreakLength   ("Streak Length (world units)", Range(0.5,12)) = 4
        _BaseGrimeStrength ("Base Grime Strength", Range(0,1)) = 0.55
        _WearBaseY      ("Base Grime Height (world Y)", Float) = 3
        _WearBaseRange  ("Base Grime Falloff", Float)    = 3
        _WetSmoothness  ("Wet Stain Smoothness", Range(0,1)) = 0.6

        [Header(Macro Variation   breaks up big flat walls)]
        _MacroScale     ("Macro Blotch Scale", Float)     = 0.035
        _MacroStrength  ("Macro Blotch Strength", Range(0,1)) = 0.35

        [Header(Concrete Patches   lighter repaired sections)]
        _PatchColor     ("Patch Color", Color)           = (0.72, 0.70, 0.66, 1)
        _PatchScale     ("Patch Scale", Float)           = 0.055
        _PatchStrength  ("Patch Strength", Range(0,1))   = 0.45
        _PatchEdge      ("Patch Edge Hardness", Range(0.01,0.45)) = 0.10

        [Header(Cracks)]
        _CrackColor     ("Crack Color", Color)           = (0.05, 0.045, 0.04, 1)
        _CrackScale     ("Crack Scale", Float)           = 0.16
        _CrackWidth     ("Crack Width", Range(0.005,0.15)) = 0.035
        _CrackStrength  ("Crack Strength", Range(0,1))   = 0.65

        [Header(Efflorescence   salt bloom on damp concrete)]
        _EffloColor     ("Efflorescence Color", Color)   = (0.84, 0.83, 0.79, 1)
        _EffloStrength  ("Efflorescence Strength", Range(0,1)) = 0.4

        [Header(Rust   rebar bleed on some streak columns)]
        _RustColor      ("Rust Color", Color)            = (0.36, 0.16, 0.07, 1)
        _RustStrength   ("Rust Strength", Range(0,1))    = 0.45

        [Header(Wear Relief   makes damage catch the light)]
        _WearBumpStrength ("Wear Bump Strength", Range(0,2)) = 0.7
        _WearBumpEps      ("Wear Bump Sample Offset", Range(0.002,0.05)) = 0.012
        _WearBlur         ("Wear Noise Blur (mip)", Range(0,8)) = 4

        [Header(Living   Emissive Pulse)]
        _EmissionMap    ("Emission Map", 2D)             = "black" {}
        [HDR] _EmissionColor ("Emission Color", Color)   = (0, 0, 0, 1)
        _PulseSpeed     ("Pulse Speed", Range(0,8))      = 1.5
        _PulseMin       ("Pulse Min", Range(0,1))        = 0.35

        [Header(Dungeon   Rim and Outline)]
        _Ambient        ("Ambient Fill", Range(0,1))     = 0.15
        _HemiStrength   ("Hemispheric Ambient", Range(0,1)) = 0.35
        _RimColor       ("Rim Color", Color)             = (1.0, 0.78, 0.35, 1)
        _RimPower       ("Rim Tightness", Range(0.5,10)) = 4.0
        _RimStrength    ("Rim Strength", Range(0,6))     = 0.6
        _OutlineColor   ("Outline Color", Color)         = (1.0, 0.80, 0.30, 1)
        _OutlineWidth   ("Outline Width (world units)", Range(0,0.6)) = 0.0

        // legacy simple grime (kept for back-compat; WEAR supersedes it)
        _GrimeStrength  ("Legacy Grime Strength", Range(0,1)) = 0.0
        _GrimeScroll    ("Legacy Grime Scroll", Vector)   = (0,0,0,0)
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }

        // ── Pass 1: FORWARD LIT — PBR + procedural wear + rim + emissive ─────
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma shader_feature_local _TRIPLANAR
            #pragma shader_feature_local _WEAR
            #pragma shader_feature_local _BLOCKS
            #pragma shader_feature_local _PARALLAX

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            // Required in URP 17: the renderer defaults to Forward+ (clustered). Without
            // this keyword GetAdditionalLightsCount() falls back to unity_LightData.y,
            // which clustered rendering never populates — the loop then walks unset
            // light indices and returns garbage/NaN, which is what turned these surfaces
            // black and see-through.
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "LivingSurfaceInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float3 tangentWS   : TEXCOORD2;
                float3 bitangentWS : TEXCOORD3;
                float3 positionWS  : TEXCOORD4;
                float  fogFactor   : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                VertexPositionInputs posn = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   norm = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);
                OUT.positionHCS = posn.positionCS;
                OUT.positionWS  = posn.positionWS;
                OUT.normalWS    = norm.normalWS;
                OUT.tangentWS   = norm.tangentWS;
                OUT.bitangentWS = norm.bitangentWS;
                OUT.uv          = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.fogFactor   = ComputeFogFactor(posn.positionCS.z);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                float3 nWS   = normalize(IN.normalWS);
                float3 posWS = IN.positionWS;

                half3  albedo;
                float3 N;

            #if defined(_TRIPLANAR)
                float3 blend = TriBlend(nWS);
                float2 uvX = posWS.zy * _TriplanarScale;
                float2 uvY = posWS.xz * _TriplanarScale;
                float2 uvZ = posWS.xy * _TriplanarScale;
                albedo = (SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvX).rgb * blend.x
                        + SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvY).rgb * blend.y
                        + SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvZ).rgb * blend.z) * _BaseColor.rgb;

                half3 tnX = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvX), _NormalScale);
                half3 tnY = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvY), _NormalScale);
                half3 tnZ = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvZ), _NormalScale);
                float3 aN = abs(nWS);
                tnX = half3(tnX.xy + nWS.zy, aN.x * tnX.z);
                tnY = half3(tnY.xy + nWS.xz, aN.y * tnY.z);
                tnZ = half3(tnZ.xy + nWS.xy, aN.z * tnZ.z);
                N = normalize(tnX.zyx * blend.x + tnY.xzy * blend.y + tnZ.xyz * blend.z);
            #else
                albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).rgb * _BaseColor.rgb;
                half3 nTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, IN.uv), _NormalScale);
                float3x3 TBN = float3x3(IN.tangentWS, IN.bitangentWS, nWS);
                N = normalize(mul(nTS, TBN));
            #endif

                half4 mask = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, IN.uv);
                half metallic   = mask.r * _Metallic;
                half occlusion  = lerp(1.0, mask.g, _OcclusionStr);
                half smoothness = mask.a * _Smoothness;

                // ── lighting inputs ──
                float4 shadowCoord = TransformWorldToShadowCoord(posWS);
                Light  mainLight   = GetMainLight(shadowCoord);
                half3  V           = normalize(GetWorldSpaceViewDir(posWS));
                float2 screenUV    = GetNormalizedScreenSpaceUV(IN.positionHCS);
                // NB: ambient is deliberately evaluated further down, AFTER the block and
                // wear layers have perturbed N — it's normal-dependent now (see below).

                half aoRaw = 1.0;
            #if defined(_SCREEN_SPACE_OCCLUSION)
                AmbientOcclusionFactor aoFactor = GetScreenSpaceAmbientOcclusion(screenUV);
                aoRaw = aoFactor.indirectAmbientOcclusion;
                occlusion = min(occlusion, aoRaw);
            #endif

            #if defined(_WEAR) || defined(_BLOCKS)
                // Shared surface frame for every procedural layer below. All of it keys
                // off world position, so nothing swims as the camera or object moves and
                // it lines up seam-free across adjacent wall meshes.
                float  wallness = 1.0 - saturate(nWS.y);         // 1 on walls, 0 on floors
                float2 wuv      = WearUV(posWS, nWS);

                // Large-scale layers all read a blurred mip so they form connected shapes
                // instead of per-texel confetti; the fine grime below keeps mip 0.
                float mB = _WearBlur;

                float3 wT, wB;
                WearFrame(nWS, wT, wB);

                // Every relief layer accumulates into ONE tangent-space slope, applied
                // through a TBN matrix at the end. Adding world-space offsets straight
                // into N (the previous approach) is unbounded — it could shove the normal
                // further than unit length and randomise it. Building a tangent normal
                // instead keeps z positive and normalises, so it cannot blow up, and the
                // block and crack layers compose coherently instead of fighting.
                float2 surfSlope = 0.0;
            #endif

            #if defined(_BLOCKS)
                // ---- stylised stone blockwork ----
                // Warp the SAMPLE POSITION rather than the edge distance. Perturbing the
                // distance field (the previous approach) eats into the joint and breaks it
                // apart in places, so courses stopped reading as continuous lines. Warping
                // the domain bends the whole grid coherently: joints stay unbroken and
                // fully connected, they just wander like hand-laid stone.
                float2 buv  = wuv * _BlockScale;
                buv += (NoiseLOD(wuv * _BlockEdgeScale, mB) - 0.5) * _BlockIrregular * 2.0;

                // Procedural patterns get no mip filtering. Once a stone cell shrinks
                // toward pixel size the joints and contact shadows alias into noise and
                // drag the whole wall darker and mushier with distance. Fade the pattern
                // out as that happens so distant walls settle to clean average stone.
                // fwidth is in CELL units, so it's directly "cells covered per pixel".
                // Nyquist says aliasing only starts once that passes ~0.5 (a cell under two
                // pixels), so hold full detail until 0.35 and fade out by 1.0 — starting
                // earlier just blurs the stonework at normal viewing distance.
                float2 bfw   = fwidth(buv) * float2(max(_BlockAspect, 1e-3), 1.0);
                float  bFade = 1.0 - saturate((max(bfw.x, bfw.y) - 0.35) / 0.65);

            #if defined(_PARALLAX)
                // Parallax occlusion mapping. March the view ray through the stone height
                // field and shade whatever it hits FIRST. This is the step that gives real
                // depth: near stones genuinely occlude the ones behind them and the joints
                // become recesses you can look into, instead of a flat plane with clever
                // shading painted on. Everything downstream then samples the hit point.
                float3 vTS = float3(dot(V, wT), dot(V, wB), dot(V, nWS));
                float  vz  = max(abs(vTS.z), 0.35);          // guard grazing angles
                float  pd  = _ParallaxDepth * bFade;         // no parallax once detail fades

                // More steps at glancing angles, where the ray crosses more cells.
                float  nLayers = lerp(_ParallaxSteps, 8.0, saturate(vz));
                float  layerD  = 1.0 / nLayers;
                float2 dUV     = (vTS.xy / vz) * pd / nLayers;

                float2 pUV   = buv;
                float  curD  = 0.0;
                float  sampD = 1.0 - BlockHeightAt(pUV);

                [loop]
                for (int pi = 0; pi < (int)nLayers; ++pi)
                {
                    if (curD >= sampD) break;
                    pUV  -= dUV;
                    sampD = 1.0 - BlockHeightAt(pUV);
                    curD += layerD;
                }

                // Linear refine between the last two samples, so the silhouette of each
                // stone edge stays smooth instead of stair-stepping at low step counts.
                float2 prevUV = pUV + dUV;
                float  aft    = sampD - curD;
                float  bef    = (1.0 - BlockHeightAt(prevUV)) - (curD - layerD);
                buv = lerp(pUV, prevUV, saturate(aft / max(aft - bef, 1e-4)));
            #endif

                float2 bcell, bid; float brnd;
                BlockCell(buv, bcell, bid, brnd);

                // distance to the nearest joint, in cell units
                float2 bd    = min(bcell, 1.0 - bcell);
                float  bedge = min(bd.x, bd.y);

                bedge = max(bedge, 0.0);

                // Slight per-stone variation in joint width, so courses aren't mechanical.
                float mw     = max(_MortarWidth * (0.7 + 0.6 * brnd), 1e-4);
                float mortar = (1.0 - smoothstep(0.0, mw, bedge)) * bFade;

                // Each stone gets its own tone — this is what makes it read as masonry
                // rather than a texture with lines scored into it.
                albedo *= 1.0 + (brnd - 0.5) * 2.0 * _BlockToneVar * bFade;

                // Contact shadow in the joint. This is the single biggest cue that the
                // stones are separate solid volumes and not a pattern on a flat plane.
                half jointAO = lerp(_BlockAO, 1.0, smoothstep(0.0, max(_BlockAOWidth, 1e-4), bedge));
                albedo *= lerp(1.0, jointAO, bFade);

                // Stones set deeper into the wall receive less bounced light. This keeps
                // the varying levels legible head-on, where parallax shows least shift,
                // and at distance once the parallax march has faded out entirely.
                half faceH = lerp(1.0 - _BlockHeightVar, 1.0, brnd);
                albedo *= lerp(1.0, faceH, bFade * 0.7);

                albedo      = lerp(albedo, _MortarColor.rgb, saturate(mortar));
                smoothness *= lerp(1.0, 0.5, saturate(mortar));      // recessed mortar is matte

                // Chamfer, not a dome. The slope lives ONLY in the narrow band between the
                // mortar and the face; across the flat face the stone keeps the wall's own
                // normal, and inside the joint the mortar stays flat too. That planar face
                // with a crisp arris is what reads as masonry rather than as a pillow.
                float bevw      = max(_BlockBevel, 1e-4);
                float bevelBand = (1.0 - smoothstep(mw, mw + bevw, bedge))   // ends at face
                                * smoothstep(0.0, mw, bedge);                // starts at mortar
                float2 bg = (bd.x < bd.y) ? float2(bcell.x < 0.5 ? -1.0 : 1.0, 0.0)
                                          : float2(0.0, bcell.y < 0.5 ? -1.0 : 1.0);
                surfSlope += bg * bevelBand * _BlockBevelStrength * bFade;
            #endif

            #if defined(_WEAR)
                // ---- static, world-space procedural wear, layered over the stonework ----

                // ---- macro tone: stops a 40m wall reading as one flat value ----
                float macro = (NoiseLOD(wuv * _MacroScale, mB + 2.0) - 0.5) * 2.0;   // -1..1

                // ---- lighter patches: poured repairs / spalled-off render ----
                // pe is clamped away from 0: smoothstep divides by (max - min), so a zero
                // edge is a divide-by-zero -> NaN -> the whole surface stops rasterising.
                // A material that predates this property zero-fills it under the SRP
                // Batcher, so the clamp is load-bearing, not just defensive.
                float pe    = max(_PatchEdge, 1e-3);
                float pat   = NoiseLOD(wuv * _PatchScale + 3.17, mB + 1.0);
                float patch = smoothstep(0.5 + pe, 0.5 - pe, pat) * _PatchStrength;

                // ---- crack network, two octaves so it branches at both scales ----
                float2 cuv    = wuv * _CrackScale;
                float  crackC = CrackLine(cuv, _CrackWidth, mB);
                float  crack  = max(crackC,
                                    CrackLine(cuv * 2.37 + 7.3, _CrackWidth * 1.5, mB) * 0.65);
                crack *= _CrackStrength;

                // multi-octave blotches (planar-projected now, so they stop banding)
                float2 sp = wuv * _StainScale;
                float blot = NoiseLOD(sp, mB) * 0.6 + NoiseLOD(sp * 2.7 + 0.37, mB - 1.0) * 0.4;

                // Grime pooling toward the base of walls. The smoothstep keeps this a
                // gradient — the raw saturate() pinned to 1.0 for *everything* below
                // (_WearBaseY - _WearBaseRange), i.e. the whole lower half of the bunker.
                float baseGrime = saturate((_WearBaseY - posWS.y) / max(_WearBaseRange, 0.01));
                baseGrime = smoothstep(0.0, 1.0, baseGrime) * _BaseGrimeStrength;
                baseGrime *= lerp(0.4, 1.0, wallness);

                // damp blotchy stains (contrast-remapped)
                float stain = smoothstep(0.55, 0.85, blot) * _StainStrength;

                // gravity water streaks on walls: SPARSE columns, broken into vertical runs.
                float colSeed = NoiseR(float2(posWS.x, posWS.z) * _StreakTiling);
                float streakMask = smoothstep(0.78, 0.97, colSeed);          // few columns streak
                float vy = posWS.y / max(_StreakLength, 0.01);
                float dripLo = NoiseR(float2(colSeed * 1.7, vy * 0.35));      // envelope (where it runs)
                float dripHi = NoiseR(float2(colSeed * 5.3, vy));            // fine vertical breakup
                float streak = streakMask * dripLo * smoothstep(0.35, 0.8, dripHi)
                               * wallness * _StreakStrength;

                // A slice of the streaking columns bleeds rust off rebar instead of water.
                float rust = streak * smoothstep(0.55, 0.85, NoiseR(float2(colSeed * 9.1, 0.5)))
                             * _RustStrength;

                // Salt bloom: pale and chalky, blooms where damp meets the base of a wall.
                float efflo = saturate(baseGrime * 1.3 + streak)
                              * smoothstep(0.45, 0.75, NoiseLOD(sp * 1.8 + 5.1, mB))
                              * wallness * _EffloStrength;

                // dirt settles in cavities / corners (from SSAO)
                float cavity = (1.0 - aoRaw) * 0.6;

                // Composite the darkening layers as coverage — each one dirties whatever
                // is still clean. Summing them (the old behaviour) saturated to 1.0 nearly
                // everywhere and flattened the surface to a flat _GrimeColor multiply.
                float dirt = 1.0 - (1.0 - saturate(baseGrime))
                                 * (1.0 - saturate(stain))
                                 * (1.0 - saturate(streak))
                                 * (1.0 - saturate(cavity));
                dirt = saturate(dirt * _WearAmount) * _GrimeMaxDarken;
                float wet = saturate((stain + streak * 0.5) * _WearAmount) * _GrimeMaxDarken;

                // Paint order matters: base tone, then substrate, then what settles on top.
                //
                // These MULTIPLY the surface rather than lerping it to a flat colour. A
                // flat-colour lerp erases whatever is underneath it, and with the block
                // layer running first that meant the wear silently wiped out the entire
                // masonry pattern and left only its own noise. Only cracks and mortar —
                // which genuinely replace the surface — are allowed to lerp.
                albedo *= 1.0 + macro * _MacroStrength;
                albedo *= lerp(1.0.xxx, _PatchColor.rgb * 1.6, saturate(patch * _WearAmount));
                albedo  = lerp(albedo, albedo * _GrimeColor.rgb, dirt);
                albedo *= lerp(1.0.xxx, _StainColor.rgb * 2.2, saturate(wet * 0.5));
                albedo *= lerp(1.0.xxx, _RustColor.rgb  * 2.2, saturate(rust));
                albedo *= lerp(1.0.xxx, _EffloColor.rgb * 1.5, saturate(efflo));
                albedo  = lerp(albedo, _CrackColor.rgb, saturate(crack * _WearAmount));

                smoothness  = lerp(smoothness, _WetSmoothness, wet);   // wet patches gleam
                smoothness *= lerp(1.0, 0.35, saturate(crack));        // cracks read matte
                smoothness *= lerp(1.0, 0.50, saturate(efflo));        // salt bloom is chalky

                // ---- relief: perturb the normal so damage actually catches the lights ----
                // A crack is a groove, so height = -crack and the perturbed normal is
                // N - grad(height). Two extra taps, sampled only on the crack field.
                float e  = _WearBumpEps;
                float cX = CrackLine(cuv + float2(e, 0), _CrackWidth, mB);
                float cY = CrackLine(cuv + float2(0, e), _CrackWidth, mB);

                // CrackLine returns 0..1, so this gradient is already O(1) across a crack
                // edge — it needs no amplification. Scaled by _CrackStrength so turning
                // cracks down turns their relief down too; it used to ignore that and stay
                // at full strength no matter how far the crack slider came down.
                float2 cgrad = float2(crackC - cX, crackC - cY);
                surfSlope -= cgrad * _WearBumpStrength * _CrackStrength;
            #endif

            #if defined(_WEAR) || defined(_BLOCKS)
                // Resolve every relief layer in one go. Tangent-space normal from the
                // accumulated slope, then into world space via TBN (rows T, B, N) exactly
                // as the normal-map path does. z = 1 keeps the normal in the surface's
                // upper hemisphere no matter how large the slope gets, so heavy settings
                // flatten out gracefully instead of scrambling into noise.
                float3 wearTS = normalize(float3(surfSlope, 1.0));
                N = normalize(mul(wearTS, float3x3(wT, wB, N)));
            #endif

                // ── shading ──
                // Hemispheric ambient. The scene uses Flat ambient, so SampleSH(N) returns
                // the SAME value for every normal — which means the block doming and crack
                // relief are completely invisible on ambient-lit surfaces, and this bunker
                // is mostly ambient-lit. Weighting ambient by the normal's vertical
                // component restores that shaping with no extra lights: each stone catches
                // more light on its upper curve and self-shades underneath.
                half hemi    = saturate(N.y * 0.5 + 0.5);
                half3 ambient = (SampleSH(N) + _Ambient)
                                * lerp(1.0 - _HemiStrength, 1.0 + _HemiStrength, hemi);

                half ndotl = saturate(dot(N, mainLight.direction));
                half3 lit  = albedo * (mainLight.color * (ndotl * mainLight.shadowAttenuation)
                                       + ambient * occlusion);

                half3 H = normalize(mainLight.direction + V);
                half specTerm = pow(saturate(dot(N, H)), lerp(8.0, 128.0, smoothness)) * smoothness;
                lit += mainLight.color * specTerm * (0.04 + metallic * albedo);

            #if defined(_ADDITIONAL_LIGHTS)
                // LIGHT_LOOP_BEGIN reads positionWS + normalizedScreenSpaceUV off a local
                // named `inputData` to seed the cluster iterator, so it has to exist even
                // though we only use two of its fields.
                InputData inputData = (InputData)0;
                inputData.positionWS = posWS;
                inputData.normalizedScreenSpaceUV = screenUV;

                uint pixelLightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light l = GetAdditionalLight(lightIndex, posWS);
                    half a = saturate(dot(N, l.direction)) * l.distanceAttenuation * l.shadowAttenuation;
                    lit += albedo * l.color * a;
                LIGHT_LOOP_END
            #endif

                // Fresnel sheen, gated by the light the surface actually receives. Raw
                // fresnel covers a large flat wall almost entirely — an FPS camera sees
                // walls at glancing angles, so dot(N,V) is ~0 across the whole surface,
                // not just at its edges. Ungated it flooded unlit walls with a flat
                // _RimColor, which read as see-through sky rather than as a highlight.
                half rim = pow(1.0 - saturate(dot(N, V)), _RimPower) * _RimStrength;
                lit += _RimColor.rgb * rim * saturate(Luminance(lit));

                half pulse = lerp(_PulseMin, 1.0, 0.5 + 0.5 * sin(_Time.y * _PulseSpeed));
                half3 emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, IN.uv).rgb
                                 * _EmissionColor.rgb * pulse;
                lit += emission;

                lit = MixFog(lit, IN.fogFactor);
                return half4(lit, 1.0);
            }
            ENDHLSL
        }

        // ── Pass 2: SHADOW CASTER ────────────────────────────────────────────
        // Without this the walls cast no shadows at all; previously the shader leaned on
        // FallBack "URP/Lit" to supply it, which pulled in a different UnityPerMaterial
        // layout and broke SRP Batcher compatibility.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "LivingSurfaceInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct AttributesS
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct VaryingsS
            {
                float4 positionHCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            VaryingsS shadowVert (AttributesS IN)
            {
                VaryingsS OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS   = TransformObjectToWorldNormal(IN.normalOS);

            #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 lightDirWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirWS = _LightDirection;
            #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirWS));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
            #endif
                OUT.positionHCS = positionCS;
                return OUT;
            }

            half4 shadowFrag (VaryingsS IN) : SV_Target { return 0; }
            ENDHLSL
        }

        // ── Pass 3: DEPTH ONLY ───────────────────────────────────────────────
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex depthVert
            #pragma fragment depthFrag
            #pragma multi_compile_instancing

            #include "LivingSurfaceInput.hlsl"

            struct AttributesD
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct VaryingsD
            {
                float4 positionHCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            VaryingsD depthVert (AttributesD IN)
            {
                VaryingsD OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 depthFrag (VaryingsD IN) : SV_Target { return 0; }
            ENDHLSL
        }

        // ── Pass 4: DEPTH NORMALS ────────────────────────────────────────────
        // Feeds _CameraNormalsTexture, which SSAO (and any normals-based post) reads.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex depthNormalsVert
            #pragma fragment depthNormalsFrag
            #pragma multi_compile_instancing

            #include "LivingSurfaceInput.hlsl"

            struct AttributesDN
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct VaryingsDN
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            VaryingsDN depthNormalsVert (AttributesDN IN)
            {
                VaryingsDN OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 depthNormalsFrag (VaryingsDN IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                return half4(NormalizeNormalPerPixel(IN.normalWS), 0.0);
            }
            ENDHLSL
        }

        // ── Pass 5: OUTLINE (inverted hull) ──────────────────────────────────
        Pass
        {
            Name "Outline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vertOutline
            #pragma fragment fragOutline
            #pragma multi_compile_instancing

            #include "LivingSurfaceInput.hlsl"

            struct AttributesO
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct VaryingsO
            {
                float4 positionHCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            VaryingsO vertOutline (AttributesO IN)
            {
                VaryingsO OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                // Width 0 means "no outline" — collapse the hull off-screen instead of
                // drawing backfaces coincident with the lit surface (which z-fights and
                // costs a full extra draw over every wall in the scene).
                if (_OutlineWidth <= 1e-5)
                {
                    OUT.positionHCS = float4(2, 2, 2, 1);
                    return OUT;
                }

                float3 posWS    = TransformObjectToWorld(IN.positionOS.xyz);
                float3 centerWS = TransformObjectToWorld(float3(0,0,0));
                float3 dir      = posWS - centerWS;
                float  len      = length(dir);
                if (len > 1e-4) posWS += (dir / len) * _OutlineWidth;
                OUT.positionHCS = TransformWorldToHClip(posWS);
                return OUT;
            }

            half4 fragOutline (VaryingsO IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                return _OutlineColor;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
