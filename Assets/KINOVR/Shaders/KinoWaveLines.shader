Shader "KINO/Wave Lines"
{
    // Every vertex position is computed here from per-line parameters, so the CPU
    // never touches the mesh after it is built. All time terms are whole cycles of
    // a 120-second period: the loop is seamless and _WaveTime wraps at 120.
    Properties
    {
        _Intensity ("Intensity", Range(0, 3)) = 1
        _CoreWidth ("Core width (degrees)", Range(.02, .3)) = .075
        _GlowWidth ("Glow width (degrees)", Range(.1, 2)) = .7
        _GlowStrength ("Glow strength", Range(0, 1)) = .3
        _CalmFront ("Calm amplitude in front", Range(0, 1)) = .3
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        ZTest Always
        // Additive colour; destination alpha preserved for the compositor.
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
                half _Intensity, _CoreWidth, _GlowWidth, _GlowStrength, _CalmFront;
                float _WaveTime, _Visibility, _Reveal, _Dim, _Collapse, _Flash;
                float4 _Origin; // xyz = eye anchor, w = front yaw (radians)
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;  // x = u (0..1 around), y = s (-1..1 across bundle), z = side (-1/+1)
                float4 shape : TEXCOORD0;      // centre offset, amplitude, azimuth cycles, speed cycles
                float4 twist : TEXCOORD1;      // phase, twist, spread, radius
                half4 color : COLOR;           // rgb colour, a brightness
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 color : COLOR;
                half across : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float3 WavePoint(float u, float s, float4 a, float4 b, out float mask)
            {
                return KinoWavePoint(u, s, a, b, _Origin, _WaveTime, _CalmFront, _Collapse, _Reveal, mask);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float mask, unused;
                float3 p = WavePoint(input.positionOS.x, input.positionOS.y, input.shape, input.twist, mask);
                float3 q = WavePoint(input.positionOS.x + 0.0015, input.positionOS.y, input.shape, input.twist, unused);
                float3 tangent = normalize(q - p);
                float3 toCamera = GetCameraPositionWS() - p;
                float distance = max(length(toCamera), 0.01);
                float3 sideways = normalize(cross(tangent, toCamera / distance));
                // Constant angular width: lines stay crisp near and far.
                float halfWidth = distance * tan(radians(_GlowWidth * 0.5));
                p += sideways * (input.positionOS.z * halfWidth);
                output.positionCS = TransformWorldToHClip(p);
                output.across = input.positionOS.z;
                float level = _Visibility * mask * (1.0 - 0.75 * _Dim) * (1.0 + 1.6 * _Collapse) * _Intensity;
                output.color = input.color.rgb * input.color.a * level;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half x = abs(input.across);
                half ratio = _GlowWidth / max(_CoreWidth, 0.001);
                half d = x * ratio;                      // distance in core widths
                half core = exp(-d * d * 2.2);
                half glow = (1.0 - x) * (1.0 - x) * _GlowStrength;
                half3 rgb = input.color * (core + glow);
                // The flash whitens the collapsing beam just before gameplay.
                rgb += (input.color * 2.0 + dot(input.color, half3(0.33, 0.33, 0.33)) * half3(1.2, 1.6, 2.0)) * core * _Flash;
                return half4(rgb, 0);
            }
            ENDHLSL
        }
    }
}
