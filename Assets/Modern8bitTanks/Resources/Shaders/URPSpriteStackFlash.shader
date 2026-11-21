Shader "Modern8bitTanks/URPSpriteStackFlash"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        [HDR] _FlashColor ("Hit Flash Color", Color) = (2.4, 0.28, 0.16, 1)
        _FlashAmount ("Flash Amount", Range(0, 1)) = 0
        _Charred ("Charred Wreck", Range(0, 1)) = 0
        _Height01 ("Slice Height Ratio", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "URPSpriteStackHD2D"
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
                float2 sunUvDir    : TEXCOORD1;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_TexelSize;

            float _Hd2dEnabled;

            CBUFFER_START(UnityPerMaterial)
                half4  _Color;
                half4  _FlashColor;
                half   _FlashAmount;
                half   _Charred;
                half   _Height01;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.color = IN.color * _Color;

                // Subtle directional sun bevel in rotated sprite UV space (matches upper-left desert sun)
                float3 sunLocal = TransformWorldToObjectDir(float3(-0.66, 0.75, 0.0));
                float2 dir2 = sunLocal.xy;
                float len2 = max(0.0001, length(dir2));
                OUT.sunUvDir = (dir2 / len2) * _MainTex_TexelSize.xy;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 texCol = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv) * IN.color;
                if (texCol.a < 0.01h) discard;

                half h = saturate(_Height01);
                half hd = saturate((half)_Hd2dEnabled);

                // Natural outdoor daylight gradient + subtle ground-contact AO on lower slices
                half groundAo = lerp(0.80h, 1.0h, smoothstep(0.0h, 0.25h, h));
                half3 shadowTint = half3(0.84h, 0.87h, 0.94h);
                half3 sunTint    = half3(1.10h, 1.06h, 0.97h);
                half3 daylight   = lerp(shadowTint, sunTint, h) * lerp(1.0h, groundAo, hd);
                half3 litRgb     = texCol.rgb * daylight;

                // Subtle directional bevel contrast (clean miniature model relief without artificial glow!)
                if (hd > 0.5h)
                {
                    half alphaSun = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv + IN.sunUvDir).a;
                    half alphaShd = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv - IN.sunUvDir).a;

                    half rimMask = saturate(1.0h - alphaSun) * smoothstep(0.12h, 0.85h, h);
                    half shdMask = saturate(1.0h - alphaShd);

                    litRgb = lerp(litRgb, litRgb * half3(0.80h, 0.84h, 0.92h), shdMask * 0.35h);
                    litRgb += half3(0.14h, 0.12h, 0.08h) * rimMask * (1.0h - saturate(_Charred) * 0.75h);
                }

                // Darken if charred wreck
                half3 baseRgb = lerp(litRgb, litRgb * half3(0.32h, 0.30h, 0.28h), saturate(_Charred));

                // Vivid HDR red hit flash (blooms naturally via URP Post-Process!)
                half3 finalRgb = lerp(baseRgb, _FlashColor.rgb, saturate(_FlashAmount));
                return half4(finalRgb, texCol.a);
            }
            ENDHLSL
        }
    }
}
