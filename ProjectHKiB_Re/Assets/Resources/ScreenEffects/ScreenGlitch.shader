// 디지털 글리치 — 화면이 가로로 찢겨 어긋나고(블록 슬라이스) RGB 채널이 갈라진다.
//
// [노이즈 셰이더와 다른 점] ProceduralScreenNoise는 화면을 노이즈로 "덮는" 셰이더라
// 원본 화면을 볼 필요가 없다. 글리치는 반대로 **원본 화면을 일그러뜨리는** 것이라
// 지금 그려진 화면을 읽어야 한다.
//
// [어떻게 화면을 읽나] URP에서는 GrabPass도 OnRenderImage도 쓸 수 없다. 게다가 2D 스프라이트는
// 전부 Transparent 큐라 _CameraOpaqueTexture에는 아무것도 담기지 않는다(배경뿐이다).
// 대신 URP **2D 렌더러**가 제공하는 _CameraSortingLayerTexture를 쓴다 — 지정한 정렬 레이어까지
// 그려진 결과를 그대로 담아 주는, 2D 왜곡 연출용으로 만들어진 텍스처다.
//
// 이 프로젝트는 Settings/Renderer2D.asset에서 이미 켜져 있고(Camera Sorting Layer Texture),
// 경계가 **Top**으로 잡혀 있다. 즉 월드 전체(Default~Top)가 담기고, 그 뒤의
// EffectDither/Blur/StandingCG/UI는 담기지 않는다.
//
// [그래서 지켜야 하는 것 두 가지]
//   1. 이 셰이더를 쓰는 캔버스는 **Screen Space - Camera**여야 한다. Overlay 캔버스는 카메라
//      렌더 루프 밖에서 그려져 그 텍스처가 묶여 있다는 보장이 없다.
//   2. 캔버스의 정렬 레이어는 경계(Top)보다 **뒤**여야 한다. 앞에 두면 아직 그려지지도 않은
//      화면을 읽게 된다. ScreenEffectManager는 Blur 레이어에 놓는다.
//
// [무엇이 일그러지나] 월드만 일그러지고, 그 뒤에 그려지는 대화창·메뉴·StandingCG는 멀쩡하다 —
// 연출 중에도 대사가 읽혀야 하므로 오히려 이쪽이 바람직하다.
Shader "UI/ScreenGlitch"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        [HideInInspector] _Color ("Tint", Color) = (1, 1, 1, 1)

        _Intensity ("Intensity", Range(0, 1)) = 1
        _Opacity ("Opacity", Range(0, 1)) = 1

        // 가로로 찢겨 어긋나는 정도(화면 폭 대비)와, 몇 겹으로 찢을지.
        _BlockShift ("Block Shift", Range(0, 0.5)) = 0.06
        _BlockDensity ("Block Density", Range(1, 128)) = 24
        // 매 프레임 몇 퍼센트의 띠만 어긋나게 할지. 1이면 전부 흔들려 죽처럼 보인다.
        _BlockCoverage ("Block Coverage", Range(0, 1)) = 0.35

        // RGB 스플릿(색수차) — 채널을 서로 반대로 밀어 놓는다.
        _RgbSplit ("RGB Split", Range(0, 0.1)) = 0.006
        _SplitAngle ("RGB Split Angle", Range(0, 6.2832)) = 0

        // 주사선과 세로 흔들림.
        _Scanline ("Scanline", Range(0, 1)) = 0.25
        _Jitter ("Vertical Jitter", Range(0, 0.2)) = 0.01

        _GlitchTime ("Unscaled Time", Float) = 0
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
            Name "Screen Glitch"

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
                float4 screenPos : TEXCOORD2;
            };

            sampler2D _MainTex;
            float4 _ClipRect;

            // URP 2D 렌더러가 Top 레이어까지 그린 뒤 채워 주는 화면 사본. 이걸 읽어서 일그러뜨린다.
            sampler2D _CameraSortingLayerTexture;

            float _Intensity;
            float _Opacity;
            float _BlockShift;
            float _BlockDensity;
            float _BlockCoverage;
            float _RgbSplit;
            float _SplitAngle;
            float _Scanline;
            float _Jitter;
            float _GlitchTime;
            float _Seed;

            float Hash11(float p)
            {
                p = frac(p * 0.1031);
                p *= p + 33.33;
                p *= p + p;
                return frac(p);
            }

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
                o.screenPos = ComputeScreenPos(o.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // 화면 좌표로 읽는다. 이 쿼드가 화면을 정확히 덮지 않아도 원본과 어긋나지 않는다.
                float2 screenUV = i.screenPos.xy / max(i.screenPos.w, 1e-5);

                // 글리치는 매끄럽게 흐르면 안 된다 — 프레임을 뚝뚝 끊어 "튀는" 느낌을 만든다.
                float frame = floor(_GlitchTime * 24.0 + _Seed);

                // ── 1. 가로 띠 어긋남 ────────────────────────────────
                // 화면을 가로 띠로 나누고, 그중 일부만 좌우로 민다. 띠마다 다른 난수를 쓰되
                // 프레임이 바뀔 때만 갱신되므로 지지직 끊기는 모양이 된다.
                float band = floor(screenUV.y * _BlockDensity);
                float bandRand = Hash21(float2(band, frame));
                // bandRand가 임계값을 넘는 띠만 어긋난다 — 전부 흔들면 형체가 사라진다.
                float bandActive = step(1.0 - _BlockCoverage, bandRand);
                float bandDir = Hash21(float2(band, frame + 7.0)) * 2.0 - 1.0;
                float shift = bandDir * _BlockShift * bandActive * _Intensity;

                // 아주 가끔 화면 전체가 한 칸 밀리는 큰 튐. 없으면 너무 규칙적으로 보인다.
                float bigJump = step(0.93, Hash11(frame * 1.37 + _Seed));
                shift += bigJump * (Hash11(frame * 2.11) * 2.0 - 1.0) * _BlockShift * 1.5 * _Intensity;

                // 세로 흔들림(수직 동기 어긋남).
                float jitter = (Hash11(frame * 0.71 + _Seed) * 2.0 - 1.0) * _Jitter * _Intensity;

                float2 uv = screenUV + float2(shift, jitter);

                // ── 2. RGB 스플릿 ────────────────────────────────────
                // 채널마다 반대 방향으로 밀어 색수차를 만든다. 띠가 어긋난 자리에서 더 크게 벌어져야
                // "찢어진 곳이 번진다"는 인상이 생긴다.
                float split = _RgbSplit * _Intensity * (1.0 + bandActive * 1.5);
                float2 splitDir = float2(cos(_SplitAngle), sin(_SplitAngle)) * split;

                float3 rgb;
                rgb.r = tex2D(_CameraSortingLayerTexture, saturate(uv + splitDir)).r;
                rgb.g = tex2D(_CameraSortingLayerTexture, saturate(uv)).g;
                rgb.b = tex2D(_CameraSortingLayerTexture, saturate(uv - splitDir)).b;

                // ── 3. 주사선 ────────────────────────────────────────
                float scan = sin(screenUV.y * _ScreenParams.y * 1.4 + _GlitchTime * 24.0) * 0.5 + 0.5;
                rgb *= lerp(1.0, 0.65 + scan * 0.35, _Scanline * _Intensity);

                // 어긋난 띠는 살짝 밝게 튀도록 — 신호가 끊긴 자리의 번쩍임.
                rgb += bandActive * bigJump * 0.12 * _Intensity;

                float alpha = _Opacity * i.color.a;
                fixed4 color = fixed4(saturate(rgb) * i.color.rgb, saturate(alpha));
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
