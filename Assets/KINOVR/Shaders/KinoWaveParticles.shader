Shader "KINO/Wave Particles"
{
    // Camera-facing soft dots, animated entirely on the GPU and periodic over 120 seconds.
    // Ambient dust drifts upward through the space; riders stream along the wave ribbons.
    Properties
    {
        _Intensity ("Intensity", Range(0, 3)) = 1
        _CalmFront ("Calm amplitude in front", Range(0, 1)) = .3
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend One One, Zero One
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "KinoWaveCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Intensity, _CalmFront;
                float _WaveTime, _Visibility, _Reveal, _Dim, _Collapse, _Flash;
                float4 _Origin;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;  // xy = quad corner (-1..1)
                float4 place : TEXCOORD0;      // azimuth (rad), rise phase (0..1), radius (m), size (degrees)
                float4 motion : TEXCOORD1;     // sway direction, rise cycles per loop, phase, rise span (m)
                float4 shape : TEXCOORD2;      // riders: bundle centre, amplitude, azimuth cycles, speed cycles
                float4 twist : TEXCOORD3;      // riders: bundle phase, twist, spread, radius
                float4 ride : TEXCOORD4;       // x = 1 for riders, y = s across the bundle, z = start u, w = laps per loop
                half4 color : COLOR;           // rgb colour, a brightness
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 color : COLOR;
                half2 corner : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float t = _WaveTime * KW_W;
                float3 centre;
                float mask, edge;
                if (input.ride.x > 0.5)
                {
                    // Whole laps per loop: the rider travels along its ribbon without a seam.
                    float u = frac(input.ride.z + input.ride.w * _WaveTime / KW_LOOP_SECONDS);
                    centre = KinoWavePoint(u, input.ride.y, input.shape, input.twist, _Origin, _WaveTime,
                        _CalmFront, _Collapse, _Reveal, mask);
                    // Riders pulse brighter as they pass, like light running along the lines.
                    edge = 0.6 + 0.4 * sin(t * 24.0 + input.motion.z);
                }
                else
                {
                    // Gentle sideways sway, periodic within the loop.
                    float theta = input.place.x + 0.08 * input.motion.x * sin(t * 2.0 + input.motion.z);
                    // Whole rise cycles per loop: drifts upward and wraps without a seam.
                    float rise = frac(input.motion.y * _WaveTime / KW_LOOP_SECONDS + input.place.y);
                    float y = (rise - 0.5) * input.motion.w;
                    edge = smoothstep(0.0, 0.12, rise) * smoothstep(1.0, 0.88, rise);
                    float r = input.place.z;
                    centre = float3(_Origin.x + r * sin(theta), _Origin.y + y, _Origin.z + r * cos(theta));
                    float delta = theta - _Origin.w;
                    float away = abs(atan2(sin(delta), cos(delta)));
                    mask = saturate((_Reveal * 3.4 - away) / 0.35);
                    edge *= 0.55 + 0.45 * sin(t * 32.0 + input.motion.z);
                }
                float3 toCamera = GetCameraPositionWS() - centre;
                float distance = max(length(toCamera), 0.01);
                float size = distance * tan(radians(input.place.w));
                float3 right = UNITY_MATRIX_V[0].xyz;
                float3 up = UNITY_MATRIX_V[1].xyz;
                float3 p = centre + (right * input.positionOS.x + up * input.positionOS.y) * size;
                output.positionCS = TransformWorldToHClip(p);
                output.corner = input.positionOS.xy;

                // Riders follow the collapse into the beam and flare with the flash; dust fades out.
                float collapse = input.ride.x > 0.5 ? (1.0 + 1.5 * _Collapse + 2.0 * _Flash) : (1.0 - 0.7 * _Collapse);
                float level = _Visibility * mask * edge * (1.0 - 0.5 * _Dim) * collapse * _Intensity;
                output.color = input.color.rgb * input.color.a * level;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half r2 = dot(input.corner, input.corner);
                half a = saturate(1.0 - r2);
                // Bright core with a soft halo.
                a = a * a * 0.6 + a * a * a * a * a * a * 1.4;
                return half4(input.color * a, 0);
            }
            ENDHLSL
        }
    }
}
