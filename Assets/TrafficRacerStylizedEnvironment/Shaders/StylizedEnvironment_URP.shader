Shader "TrafficRacer/Stylized Environment URP"
{
    Properties
    {
        [MainColor] _BaseColor ("Base Color", Color) = (0.22, 0.48, 0.20, 1)
        _VariationColor ("Variation Color", Color) = (0.34, 0.62, 0.25, 1)
        _AccentColor ("Accent Color", Color) = (0.13, 0.32, 0.13, 1)
        _PatchScale ("Large Patch Scale", Range(0.01, 1.0)) = 0.08
        _DetailScale ("Small Detail Scale", Range(0.05, 4.0)) = 0.55
        _VariationStrength ("Color Variation Strength", Range(0, 1)) = 0.62
        _AccentThreshold ("Accent Threshold", Range(0, 1)) = 0.73
        _AccentStrength ("Accent Strength", Range(0, 1)) = 0.22
        _ColorSteps ("Color Steps (Low-Poly Style)", Range(2, 8)) = 4
        _AmbientStrength ("Ambient Brightness", Range(0, 1.5)) = 0.65
        _LightStrength ("Main Light Strength", Range(0, 2)) = 0.9
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }
        LOD 150
        Cull Back

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _VariationColor;
                float4 _AccentColor;
                float _PatchScale;
                float _DetailScale;
                float _VariationStrength;
                float _AccentThreshold;
                float _AccentStrength;
                float _ColorSteps;
                float _AmbientStrength;
                float _LightStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 shadowCoord : TEXCOORD2;
                half fogFactor : TEXCOORD3;
            };

            float Hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float ValueNoise3D(float3 p)
            {
                float3 cell = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float h000 = Hash31(cell + float3(0, 0, 0));
                float h100 = Hash31(cell + float3(1, 0, 0));
                float h010 = Hash31(cell + float3(0, 1, 0));
                float h110 = Hash31(cell + float3(1, 1, 0));
                float h001 = Hash31(cell + float3(0, 0, 1));
                float h101 = Hash31(cell + float3(1, 0, 1));
                float h011 = Hash31(cell + float3(0, 1, 1));
                float h111 = Hash31(cell + float3(1, 1, 1));

                float x00 = lerp(h000, h100, f.x);
                float x10 = lerp(h010, h110, f.x);
                float x01 = lerp(h001, h101, f.x);
                float x11 = lerp(h011, h111, f.x);
                float y0 = lerp(x00, x10, f.y);
                float y1 = lerp(x01, x11, f.y);
                return lerp(y0, y1, f.z);
            }

            half3 GetProceduralColor(float3 worldPosition)
            {
                // World-space noise means adjacent meshes can share a seamless pattern.
                float largePatch = ValueNoise3D(worldPosition * _PatchScale);
                float fineDetail = ValueNoise3D(worldPosition * _DetailScale);
                float pattern = saturate(largePatch * 0.78 + fineDetail * 0.22);

                // Quantized values create broad, graphic color regions for a stylized look.
                float steps = max(2.0, floor(_ColorSteps + 0.5));
                pattern = floor(pattern * (steps - 1.0) + 0.5) / (steps - 1.0);

                half3 color = lerp(_BaseColor.rgb, _VariationColor.rgb,
                                   pattern * _VariationStrength);

                float accentMask = smoothstep(_AccentThreshold, 1.0, largePatch)
                                   * _AccentStrength;
                color = lerp(color, _AccentColor.rgb, accentMask);
                return color;
            }

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.positionWS = positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.shadowCoord = TransformWorldToShadowCoord(positionWS);
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                half3 albedo = GetProceduralColor(IN.positionWS);
                half3 normalWS = normalize(IN.normalWS);
                Light mainLight = GetMainLight(IN.shadowCoord);

                half nDotL = saturate(dot(normalWS, mainLight.direction));
                half shadow = mainLight.shadowAttenuation;
                half3 ambient = max(SampleSH(normalWS), half3(0.12, 0.12, 0.12));
                half3 lighting = ambient * _AmbientStrength
                               + mainLight.color * nDotL * _LightStrength * shadow;

                half3 finalColor = albedo * lighting;
                finalColor = MixFog(finalColor, IN.fogFactor);
                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            ShadowVaryings ShadowVert(ShadowAttributes IN)
            {
                ShadowVaryings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);

                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif

                positionWS = ApplyShadowBias(positionWS, normalWS, lightDirectionWS);
                OUT.positionCS = TransformWorldToHClip(positionWS);

                #if UNITY_REVERSED_Z
                    OUT.positionCS.z = min(OUT.positionCS.z,
                                           OUT.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    OUT.positionCS.z = max(OUT.positionCS.z,
                                           OUT.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif
                return OUT;
            }

            half4 ShadowFrag(ShadowVaryings IN) : SV_TARGET
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
