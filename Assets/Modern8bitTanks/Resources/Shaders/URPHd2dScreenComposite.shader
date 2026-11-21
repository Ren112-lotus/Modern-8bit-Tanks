Shader "Modern8bitTanks/URPHd2dScreenComposite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Game RenderTexture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _TiltShiftStrength ("Tilt-Shift Bokeh Strength", Range(0, 2.5)) = 1.0
        _FocusCenter ("Focus Band Center Y", Range(0.2, 0.8)) = 0.50
        _FocusWidth ("Sharp Focus Half-Width", Range(0.10, 0.45)) = 0.25
        _TexelSize ("Game RT Texel Size", Vector) = (0.000694, 0.001235, 1440, 810)

        // Standard uGUI RawImage stencil/mask properties
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
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
            "RenderPipeline" = "UniversalPipeline"
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
            Name "HD2DTiltShiftDOF"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                half4  color       : COLOR;
                float2 uv          : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            float _Hd2dEnabled;

            CBUFFER_START(UnityPerMaterial)
                float4 _TexelSize;
                half4  _Color;
                float  _TiltShiftStrength;
                float  _FocusCenter;
                float  _FocusWidth;
            CBUFFER_END

            // 12-tap Golden-Angle Vogel Disk offsets for silky circular photographic Bokeh Depth-of-Field
            static const float2 BokehTaps[12] =
            {
                float2( 0.289,  0.000),
                float2(-0.302,  0.276),
                float2( 0.044, -0.498),
                float2( 0.365,  0.464),
                float2(-0.628, -0.145),
                float2( 0.562, -0.432),
                float2(-0.181,  0.744),
                float2(-0.453, -0.680),
                float2( 0.858,  0.146),
                float2(-0.822,  0.422),
                float2( 0.354, -0.890),
                float2( 0.388,  0.908)
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.color = IN.color * _Color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;

                if (_Hd2dEnabled < 0.5)
                    return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv) * IN.color;

                // Pure Photographic HD-2D Tilt-Shift Depth of Field:
                // Center combat band stays 100% tack-sharp; top horizon & bottom foreground blur into silky bokeh!
                // Scale radius proportionally to RT height so 480x270 matches the exact physical blur width of 1440x810!
                float dy = abs(uv.y - _FocusCenter);
                float blurMask = smoothstep(_FocusWidth, 0.51, dy);
                float edgeScale = uv.y > _FocusCenter ? 1.05 : 0.78;
                float resScale = clamp(_TexelSize.w / 810.0, 0.25, 2.0);
                float blurPx = blurMask * edgeScale * (6.8 * resScale) * _TiltShiftStrength;

                half3 col;
                if (blurPx > 0.25)
                {
                    float2 stepUv = _TexelSize.xy * blurPx;
                    half3 accum = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb;
                    half totalW = 1.0h;

                    [unroll]
                    for (int i = 0; i < 12; i++)
                    {
                        float2 sampleUv = clamp(uv + BokehTaps[i] * stepUv, float2(0.001, 0.001), float2(0.999, 0.999));
                        half3 s = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, sampleUv).rgb;
                        // Bright highlights bloom gently into smooth bokeh discs
                        half lum = dot(s, half3(0.299h, 0.587h, 0.114h));
                        half w = 1.0h + smoothstep(0.68h, 1.20h, lum) * 1.25h;
                        accum += s * w;
                        totalW += w;
                    }
                    col = accum / totalW;
                }
                else
                {
                    col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb;
                }

                return half4(col, 1.0h) * IN.color;
            }
            ENDHLSL
        }
    }
}
