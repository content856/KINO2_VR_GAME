Shader "KINO/Ball Lacquer"
{
    Properties
    {
        _BaseColor ("Lacquer tint", Color) = (1,1,1,1)
        _EnvironmentAmount ("Room lighting influence", Range(0,1)) = .16
        [HideInInspector] _BaseMap ("Base map", 2D) = "white" {}
        [HideInInspector] _Cutoff ("Cutoff", Float) = .5
        [HideInInspector] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "Lacquer"
            Tags { "LightMode"="UniversalForward" }
            ZWrite On Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "KinoGoldSurface.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _EnvironmentAmount;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float fog : TEXCOORD2;
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
                output.fog = ComputeFogFactor(position.positionCS.z);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 normalWS = normalize(input.normalWS);
                float3 normalVS = normalize(mul((float3x3)GetWorldToViewMatrix(), normalWS));
                float3 gold = SRGBToLinear(KinoGoldSurface(normalVS)) * _BaseColor.rgb;
                Light light = GetMainLight();
                float3 room = SampleSH(normalWS) + light.color * saturate(dot(normalWS, light.direction));
                gold *= lerp(float3(1, 1, 1), clamp(room, .35, 1.25), _EnvironmentAmount);
                return half4(MixFog(gold, input.fog), 1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}
