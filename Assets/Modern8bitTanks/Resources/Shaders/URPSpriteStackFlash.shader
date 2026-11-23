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
        _MetallicWear ("Armor Metal & Edge Wear", Range(0, 1)) = 0.35
        [HideInInspector] _OcclusionFocus ("Occlusion Focus", Vector) = (0, 0, 0, 0)
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
            Name "URPSpriteStackArmorShader"
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
                float2 perpUvDir   : TEXCOORD2;
                float2 worldXY     : TEXCOORD3;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_TexelSize;

            float  _Hd2dEnabled;
            half4  _BiomeDustTint;

            CBUFFER_START(UnityPerMaterial)
                half4  _Color;
                half4  _FlashColor;
                half   _FlashAmount;
                half   _Charred;
                half   _Height01;
                half   _MetallicWear;
                float4 _OcclusionFocus;
            CBUFFER_END

            float hash21(float2 p)
            {
                p = frac(p * float2(127.1, 311.7));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            half luma(half3 c)
            {
                return dot(c, half3(0.299h, 0.587h, 0.114h));
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 world = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(world);
                OUT.worldXY = world.xy;
                OUT.uv = IN.uv;
                OUT.color = IN.color * _Color;

                // Transform world sun vector (-0.64, 0.77) into rotated sprite UV space
                float3 sunLocal = TransformWorldToObjectDir(float3(-0.64, 0.77, 0.0));
                float2 dir2 = sunLocal.xy;
                float len2 = max(0.0001, length(dir2));
                float2 nDir = dir2 / len2;
                float2 nPerp = float2(-nDir.y, nDir.x);

                OUT.sunUvDir = nDir * _MainTex_TexelSize.xy;
                OUT.perpUvDir = nPerp * _MainTex_TexelSize.xy;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 texCol = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv) * IN.color;
                if (texCol.a < 0.01h) discard;
                if (_OcclusionFocus.w > 0.001)
                {
                    float distanceToTank = distance(IN.worldXY, _OcclusionFocus.xy);
                    float fade = 1.0 - smoothstep(_OcclusionFocus.z * 0.55, _OcclusionFocus.z, distanceToTank);
                    // Screen pixels share the same coverage test across all overlapping slices.
                    clip(hash21(floor(IN.positionHCS.xy)) - fade * _OcclusionFocus.w * 0.88);
                }

                half h = saturate(_Height01);
                half hd = saturate((half)_Hd2dEnabled);
                half metal = saturate(_MetallicWear);
                half charred = saturate(_Charred);

                // Sample 4 directional neighbors in sprite texel space for true armor relief & edge detection
                half4 sSun  = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv + IN.sunUvDir);
                half4 sShd  = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv - IN.sunUvDir);
                half4 sLeft = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv + IN.perpUvDir);
                half4 sRgt  = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv - IN.perpUvDir);

                // Silhouette rim & shadow-side bevel masks
                half sunRim   = saturate(1.0h - sSun.a);
                half shdRim   = saturate(1.0h - sShd.a);
                half leftRim  = saturate(1.0h - sLeft.a);
                half rgtRim   = saturate(1.0h - sRgt.a);
                half sideEdge = saturate(leftRim + rgtRim);
                half anyEdge  = saturate(sunRim + shdRim + sideEdge * 0.6h);

                // Detect small tank sprites (e.g. 13x23 Wiesel/MS-1) & thin 1-2px gun barrels / cupolas
                // so edge wear, shadow creases, and grain NEVER crush tiny details into mud!
                half spriteMinDim = (half)min(_MainTex_TexelSize.z, _MainTex_TexelSize.w);
                half detailScale = smoothstep(10.0h, 24.0h, spriteMinDim);
                half isThin = saturate(sunRim * shdRim + leftRim * rgtRim);

                // Micro-relief normal approximation from pixel luminance & silhouette slopes
                half lumC = luma(texCol.rgb);
                half lumSun = sSun.a > 0.1h ? luma(sSun.rgb) : lumC * 0.45h;
                half lumShd = sShd.a > 0.1h ? luma(sShd.rgb) : lumC * 0.45h;
                // Positive reliefSlope means this pixel faces the upper-left sun (raised hatch, ERA block, barrel crown)
                half rimBias = (sunRim - shdRim) * (1.0h - isThin * 0.55h);
                half reliefSlope = clamp((lumC - lumSun) * 1.65h + (lumShd - lumC) * 1.35h + rimBias * 0.62h, -1.0h, 1.0h);

                // 1. Stable texel-locked cast-steel micro-grain (gentler on small sprites & thin barrels!)
                float2 texelId = floor(IN.uv * _MainTex_TexelSize.zw);
                half grain = (half)(hash21(texelId + floor(h * 16.0h) * 13.7h) - 0.5);
                half grainAmp = lerp(0.032h, 0.072h, metal * detailScale) * (1.0h - isThin * 0.65h) * hd;
                half3 albedo = texCol.rgb * (1.0h + grain * grainAmp);

                // 2. Ground-contact Ambient Occlusion + subtle biome dust weathering on lower hull/tracks!
                half groundAo = lerp(0.74h, 1.0h, smoothstep(0.0h, 0.34h, h));
                half3 dustCol = (_BiomeDustTint.a > 0.01h) ? _BiomeDustTint.rgb : half3(0.76h, 0.64h, 0.44h);
                half dustMask = (1.0h - smoothstep(0.0h, 0.28h, h)) * metal * (0.20h * hd) * lerp(0.65h, 1.0h, detailScale) * (1.0h - charred);
                albedo = lerp(albedo, dustCol, dustMask);

                // 3. Directional Military Daylight (warm sunlight vs deep cool sky-lit shadow side!)
                half sunFacing = saturate(0.50h + reliefSlope * 0.44h + (h - 0.35h) * 0.38h);
                half3 shadowTint = lerp(half3(0.78h, 0.82h, 0.92h), half3(0.66h, 0.74h, 0.87h), hd * (0.45h + metal * 0.55h));
                half3 sunTint    = lerp(half3(1.06h, 1.03h, 0.96h), half3(1.13h, 1.08h, 0.97h), hd);
                half3 litRgb     = albedo * lerp(shadowTint, sunTint, sunFacing) * lerp(0.90h, groundAo, hd);

                // 4. Shadow-side armor depth crease (protected on thin 1-2px barrels & tiny turrets!)
                half shdCrease = shdRim * (1.0h - isThin * 0.75h) * lerp(0.55h, 1.0h, detailScale) * lerp(0.22h, 0.38h, metal) * hd;
                litRgb = lerp(litRgb, litRgb * half3(0.68h, 0.74h, 0.86h), shdCrease);

                // 5. Tank-specific Rolled Steel Edge Wear + Metallic Specular Glint (crisp even on small vehicles!)
                if (hd > 0.5h && metal > 0.05h)
                {
                    half intact = 1.0h - charred * 0.85h;

                    // Crisp cold steel edge wear: on small sprites focus strictly on sun-facing corners to preserve pixel art
                    half wearEdge = lerp(sunRim * (1.0h - shdRim), anyEdge, detailScale);
                    half wearMask = wearEdge * (1.0h - isThin * 0.50h) * smoothstep(0.24h, 0.88h, h) * metal * intact;
                    half3 steelWearCol = lerp(albedo * 1.32h + half3(0.06h, 0.07h, 0.09h), half3(0.84h, 0.87h, 0.92h), 0.28h);
                    litRgb = lerp(litRgb, steelWearCol, wearMask * (0.22h + sunRim * 0.20h));

                    // Dynamic sun-facing metallic specular highlight (glints smoothly as hull/turret rotate!)
                    half specFactor = pow(saturate(reliefSlope * 0.75h + sunRim * 0.62h), 3.0h) * smoothstep(0.30h, 0.92h, h);
                    half3 specCol = half3(1.12h, 1.07h, 0.94h) * (specFactor * metal * lerp(0.24h, 0.30h, detailScale) * intact);
                    litRgb += specCol;

                    // Cool sky sheen across flat turret roofs / top hull plates
                    half skySpec = smoothstep(0.72h, 1.0h, h) * (1.0h - anyEdge * 0.5h) * metal * 0.06h * intact;
                    litRgb += half3(0.62h, 0.76h, 0.96h) * skySpec;
                }
                else if (hd > 0.5h)
                {
                    // Subtle warm sunlit edge on non-metal props (rocks, bunkers, palms)
                    litRgb += half3(0.11h, 0.09h, 0.06h) * sunRim * smoothstep(0.15h, 0.85h, h);
                }

                // 6. Darken charred wrecks cleanly
                half3 baseRgb = lerp(litRgb, litRgb * half3(0.28h, 0.26h, 0.25h), charred);

                // 7. Vivid HDR red hit flash (blooms smoothly via Post-RT URP Bloom!)
                half3 finalRgb = lerp(baseRgb, _FlashColor.rgb, saturate(_FlashAmount));
                return half4(finalRgb, texCol.a);
            }
            ENDHLSL
        }
    }
}
