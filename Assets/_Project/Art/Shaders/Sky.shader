// The maps' skybox: a plain gradient from the fog colour at the horizon up to the zenith, with a soft sun. Below the
// horizon it is the fog colour too, so fogged ground and sea far out melt into the sky with no seam: the edge of the
// world reads as haze. Cheap: one pow and a dot per pixel, no scattering.
Shader "Winging It/Sky"
{
    Properties
    {
        _ZenithColor ("Zenith", Color) = (0.4, 0.58, 0.86, 1)
        _HorizonColor ("Horizon (= fog colour)", Color) = (0.72, 0.82, 0.92, 1)
        _HazeHeight ("Haze Height", Range(0.01, 1)) = 0.35 // how far up (sine of elevation) the horizon colour reaches
        _HazeCurve ("Haze Curve", Range(1, 3)) = 1.5       // over 1: no crease at the horizon
        _SunColor ("Sun", Color) = (1, 0.96, 0.85, 1)
        _SunSize ("Sun Size", Range(0.0001, 0.01)) = 0.0005 // 1 - cos(angular radius)
        _SunGlow ("Sun Glow", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ZenithColor;
                half4 _HorizonColor;
                half _HazeHeight;
                half _HazeCurve;
                half4 _SunColor;
                half _SunSize;
                half _SunGlow;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.direction = input.positionOS.xyz;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 dir = normalize(i.direction);
                half up = saturate(dir.y / _HazeHeight);
                half3 sky = lerp(_HorizonColor.rgb, _ZenithColor.rgb, pow(up, _HazeCurve));

                // The sun: a hard disc and a wide glow, the glow brightest low in the sky where the haze lights up.
                half sunDot = dot(dir, _MainLightPosition.xyz);
                half disc = smoothstep(1.0 - _SunSize * 1.15, 1.0 - _SunSize, sunDot) * step(0.0, dir.y);
                half glow = pow(saturate(sunDot), 24.0) * _SunGlow * (1.0 - 0.5 * up);
                sky = lerp(sky, _SunColor.rgb, saturate(glow));
                sky = lerp(sky, _SunColor.rgb * 1.3, disc);
                return half4(sky, 1);
            }
            ENDHLSL
        }
    }
}
