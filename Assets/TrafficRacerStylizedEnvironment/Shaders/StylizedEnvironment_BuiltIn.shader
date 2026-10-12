Shader "TrafficRacer/Stylized Environment Built-In"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.22, 0.48, 0.20, 1)
        _VariationColor ("Variation Color", Color) = (0.34, 0.62, 0.25, 1)
        _AccentColor ("Accent Color", Color) = (0.13, 0.32, 0.13, 1)
        _PatchScale ("Large Patch Scale", Range(0.01, 1.0)) = 0.08
        _DetailScale ("Small Detail Scale", Range(0.05, 4.0)) = 0.55
        _VariationStrength ("Color Variation Strength", Range(0, 1)) = 0.62
        _AccentThreshold ("Accent Threshold", Range(0, 1)) = 0.73
        _AccentStrength ("Accent Strength", Range(0, 1)) = 0.22
        _ColorSteps ("Color Steps (Low-Poly Style)", Range(2, 8)) = 4
        _Metallic ("Metallic", Range(0, 1)) = 0
        _Smoothness ("Smoothness", Range(0, 1)) = 0.18
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 150

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        #include "UnityCG.cginc"

        fixed4 _BaseColor;
        fixed4 _VariationColor;
        fixed4 _AccentColor;
        float _PatchScale;
        float _DetailScale;
        float _VariationStrength;
        float _AccentThreshold;
        float _AccentStrength;
        float _ColorSteps;
        half _Metallic;
        half _Smoothness;

        struct Input
        {
            float3 worldPos;
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
            float largePatch = ValueNoise3D(worldPosition * _PatchScale);
            float fineDetail = ValueNoise3D(worldPosition * _DetailScale);
            float pattern = saturate(largePatch * 0.78 + fineDetail * 0.22);

            float steps = max(2.0, floor(_ColorSteps + 0.5));
            pattern = floor(pattern * (steps - 1.0) + 0.5) / (steps - 1.0);

            half3 color = lerp(_BaseColor.rgb, _VariationColor.rgb,
                               pattern * _VariationStrength);
            float accentMask = smoothstep(_AccentThreshold, 1.0, largePatch)
                               * _AccentStrength;
            color = lerp(color, _AccentColor.rgb, accentMask);
            return color;
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            o.Albedo = GetProceduralColor(IN.worldPos);
            o.Metallic = _Metallic;
            o.Smoothness = _Smoothness;
            o.Alpha = 1.0;
        }
        ENDCG
    }

    FallBack "Standard"
}
