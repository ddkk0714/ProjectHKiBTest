Shader "UI/ProceduralScreenNoise"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        [HideInInspector] _Color ("Tint", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Range(0, 1)) = 0.6
        _Opacity ("Opacity", Range(0, 1)) = 1
        _GrainTiling ("Grain Tiling", Float) = 16
        _NoiseTime ("Unscaled Time", Float) = 0
        _Seed ("Seed", Float) = 0

        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
        [HideInInspector] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Procedural Screen Noise"

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float4 worldPosition : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4 _ClipRect;
            float _Intensity;
            float _Opacity;
            float _GrainTiling;
            float _NoiseTime;
            float _Seed;

            float Hash21(float2 p)
            {
                p = frac(p * float2(0.1031, 0.1030));
                p += dot(p, p.yx + 33.33);
                return frac((p.x + p.y) * p.x);
            }

            v2f vert(appdata_t v)
            {
                v2f o;
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Fine grain is screen-pixel based, so it never reveals a tiled source texture.
                float frame = floor(_NoiseTime * 30.0 + _Seed);
                float2 pixel = floor(i.uv * _ScreenParams.xy);
                float fineGrain = Hash21(pixel + frame * float2(17.0, 53.0));

                // A second, independently generated layer gives the noise analogue clusters rather
                // than uniformly random digital dots. Larger tiling values retain the old API's
                // "smaller grain" intent.
                float density = max(_GrainTiling, 1.0) * 64.0;
                float2 clusterGrid = float2(density, density * (_ScreenParams.y / _ScreenParams.x));
                float clusterGrain = Hash21(floor(i.uv * clusterGrid) + frame * float2(7.0, 29.0));

                float rowNoise = Hash21(float2(floor(i.uv.y * _ScreenParams.y * 0.45), frame));
                float scanline = sin(pixel.y * 0.58 + _NoiseTime * 18.0) * 0.5 + 0.5;
                float rollingBand = pow(1.0 - abs(frac(i.uv.y - _NoiseTime * 0.125) * 2.0 - 1.0), 28.0);
                float interference = step(0.92, rowNoise) * (0.35 + rollingBand * 0.65);

                float noisyLuma = lerp(fineGrain, clusterGrain, 0.28);
                noisyLuma = saturate(noisyLuma + (scanline - 0.5) * 0.10 + interference * 0.18);
                // Opacity와 별개로 패턴 대비만 조절한다. _Opacity가 1이면 아래 알파가 항상
                // 1이므로, intensity 값과 무관하게 원본 화면은 전혀 비치지 않는다.
                float luma = lerp(0.5, noisyLuma, _Intensity);

                float3 chroma = float3(
                    Hash21(pixel + frame * float2(101.0, 31.0)),
                    Hash21(pixel + frame * float2(47.0, 89.0)),
                    Hash21(pixel + frame * float2(13.0, 151.0))) - 0.5;
                float3 noiseColor = saturate(luma.xxx + chroma * (_Intensity * (0.025 + interference * 0.10)));

                float alpha = _Opacity;
                fixed4 color = fixed4(noiseColor, saturate(alpha)) * i.color;
                color.a *= tex2D(_MainTex, i.uv).a;

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
