Shader "KINO/Board Graphic"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Mode ("0: blue panel, 1: gold marker, 2: badge, 3: artwork", Float) = 0
        _Color ("Tint", Color) = (1,1,1,1)
        _BoardRect ("Panel rectangle in board UV", Vector) = (0,0,1,1)
        _MotionSpeed ("Neon motion speed", Range(0,2)) = .7
        _GlowStrength ("Neon brightness", Range(0,2)) = 1
        [HideInInspector] _PreviewTime ("Preview time (-1: live)", Float) = -1
        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "KinoGoldSurface.hlsl"
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; UNITY_VERTEX_OUTPUT_STEREO };
            float _Mode;
            sampler2D _MainTex;
            float4 _BoardRect;
            float _MotionSpeed, _GlowStrength, _PreviewTime;
            fixed4 _Color;
            float2 Perimeter(float phase)
            {
                // Constant-speed travel around the number field, in board UV space.
                float d = frac(phase) * 3.284;
                if (d < .897) return float2(.055 + d, .799);
                d -= .897;
                if (d < .745) return float2(.952, .799 - d);
                d -= .745;
                if (d < .897) return float2(.952 - d, .054);
                return float2(.055, .054 + d - .897);
            }
            float3 Atmosphere(float2 uv, float time)
            {
                float2 aspect = float2(1.77, 1);
                float2 a = (uv - Perimeter(time * .035)) * aspect;
                float2 b = (uv - Perimeter(time * .035 - .07)) * aspect;
                float2 c = (uv - Perimeter(-time * .025 + .54)) * aspect;
                float halo = exp(-dot(a,a) * 35) + .55 * exp(-dot(b,b) * 28) + .7 * exp(-dot(c,c) * 40);
                float2 q = abs((uv - float2(.5035, .4265)) * aspect) - float2(.784, .362);
                float frame = length(max(q,0)) + min(max(q.x,q.y),0) - .01;
                float neonLine = exp(-abs(frame) * 470);
                float bloom = exp(-abs(frame) * 65);
                float ribbon = exp(-pow((uv.x - .5 - .62 * sin(time * .18) + (uv.y - .5) * .36) * 7, 2));
                float edgeMask = pow(abs(uv.x * 2 - 1), 2) + .25 * pow(uv.y, 3);
                float3 light = float3(.005,.21,.32) * halo;
                light += float3(.015,.40,.55) * neonLine * (.16 + halo * .8);
                light += float3(.002,.08,.18) * bloom * (.2 + halo);
                light += float3(.003,.035,.075) * ribbon * edgeMask;
                return light * _GlowStrength;
            }
            v2f vert(appdata v)
            {
                v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.color=v.color*_Color; return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 p=i.uv*2-1;
                if (_Mode < .5 || _Mode > 2.5)
                {
                    float time = (_PreviewTime >= 0 ? _PreviewTime : _Time.y) * _MotionSpeed;
                    float2 boardUV = _BoardRect.xy + i.uv * _BoardRect.zw;
                    float3 atmosphere = Atmosphere(boardUV, time);
                    if (_Mode > 2.5)
                        return fixed4(tex2D(_MainTex, i.uv).rgb + atmosphere, 1) * i.color;
                    float glow=pow(abs(p.x),5)*.35 + pow(abs(p.y),8)*.13;
                    float3 blue=lerp(float3(.008,.055,.17),float3(.01,.21,.43),glow);
                    return fixed4(GammaToLinearSpace(blue) + atmosphere,1)*i.color;
                }
                float radius=length(p);
                float edge=1-smoothstep(.96,1,radius);
                if (_Mode > 1.5) return fixed4(.99,.98,.90,edge)*i.color;
                float3 gold=KinoGoldSurface(float3(p, sqrt(saturate(1-dot(p,p)))));
                return fixed4(GammaToLinearSpace(gold),edge)*i.color;
            }
            ENDCG
        }
    }
}
