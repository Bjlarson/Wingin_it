// The projected altitude shadow under each plane (PlaneShadow): an ellipse that multiplies what is below it toward
// _Color, solid in the middle with a short soft rim. Drawn on top of the ground with a depth offset (no z-fighting on
// the terrain or the sea) and faded into the fog with distance.
Shader "Winging It/Blob Shadow"
{
    Properties
    {
        _Color ("Shadow Color", Color) = (0.32, 0.36, 0.52, 1)
        _Strength ("Strength", Range(0, 1)) = 0.6 // PlaneShadow fades it with height
        _Softness ("Rim Softness", Range(0.01, 1)) = 0.35
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent-100" "IgnoreProjector" = "True" }

        Pass
        {
            Name "BlobShadow"
            Tags { "LightMode" = "UniversalForward" }
            Blend DstColor Zero
            ZWrite Off
            ZTest LEqual
            Cull Off
            Offset -2, -2

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Strength;
                half _Softness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half fog : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv * 2.0 - 1.0;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half r = length(i.uv);
                half body = 1.0 - smoothstep(1.0 - _Softness, 1.0, r);
                half amount = body * _Strength;
                half3 shade = lerp(half3(1, 1, 1), _Color.rgb, amount);
                shade = MixFogColor(shade, half3(1, 1, 1), i.fog); // multiply by white in the fog: no shadow there
                return half4(shade, 1);
            }
            ENDHLSL
        }
    }
}
