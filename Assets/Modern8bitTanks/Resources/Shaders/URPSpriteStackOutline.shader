Shader "Modern8bitTanks/URPSpriteStackOutline"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_TexelSize;
            static const float OutlineWidth = 1.25;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                // Sprite-stack slices are full, unatlased rectangles centered on the slice origin.
                float2 expansion = 1.0 + 2.0 * OutlineWidth * _MainTex_TexelSize.xy;
                input.positionOS.xy *= expansion;
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = (input.uv - 0.5) * expansion + 0.5;
                output.color = input.color;
                return output;
            }

            half AlphaAt(float2 uv)
            {
                if (any(uv < 0.0) || any(uv > 1.0)) return 0.0h;
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).a;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 stepUV = OutlineWidth * _MainTex_TexelSize.xy;
                half coverage = AlphaAt(input.uv);
                coverage = max(coverage, AlphaAt(input.uv + float2(stepUV.x, 0.0)));
                coverage = max(coverage, AlphaAt(input.uv - float2(stepUV.x, 0.0)));
                coverage = max(coverage, AlphaAt(input.uv + float2(0.0, stepUV.y)));
                coverage = max(coverage, AlphaAt(input.uv - float2(0.0, stepUV.y)));
                clip(coverage - 0.05h);
                return input.color;
            }
            ENDHLSL
        }
    }
}
