Shader "KinoRotunda/Dust Mote"
{
    Properties
    {
        [MainColor] _BaseColor("Warm dust tint", Color) = (1, 0.88, 0.65, 0.7)
        _NearFade("Camera fade start / end (metres)", Vector) = (0.9, 2.2, 0, 0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Name "DustUnlit"
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
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _NearFade;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.uv;
                output.color = input.color * _BaseColor;
                float cameraFade = smoothstep(_NearFade.x, _NearFade.y, distance(GetCameraPositionWS(), positionWS));
                // The source volume is in the rotunda's world coordinates. Fade the
                // occasional drifting mote before it reaches the floor or arcade edge.
                float roomFade = 1 - smoothstep(11.8, 12.4, length(positionWS.xz));
                roomFade *= smoothstep(0.8, 1.3, positionWS.y) * (1 - smoothstep(5.8, 6.3, positionWS.y));
                output.color.a *= cameraFade * roomFade;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 p = input.uv * 2 - 1;
                half softness = saturate(1 - dot(p, p));
                // A soft grain computed from UVs: no texture, depth copy, or glow pass.
                return half4(input.color.rgb, input.color.a * softness * softness);
            }
            ENDHLSL
        }
    }
}
