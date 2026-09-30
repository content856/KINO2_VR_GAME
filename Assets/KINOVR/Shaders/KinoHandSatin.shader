Shader "KINO/Hand Satin"
{
    Properties
    {
        _BaseColor ("Midnight blue satin", Color) = (0.11, 0.24, 0.39, 1)
        _SheenColor ("Cool satin highlight", Color) = (0.34, 0.65, 0.85, 1)
        _GoldColor ("Champagne gold reflection", Color) = (0.95, 0.72, 0.36, 1)
        [HDR] _RimColor ("Allwyn cyan edge", Color) = (0.02, 0.72, 0.86, 1)
        _Sheen ("Satin highlight strength", Range(0, 1)) = 0.42
        _GoldAmount ("Gold reflection strength", Range(0, 1)) = 0.32
        _RimStrength ("Cyan edge brightness", Range(0, 2)) = 0.48
        _RimPower ("Cyan edge tightness", Range(1, 8)) = 3.8
        _EnvironmentAmount ("Room lighting influence", Range(0, 1)) = 0.25
        _BreathAmount ("Edge breathing amount", Range(0, 0.2)) = 0.045
        _BreathSpeed ("Edge breathing cycles per second", Range(0, 1)) = 0.16
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor, _SheenColor, _GoldColor, _RimColor;
            half _Sheen, _GoldAmount, _RimStrength, _RimPower;
            half _EnvironmentAmount, _BreathAmount, _BreathSpeed;
        CBUFFER_END
        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            half3 normalWS : TEXCOORD1;
            half4 roomAndFog : TEXCOORD2;
            UNITY_VERTEX_OUTPUT_STEREO
        };
        Varyings Vert(Attributes input)
        {
            Varyings output;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
            output.positionCS = position.positionCS;
            output.positionWS = position.positionWS;
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            output.roomAndFog = half4(SampleSH(output.normalWS), ComputeFogFactor(position.positionCS.z));
            return output;
        }
        ENDHLSL
        Pass
        {
            Name "Satin"
            Tags { "LightMode"="UniversalForwardOnly" }
            ZWrite On
            ZTest LEqual
            Cull Back
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 n = normalize(input.normalWS);
                half3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half facing = saturate(dot(n, v));
                half edge = 1.0h - facing;

                // Broad, world-oriented softboxes echo the hall's ceiling and brass.
                // No cubemap, texture fetches, extra lights or transparent shell.
                half3 key = half3(-0.436h, 0.873h, -0.218h);
                half3 warm = half3(0.808h, 0.505h, 0.303h);
                half sky = saturate(n.y * 0.5h + 0.5h);
                half3 color = _BaseColor.rgb * lerp(0.55h, 1.35h, sky);
                Light mainLight = GetMainLight();
                half3 room = input.roomAndFog.rgb + mainLight.color * saturate(dot(n, mainLight.direction));
                color *= lerp(half3(1, 1, 1), clamp(room, 0.45h, 1.45h), _EnvironmentAmount);

                half satin = pow(saturate(dot(n, normalize(key + v))), 18.0h);
                half gold = pow(saturate(dot(n, normalize(warm + v))), 28.0h);
                color += _SheenColor.rgb * satin * _Sheen;
                color += _GoldColor.rgb * _GoldAmount * (gold + edge * edge * sky * 0.24h);

                // Tiny brightness drift; zero speed or amount gives a static edge.
                half breath = 1.0h + _BreathAmount * sin(_Time.y * _BreathSpeed * 6.2831853);
                color += _RimColor.rgb * pow(edge, _RimPower) * _RimStrength * breath;
                return half4(MixFog(color, input.roomAndFog.w), 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            half4 DepthFrag(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            ZWrite On
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment NormalFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            half4 NormalFrag(Varyings input) : SV_Target
            {
                half3 n = NormalizeNormalPerPixel(input.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 octNormal = PackNormalOctQuadEncode(n);
                    return half4(PackFloat2To888(saturate(octNormal * 0.5 + 0.5)), 0);
                #else
                    return half4(n, 0);
                #endif
            }
            ENDHLSL
        }
    }
    FallBack Off
}
