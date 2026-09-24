Shader "KinoRotunda/Scrolling Cloud Layer"
{
    Properties
    {
        [MainTexture] _BaseMap("Cloud texture (RGBA)", 2D) = "white" {}
        [MainColor] _BaseColor("Cloud tint", Color) = (1, 1, 1, 1)
        _Opacity("Opacity", Range(0, 1)) = 0.35
        _ScrollSpeed("Scroll speed (UV per second, XY)", Vector) = (0.003, 0.0007, 0, 0)
        _EdgeFade("Quad edge softness", Range(0.001, 0.25)) = 0.06
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-100" "RenderType"="Transparent" }
        Pass
        {
            Name "CloudUnlit"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float4 _ScrollSpeed;
                half _Opacity;
                float _EdgeFade;
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
                float2 cloudUV : TEXCOORD0;
                float2 quadUV : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.quadUV = input.uv;
                // Repeat sampling keeps motion continuous without moving the overhead quad.
                output.cloudUV = TRANSFORM_TEX(input.uv, _BaseMap) - frac(_Time.y * _ScrollSpeed.xy);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 cloud = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.cloudUV);
                float2 edgeDistance = min(input.quadUV, 1 - input.quadUV);
                half edgeFade = smoothstep(0, max(_EdgeFade, 0.001), min(edgeDistance.x, edgeDistance.y));
                return half4(cloud.rgb * _BaseColor.rgb, cloud.a * _BaseColor.a * _Opacity * edgeFade);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
