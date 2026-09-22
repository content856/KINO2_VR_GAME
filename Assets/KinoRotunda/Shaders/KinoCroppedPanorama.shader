Shader "KINO/Cropped Panorama Skybox"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("360 degree strip (linear)", 2D) = "grey" {}
        _Tint ("Tint", Color) = (.5,.5,.5,1)
        _Exposure ("Exposure", Range(0,8)) = 1
        _Rotation ("Rotation", Range(0,360)) = 0
        _BottomElevation ("Bottom elevation", Float) = -30
        _VerticalDegrees ("Captured vertical angle", Float) = 90
        _Zenith ("Uncaptured zenith", Color) = (.0094,.331,.55,1)
        _Nadir ("Uncaptured nadir", Color) = (.18,.17,.14,1)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            half4 _Tint, _Zenith, _Nadir;
            float _Exposure, _Rotation, _BottomElevation, _VerticalDegrees;
            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float3 direction : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float s, c;
                sincos(radians(_Rotation), s, c);
                float3 rotated = float3(c*v.vertex.x-s*v.vertex.z, v.vertex.y, s*v.vertex.x+c*v.vertex.z);
                o.pos = UnityObjectToClipPos(float4(rotated,1));
                o.direction = v.vertex.xyz;
                return o;
            }
            half4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 d = normalize(i.direction);
                float u = .5 - atan2(d.z,d.x) / (2*UNITY_PI);
                float elevation = degrees(asin(clamp(d.y,-1,1)));
                float v = (elevation - _BottomElevation) / _VerticalDegrees;
                float halfTexel = .5 * _MainTex_TexelSize.y;
                half3 color = tex2D(_MainTex,float2(u,clamp(v,halfTexel,1-halfTexel))).rgb;
                // Preserve every captured latitude. Only missing polar regions blend to a
                // constant, avoiding stretched buildings/clouds and a singularity at poles.
                float outside = max(-v,v-1) * _VerticalDegrees;
                float blend = smoothstep(0,18,max(0,outside));
                color = lerp(color, v > 1 ? _Zenith.rgb : _Nadir.rgb, blend);
                return half4(color * _Tint.rgb * 2 * _Exposure,1);
            }
            ENDCG
        }
    }
    Fallback Off
}
