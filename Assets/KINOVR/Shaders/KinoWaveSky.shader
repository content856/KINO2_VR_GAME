Shader "KINO/Wave Sky"
{
    // Deep navy gradient drawn on the black enclosure's shell, brightest at the
    // horizon in front of the player and darkest overhead and underfoot.
    Properties
    {
        _HorizonColor ("Horizon", Color) = (.04, .15, .44, 1)
        _ZenithColor ("Zenith and floor", Color) = (.01, .035, .12, 1)
        _FrontLift ("Front lift", Range(0, 1)) = .45
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Front
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha, Zero One
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _HorizonColor, _ZenithColor;
                half _FrontLift;
                float _WaveTime, _Visibility, _Reveal, _Dim, _Collapse, _Flash, _SkyOpacity;
                float4 _Origin;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 dir = normalize(input.positionWS - _Origin.xyz);
                float2 front = float2(sin(_Origin.w), cos(_Origin.w));
                float facing = saturate(dot(normalize(dir.xz + 1e-4), front)) * (1.0 - abs(dir.y));
                half horizon = saturate(1.0 - abs(dir.y + 0.08) * 1.6);
                horizon = horizon * horizon * (0.55 + _FrontLift * facing);
                half3 rgb = lerp(_ZenithColor.rgb, _HorizonColor.rgb, saturate(horizon));
                rgb *= (1.0 - 0.35 * _Dim);
                rgb += _Flash * 0.08 * half3(0.35, 0.6, 1.0) * horizon;
                // _SkyOpacity fades the navy away to reveal the room while the beam is still visible.
                return half4(rgb, saturate(_Visibility * _SkyOpacity));
            }
            ENDHLSL
        }
    }
}
