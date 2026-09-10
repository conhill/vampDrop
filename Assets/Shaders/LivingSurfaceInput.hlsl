#ifndef VAMPIRE_LIVING_SURFACE_INPUT_INCLUDED
#define VAMPIRE_LIVING_SURFACE_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// Every non-texture property in the Properties block must appear in this CBUFFER, and
// every pass must see the exact same layout, or the SRP Batcher silently rejects the
// shader. Keeping it in one include is what guarantees the passes can't drift apart.
CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    float4 _BumpMap_ST;
    float4 _MaskMap_ST;
    float4 _GrimeMap_ST;
    float4 _EmissionMap_ST;
    float4 _BaseColor;
    float4 _GrimeColor;
    float4 _GrimeScroll;
    float4 _EmissionColor;
    float4 _RimColor;
    float4 _OutlineColor;
    float4 _StainColor;
    float4 _PatchColor;
    float4 _CrackColor;
    float4 _EffloColor;
    float4 _RustColor;
    float4 _MortarColor;
    float  _NormalScale;
    float  _Metallic;
    float  _Smoothness;
    float  _OcclusionStr;
    float  _Triplanar;
    float  _TriplanarScale;
    float  _TriplanarSharp;
    float  _WearOn;
    float  _WearAmount;
    float  _GrimeStrength;
    float  _GrimeMaxDarken;
    float  _BaseGrimeStrength;
    float  _StainScale;
    float  _StainStrength;
    float  _StreakStrength;
    float  _StreakTiling;
    float  _StreakLength;
    float  _WearBaseY;
    float  _WearBaseRange;
    float  _WetSmoothness;
    float  _MacroScale;
    float  _MacroStrength;
    float  _PatchScale;
    float  _PatchStrength;
    float  _PatchEdge;
    float  _CrackScale;
    float  _CrackWidth;
    float  _CrackStrength;
    float  _EffloStrength;
    float  _RustStrength;
    float  _WearBumpStrength;
    float  _WearBumpEps;
    float  _WearBlur;
    float  _BlocksOn;
    float  _BlockScale;
    float  _BlockAspect;
    float  _BlockOffset;
    float  _MortarWidth;
    float  _BlockIrregular;
    float  _BlockEdgeScale;
    float  _BlockToneVar;
    float  _BlockBevelStrength;
    float  _BlockBevel;
    float  _BlockAO;
    float  _BlockAOWidth;
    float  _BlockRowRand;
    float  _BlockWidthRand;
    float  _BlockHeightVar;
    float  _ParallaxOn;
    float  _ParallaxDepth;
    float  _ParallaxSteps;
    float  _PulseSpeed;
    float  _PulseMin;
    float  _Ambient;
    float  _HemiStrength;
    float  _RimPower;
    float  _RimStrength;
    float  _OutlineWidth;
CBUFFER_END

TEXTURE2D(_BaseMap);     SAMPLER(sampler_BaseMap);
TEXTURE2D(_BumpMap);     SAMPLER(sampler_BumpMap);
TEXTURE2D(_MaskMap);     SAMPLER(sampler_MaskMap);
TEXTURE2D(_GrimeMap);    SAMPLER(sampler_GrimeMap);
TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);

float NoiseR(float2 p)
{
    return SAMPLE_TEXTURE2D(_GrimeMap, sampler_GrimeMap, p).r;
}

// Blurred noise, straight off the mip chain. _GrimeMap is a *detailed* concrete
// displacement map, not a smooth fractal noise — sampling mip 0 for the large-scale
// layers turns cracks and patches into confetti. Forcing a high mip gives the smooth,
// low-frequency field those layers actually need, for the same one fetch.
float NoiseLOD(float2 p, float mip)
{
    return SAMPLE_TEXTURE2D_LOD(_GrimeMap, sampler_GrimeMap, p, mip).r;
}

// Triplanar weights: bias toward the dominant axis, then normalise so the three
// projections sum to 1 and the surface keeps its full albedo energy.
float3 TriBlend(float3 nWS)
{
    float3 b = pow(abs(nWS), _TriplanarSharp);
    return b / max(b.x + b.y + b.z, 1e-4);
}

// World-space planar projection onto the face the surface actually points at.
// The wear layers used to sample posWS.xz, which is CONSTANT along a vertical wall's
// height — every blotch smeared into a vertical band. Picking the dominant axis keeps
// noise isotropic on walls, floors and ceilings alike.
float2 WearUV(float3 posWS, float3 nWS)
{
    float3 a = abs(nWS);
    if (a.x >= a.y && a.x >= a.z) return posWS.zy;
    if (a.y >= a.z)               return posWS.xz;
    return posWS.xy;
}

// The world-space axes WearUV's u and v actually run along. Any relief derived from the
// wear/block UVs has to be pushed along THESE, not along an arbitrary tangent frame, or
// the bump direction is rotated relative to the pattern that produced it.
void WearFrame(float3 nWS, out float3 T, out float3 B)
{
    float3 a = abs(nWS);
    if (a.x >= a.y && a.x >= a.z) { T = float3(0,0,1); B = float3(0,1,0); }  // uv = zy
    else if (a.y >= a.z)          { T = float3(1,0,0); B = float3(0,0,1); }  // uv = xz
    else                          { T = float3(1,0,0); B = float3(0,1,0); }  // uv = xy
}

// Cheap per-cell hashes — no texture fetch. Used to give each stone its own tone, and
// each course its own stone width and horizontal shift.
float Hash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float Hash11(float p)
{
    p = frac(p * 0.1031);
    p *= p + 33.33;
    return frac((p + p) * p);
}

// Single source of truth for the stone grid, so the parallax height field and the shading
// path can never disagree about where a given stone actually sits.
void BlockCell(float2 buv, out float2 cell, out float2 id, out float rnd)
{
    float brow = floor(buv.y);
    float rowR = Hash11(brow);

    // each course gets its own stone width and horizontal shift
    float rowW = lerp(1.0, 0.7 + 0.6 * rowR, _BlockWidthRand);
    float bx   = buv.x * max(_BlockAspect, 1e-3) * rowW
               + _BlockOffset * fmod(abs(brow), 2.0)
               + rowR * _BlockRowRand;

    id   = float2(floor(bx), brow);
    cell = float2(frac(bx), frac(buv.y));
    rnd  = Hash21(id);
}

// Analytic stone height: 0 deep in the joint, up to 1 at a proud stone face. Each stone
// gets its own face height from its hash, so they sit at genuinely different levels.
// Deliberately texture-free — this gets ray-marched, so it has to stay cheap.
float BlockHeightAt(float2 buv)
{
    float2 cell, id; float rnd;
    BlockCell(buv, cell, id, rnd);

    float2 d    = min(cell, 1.0 - cell);
    float  edge = min(d.x, d.y);
    float  mw   = max(_MortarWidth * (0.7 + 0.6 * rnd), 1e-4);
    float  bev  = max(_BlockBevel, 1e-4);
    float  face = lerp(1.0 - _BlockHeightVar, 1.0, rnd);

    // A PLATEAU, not a dome: flat mortar floor, a short ramp up the arris, then a flat
    // stone face. Curving the whole face is what made these read as pillows — a real
    // brick's face is planar and only its edge is chamfered.
    return smoothstep(mw, mw + bev, edge) * face;
}

// A crack network for free: the set of points where a smooth noise field crosses its
// midpoint is a branching contour line. Thresholding |n - 0.5| turns that contour into
// a thin, connected, organic-looking crack from a single grayscale texture fetch.
// The field MUST be smooth, hence NoiseLOD — see the note there.
float CrackLine(float2 uv, float width, float mip)
{
    // Threshold against the field's own LOCAL mean (a blurrier mip), not a hardcoded 0.5.
    // _GrimeMap is a displacement map whose values cluster in a narrow band, so a fixed
    // 0.5 contour can miss the data entirely and yield no cracks at all. Differencing two
    // mips is a cheap high-pass that always straddles zero, whatever the source range.
    float n    = NoiseLOD(uv, mip);
    float mean = NoiseLOD(uv, mip + 2.0);
    // The mip difference spans only a few hundredths, so expand it before thresholding —
    // otherwise any usable `width` swallows most of the surface and you get dense crazing
    // instead of sparse structural cracks.
    float d = (n - mean) * 6.0;
    return 1.0 - smoothstep(0.0, max(width, 1e-4), abs(d));
}

#endif // VAMPIRE_LIVING_SURFACE_INPUT_INCLUDED
