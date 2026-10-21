// The sea and the canal (living-world.md, section 2): blue water rolling with long swells, chop on top, broken up by
// Perlin noise, frothy white caps on the highest crests and a band of foam along every shore and quay.
// - Waves: summed sines from OceanWaves (C#, pushed as the _OceanWaves / _OceanWaveMotion global arrays), world-space,
//   so the surface mesh (OceanSurface) can follow the camera without the pattern swimming. Each wave fades out where the
//   grid is too coarse for it (uv.x = grid spacing), all of them fade with distance so the horizon is flat and clean,
//   and the swells calm down in shallow water (_ShoreTex R): small in the canal, dying out on the beaches.
// - Noise: Perlin (the same integer-hash Perlin as OceanWaves.Perlin, so C# agrees) scales the waves into calmer and
//   rougher patches and adds bumps of its own, both drifting down the wind (_OceanNoise / _OceanRoughness).
// - Shape: the swells are shaded with a smooth normal worked out per pixel from the waves' slopes (exaggerated by
//   _ShapeStrength so they read from the air), and crests are tinted lighter, troughs darker. No facets, no pixels.
// - Caps: the wave height is worked out again per pixel. Above _CrestFoam the foam texture (_FoamTex: frothy cell edges
//   first, then filling in) is revealed, more of it the higher the crest, so caps grow from the peaks as froth, not
//   blobs. Edges anti-aliased over one pixel.
// - Shore: _ShoreTex (baked by IslandSceneBuilder from the heightmap) holds shallowness in R and the shore foam in G.
// Cosmetic only: the collider stays flat at y = 0 and the waves never pass OceanWaves.MaxHeight.
Shader "Winging It/Water"
{
    Properties
    {
        _Deep ("Water", Color) = (0.118, 0.431, 0.612, 1)
        _Foam ("Foam", Color) = (0.957, 0.984, 1, 1)
        [NoScaleOffset] _ShoreTex ("Shore (R shallow, G foam)", 2D) = "black" {}
        _ShoreRect ("Shore Rect (min x, min z, 1 / size)", Vector) = (-2800, -2800, 0.000178571, 0)
        _FoamTex ("Foam Texture (low values foam first)", 2D) = "white" {}
        _FoamTile ("Foam Tile Size (m)", Float) = 14
        _CrestFoam ("White Caps Start (fraction of max height)", Range(0, 1)) = 0.35
        _CapRange ("White Caps Growth (fraction of max height)", Range(0.05, 1)) = 0.35
        _ShapeStrength ("Swell Shading", Range(0, 6)) = 2.5
        _HeightTint ("Crest / Trough Tint", Range(0, 1)) = 0.3
        _ShallowCalm ("Swells Calmed in Shallows", Range(0, 1)) = 0.85
        _WaveFadeStart ("Wave Fade Start (m)", Float) = 400
        _WaveFadeEnd ("Wave Fade End (m)", Float) = 1500
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _Deep;
            half4 _Foam;
            float4 _ShoreRect;
            float4 _FoamTex_ST;
            float _FoamTile;
            half _CrestFoam;
            half _CapRange;
            half _ShapeStrength;
            half _HeightTint;
            half _ShallowCalm;
            float _WaveFadeStart;
            float _WaveFadeEnd;
        CBUFFER_END

        // OceanWaves.Apply, per wave: (dir x, dir z, wave number, amplitude) and (phase speed rad/s, 1 if a swell, 0, 0).
        #define OCEAN_WAVES 5
        float4 _OceanWaves[OCEAN_WAVES];
        float4 _OceanWaveMotion[OCEAN_WAVES];
        float _OceanMaxHeight;
        float4 _OceanNoise;     // amplitude (m), 1 / scale, drift speed (m/s), 1 / roughness scale
        float4 _OceanRoughness; // wind dir x, wind dir z, roughness minimum, roughness drift speed (m/s)

        TEXTURE2D(_ShoreTex);
        SAMPLER(sampler_ShoreTex);

        struct Attributes
        {
            float4 positionOS : POSITION;
            float2 uv : TEXCOORD0; // x: grid spacing here (m)
        };

        // How much of a wave of wave number k the grid can carry at this spacing: all of it below a third of a
        // wavelength per cell, none at half (where it would alias and crawl as the surface moves).
        float Carry(float k, float spacing)
        {
            float cellsPerWave = 6.2831853 / (k * spacing);
            return saturate((cellsPerWave - 2.0) / 1.0);
        }

        // The shore map at world xz: R shallowness, G shore foam (mip 0, so the vertex stage can read it too).
        float2 Shore(float2 xz)
        {
            return SAMPLE_TEXTURE2D_LOD(_ShoreTex, sampler_ShoreTex, (xz - _ShoreRect.xy) * _ShoreRect.z, 0).rg;
        }

        // OceanWaves.Perlin, line for line: 2D Perlin in -1..1 over an integer hash of the cell corners.
        static const float2 Gradients[8] =
        {
            float2(1, 0), float2(-1, 0), float2(0, 1), float2(0, -1),
            float2(0.70710678, 0.70710678), float2(-0.70710678, 0.70710678),
            float2(0.70710678, -0.70710678), float2(-0.70710678, -0.70710678),
        };

        uint Hash(uint x, uint y)
        {
            uint h = (x * 0x27d4eb2du) ^ (y * 0x165667b1u);
            h ^= h >> 15;
            h *= 0x85ebca6bu;
            h ^= h >> 13;
            return h & 7u;
        }

        float Corner(int2 cell, float2 offset)
        {
            return dot(Gradients[Hash(asuint(cell.x), asuint(cell.y))], offset);
        }

        float Perlin(float2 p)
        {
            float2 f = floor(p);
            int2 i = int2(f);
            float2 d = p - f;
            float2 u = d * d * d * (d * (d * 6.0 - 15.0) + 10.0);
            float bottom = lerp(Corner(i, d), Corner(i + int2(1, 0), d - float2(1, 0)), u.x);
            float top = lerp(Corner(i + int2(0, 1), d - float2(0, 1)), Corner(i + int2(1, 1), d - float2(1, 1)), u.x);
            return lerp(bottom, top, u.y) * 1.41421356;
        }

        // OceanWaves.Roughness: every wave is scaled by this, roughness minimum .. 1.
        float Roughness(float2 xz)
        {
            float2 drift = _OceanRoughness.xy * (_OceanRoughness.w * _Time.y);
            return lerp(_OceanRoughness.z, 1.0, 0.5 + 0.5 * Perlin((xz - drift) * _OceanNoise.w));
        }

        // OceanWaves.HeightNoise, each octave dropped where the grid is too coarse for it, like the waves.
        float HeightNoise(float2 xz, float spacing)
        {
            float2 q = (xz - _OceanRoughness.xy * (_OceanNoise.z * _Time.y)) * _OceanNoise.y;
            float k = 6.2831853 * _OceanNoise.y;
            return _OceanNoise.x * (Perlin(q) * Carry(k, spacing) + 0.5 * Perlin(q * 2.0 + 17.3) * Carry(2.0 * k, spacing)) / 1.5;
        }

        // Height (x) and slope (yz: dh/dx, dh/dz) of the sea at world xz, with the distance fade and the swells calmed
        // by how shallow the water is (0 open sea .. 1 the waterline). The slope leaves out the noise's own bumps.
        float3 Waves(float2 xz, float spacing, float shallow)
        {
            float fade = 1.0 - smoothstep(_WaveFadeStart, _WaveFadeEnd, distance(xz, _WorldSpaceCameraPos.xz));
            float swell = 1.0 - _ShallowCalm * shallow;
            float3 sum = 0;
            [unroll] for (int i = 0; i < OCEAN_WAVES; i++)
            {
                float4 w = _OceanWaves[i];
                float4 m = _OceanWaveMotion[i];
                float a = w.w * Carry(w.z, spacing) * lerp(1.0, swell, m.y);
                float phase = dot(w.xy, xz) * w.z - m.x * _Time.y;
                sum += a * float3(sin(phase), w.z * cos(phase) * w.xy);
            }
            sum *= Roughness(xz);
            sum.x += HeightNoise(xz, spacing) * lerp(1.0, swell, 0.5);
            return sum * fade;
        }

        // World position of a surface vertex, with the waves on it.
        float3 Displace(float4 positionOS, float spacing)
        {
            float3 p = TransformObjectToWorld(positionOS.xyz);
            p.y += Waves(p.xz, spacing, Shore(p.xz).r).x;
            return p;
        }
        ENDHLSL

        Pass
        {
            Name "Water"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float spacing : TEXCOORD1; // grid spacing, so the caps drop the same waves the surface does
                half fog : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionWS = Displace(input.positionOS, input.uv.x);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.spacing = input.uv.x;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            TEXTURE2D(_FoamTex);
            SAMPLER(sampler_FoamTex);

            // 0 below threshold, 1 above, with the edge anti-aliased over one pixel's worth of value.
            half Edge(half value, half threshold)
            {
                half width = max(fwidth(value), 1e-4);
                return saturate((value - threshold) / width + 0.5);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                Light sun = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float2 shore = Shore(i.positionWS.xz);
                float3 wave = Waves(i.positionWS.xz, i.spacing, shore.r);
                half crest = wave.x / max(_OceanMaxHeight, 1e-3); // -1 trough .. 1 the highest crest

                // Lighter on the crests, darker in the troughs.
                half3 color = _Deep.rgb * (1.0 + _HeightTint * crest);

                // White caps: the higher the crest above _CrestFoam, the more of the foam texture shows, froth first.
                half cover = saturate((crest - _CrestFoam) / _CapRange);
                float2 foamUV = (i.positionWS.xz - _OceanRoughness.xy * (_OceanNoise.z * _Time.y)) / _FoamTile;
                half froth = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, foamUV).r;
                half foam = Edge(cover - froth, 0.0) * step(0.001, cover);

                // The shore band, its edge rising and falling with the waves.
                foam = max(foam, Edge(shore.g + 0.12 * crest, 0.4));
                color = lerp(color, _Foam.rgb, foam);

                // The swells' own shape: a smooth normal from their slope, steepened so they read from the air.
                float3 n = normalize(float3(-wave.y * _ShapeStrength, 1.0, -wave.z * _ShapeStrength));
                half3 lit = color * (sun.color * saturate(dot(n, sun.direction)) * sun.shadowAttenuation + SampleSH(half3(0, 1, 0)));
                return half4(MixFog(lit, i.fog), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            float4 Vert(Attributes input) : SV_POSITION
            {
                return TransformWorldToHClip(Displace(input.positionOS, input.uv.x));
            }

            half Frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            float4 Vert(Attributes input) : SV_POSITION
            {
                return TransformWorldToHClip(Displace(input.positionOS, input.uv.x));
            }

            half4 Frag() : SV_Target
            {
                float3 n = float3(0, 1, 0); // the waves are too gentle to matter to the ambient occlusion
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 oct = PackNormalOctQuadEncode(n) * 0.5 + 0.5;
                    return half4(PackFloat2To888(oct), 0);
                #else
                    return half4(n, 0);
                #endif
            }
            ENDHLSL
        }
    }
}
