Shader "Modern8bitTanks/URPHd2dDesertGround"
{
    Properties
    {
        [PerRendererData] _MainTex ("Ground Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "URPHd2dDesertGroundPass"
            Tags { "LightMode" = "SRPDefaultUnlit" }

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
                float3 worldPos    : TEXCOORD1;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            float _Hd2dTime;
            float _Hd2dEnabled;

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 wp = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(wp);
                OUT.uv = IN.uv;
                OUT.color = IN.color * _Color;
                OUT.worldPos = wp;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 texCol = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv) * IN.color;
                if (_Hd2dEnabled < 0.5)
                    return texCol;

                float2 wp = IN.worldPos.xy;
                float t = _Hd2dTime;
                half3 rgb = texCol.rgb;

                // 1. Subtle natural desert dune relief & tactile sand grain
                float duneGrad =
                    0.55 * cos(wp.x * 0.24 + wp.y * 0.38) +
                    0.32 * sin(wp.x * 0.51 - wp.y * 0.26 + 1.3) +
                    0.18 * sin(wp.y * 0.72);
                half3 duneShade = lerp(half3(0.93h, 0.92h, 0.96h), half3(1.05h, 1.03h, 0.97h), (half)saturate(duneGrad * 0.5 + 0.5));

                float2 grainCell = floor(wp * 16.0);
                float rnd = hash21(grainCell);
                float grainTint = (rnd - 0.5) * 0.035;
                rgb = rgb * duneShade * (1.0h + (half)grainTint);

                // 2. Smooth drifting cloud shadows across the desert terrain
                float2 cUv = wp * 0.068 + float2(-t * 0.042, t * 0.022);
                float cloud =
                    0.50 * sin(cUv.x * 1.4 + cUv.y * 0.9) +
                    0.32 * cos(cUv.x * 2.3 - cUv.y * 1.7 + 0.8) +
                    0.20 * sin(cUv.y * 2.9 + 1.6);
                half cloudShade = (half)smoothstep(0.20, 0.88, cloud) * 0.13h;
                rgb = lerp(rgb, rgb * half3(0.66h, 0.72h, 0.86h), cloudShade);

                return half4(rgb, texCol.a);
            }
            ENDHLSL
        }
    }
}
