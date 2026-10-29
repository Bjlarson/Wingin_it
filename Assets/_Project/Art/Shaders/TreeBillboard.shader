// The far stand-in for a terrain tree (TreeBillboards): one quad with a picture of the tree, turned about its own
// upright to face the camera so it always shows its full width. The picture is the tree's unlit palette colours (the
// kit bakes its face tones into them); the sun and sky light it here as they light the mesh, with a normal half up and
// half toward the camera. Cut out on alpha, fogged, no shadows (it only draws past the shadow distance).
// The quad's mesh is in metres about the tree's base; the object matrix gives position and the terrain's per-tree
// scale, never its turn.
Shader "Winging It/Tree Billboard"
{
    Properties
    {
        [MainTexture] _BaseMap ("Picture", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        _Brightness ("Brightness", Range(0.5, 2)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "DisableBatching" = "True" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half _Cutoff;
            half _Brightness;
        CBUFFER_END

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        struct Attributes
        {
            float4 positionOS : POSITION;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        // The quad's corner in world space, and the flat direction from the tree to the camera.
        float3 Billboard(float3 positionOS, out float3 toCamera)
        {
            float3 root = TransformObjectToWorld(float3(0, 0, 0));
            float4x4 m = GetObjectToWorldMatrix();
            float width = length(float3(m[0].x, m[1].x, m[2].x));
            float height = length(float3(m[0].y, m[1].y, m[2].y));
            float3 flat = GetCameraPositionWS() - root;
            flat.y = 0;
            toCamera = dot(flat, flat) > 1e-4 ? normalize(flat) : float3(0, 0, -1);
            float3 right = float3(-toCamera.z, 0, toCamera.x);
            return root + right * positionOS.x * width + float3(0, positionOS.y * height, 0);
        }

        half Coverage(float2 uv)
        {
            half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a;
            clip(alpha - _Cutoff);
            return alpha;
        }
        ENDHLSL

        Pass
        {
            Name "TreeBillboard"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half3 light : TEXCOORD1;
                half fog : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 toCamera;
                float3 positionWS = Billboard(input.positionOS.xyz, toCamera);
                o.positionCS = TransformWorldToHClip(positionWS);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                half3 normal = normalize(half3(0, 1, 0) + toCamera);
                Light sun = GetMainLight();
                o.light = (SampleSH(normal) + sun.color * saturate(dot(normal, sun.direction))) * _Brightness;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half4 picture = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                clip(picture.a - _Cutoff);
                return half4(MixFog(picture.rgb * i.light, i.fog), 1);
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
            #pragma multi_compile_instancing

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 toCamera;
                o.positionCS = TransformWorldToHClip(Billboard(input.positionOS.xyz, toCamera));
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return o;
            }

            half Frag(Varyings i) : SV_Target
            {
                Coverage(i.uv);
                return 0;
            }
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
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 toCamera;
                o.positionCS = TransformWorldToHClip(Billboard(input.positionOS.xyz, toCamera));
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                o.normal = normalize(float3(0, 1, 0) + toCamera);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                Coverage(i.uv);
                float3 n = normalize(i.normal);
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
