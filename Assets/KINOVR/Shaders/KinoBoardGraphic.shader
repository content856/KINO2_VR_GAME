Shader "KINO/Board Graphic"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Mode ("0: blue panel, 1: gold marker, 2: number badge", Float) = 0
        _Color ("Tint", Color) = (1,1,1,1)
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
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; UNITY_VERTEX_OUTPUT_STEREO };
            float _Mode;
            fixed4 _Color;
            v2f vert(appdata v)
            {
                v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.color=v.color*_Color; return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 p=i.uv*2-1;
                if (_Mode < .5)
                {
                    float glow=pow(abs(p.x),5)*.35 + pow(abs(p.y),8)*.13;
                    float3 blue=lerp(float3(.008,.055,.17),float3(.01,.21,.43),glow);
                    return fixed4(GammaToLinearSpace(blue),1)*i.color;
                }
                float radius=length(p);
                float edge=1-smoothstep(.96,1,radius);
                if (_Mode > 1.5) return fixed4(.99,.98,.90,edge)*i.color;
                float3 gold=lerp(float3(.91,.55,.015),float3(1,.96,.25),saturate(i.uv.y*.85+.15));
                float shine=exp(-dot(p-float2(-.25,.42),p-float2(-.25,.42))*11);
                gold=lerp(gold,float3(1,1,.9),shine*.8);
                float rim=smoothstep(.86,.94,radius);
                gold=lerp(gold,float3(1,.9,.37),rim);
                return fixed4(GammaToLinearSpace(gold),edge)*i.color;
            }
            ENDCG
        }
    }
}
