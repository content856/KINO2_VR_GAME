Shader "KINO/Catch Burst"
{
    // Pooled catch bursts: streaking sparks with drag and gravity, an expanding shockwave
    // ring and a short core flash. Sixteen bursts share one mesh and one draw call;
    // positions, colours and start times come from per-renderer arrays.
    Properties
    {
        _Intensity ("Intensity", Range(0, 4)) = 1.6
        _Gravity ("Spark gravity (m/s²)", Range(0, 10)) = 3
        _Drag ("Spark drag", Range(0, 10)) = 3.2
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend One One, Zero One
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Intensity, _Gravity, _Drag;
                float _BurstNow;
            CBUFFER_END
            float4 _BurstPos[16];    // xyz = position, w = start time
            float4 _BurstColor[16];  // rgb = colour, a = size scale

            struct Attributes
            {
                float4 positionOS : POSITION;  // xy = quad corner, z = burst slot
                float4 shape : TEXCOORD0;      // x = kind (0 spark, 1 ring, 2 flash), yzw = direction
                float4 data : TEXCOORD1;       // speed (m/s), life (s), size (m), brightness
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 color : COLOR;
                half3 corner : TEXCOORD0;      // xy = corner, z = kind
                half fade : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                int slot = (int)input.positionOS.z;
                float4 burst = _BurstPos[slot];
                float4 tint = _BurstColor[slot];
                float scale = max(tint.a, 0.1);
                float kind = input.shape.x;
                float life = input.data.y * (0.85 + 0.15 * scale);
                float age = _BurstNow - burst.w;
                float n = saturate(age / life);
                float3 origin = burst.xyz;
                float3 toCamera = GetCameraPositionWS() - origin;
                float3 view = normalize(toCamera);
                float3 right = UNITY_MATRIX_V[0].xyz;
                float3 up = UNITY_MATRIX_V[1].xyz;
                float2 c = input.positionOS.xy;
                float3 p;
                float fade;
                if (kind < 0.5)
                {
                    // Spark: analytic motion with drag and gravity, stretched along its velocity.
                    float3 v0 = input.shape.yzw * input.data.x * scale;
                    float k = _Drag;
                    float decay = exp(-k * age);
                    float3 g = float3(0, -_Gravity, 0);
                    float3 position = origin + v0 * (1.0 - decay) / k + 0.5 * g * age * age;
                    float3 velocity = v0 * decay + g * age;
                    float speed = max(length(velocity), 0.001);
                    float3 axis = velocity / speed;
                    float3 side = normalize(cross(axis, normalize(GetCameraPositionWS() - position)) + 1e-5);
                    float len = max(speed * 0.04, input.data.z);
                    float width = input.data.z * 0.45 * (1.0 - 0.5 * n);
                    p = position + axis * (c.y * len) + side * (c.x * width);
                    fade = pow(1.0 - n, 1.4);
                }
                else if (kind < 1.5)
                {
                    // Shockwave ring, easing out.
                    float grow = 1.0 - (1.0 - n) * (1.0 - n);
                    float radius = lerp(0.03, input.data.z * scale, grow);
                    p = origin + (right * c.x + up * c.y) * radius;
                    fade = (1.0 - n) * (1.0 - n);
                }
                else
                {
                    // Core flash.
                    float radius = input.data.z * scale * (0.6 + 1.4 * n);
                    p = origin + view * 0.02 + (right * c.x + up * c.y) * radius;
                    fade = exp(-n * 5.0);
                }
                bool alive = age >= 0.0 && age < life;
                output.positionCS = alive ? TransformWorldToHClip(p) : float4(0, 0, -2, 1);
                output.corner = half3(c, kind);
                output.fade = alive ? fade : 0;
                output.color = tint.rgb * input.data.w * _Intensity;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half2 c = input.corner.xy;
                half a;
                if (input.corner.z < 0.5)
                {
                    // Bright streak: soft across, tapering at both ends.
                    a = saturate(1.0 - c.x * c.x) * saturate(1.0 - c.y * c.y);
                    a = a * a;
                }
                else if (input.corner.z < 1.5)
                {
                    half r = length(c);
                    half d = (r - 0.82) / 0.09;
                    a = exp(-d * d);
                }
                else
                {
                    half r2 = dot(c, c);
                    a = saturate(1.0 - r2);
                    a = a * a * a;
                }
                // Whiten the hottest part of each element.
                half3 rgb = input.color * a * input.fade;
                rgb += a * a * input.fade * 0.35 * dot(input.color, half3(0.33, 0.33, 0.33));
                return half4(rgb, 0);
            }
            ENDHLSL
        }
    }
}
