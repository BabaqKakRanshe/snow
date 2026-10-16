Shader "Hidden/SkyIsland/BuiltinPost"
{
    // Built-in pipeline only (see SkyIslandBuiltinPost.cs). Same math as URP: bloom, vignette,
    // post exposure, contrast in ALEXA LogC (URP LutBuilderHdr), Neutral tonemapping (SRP Core), user LUT in sRGB, film grain.
    Properties { _MainTex ("", 2D) = "white" {} }

    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _MainTex; float4 _MainTex_TexelSize;
    sampler2D _BloomLow;
    sampler2D _BloomTex;
    sampler2D _UserLut;
    float4 _BloomParams;    // x threshold(linear) y knee z intensity w scatter
    float4 _BloomTint;
    float4 _Vignette;       // x intensity*3 y smoothness*5
    float4 _VignetteColor;
    float _PostExposure;
    float _Contrast;        // 1 + contrast/100
    float4 _UserLutParams;  // x 1/w y 1/h z h-1 w contribution
    float4 _GrainParams;    // x intensity y response zw random offset

    half3 SampleBox(float2 uv)
    {
        float4 o = _MainTex_TexelSize.xyxy * float4(-1.0, -1.0, 1.0, 1.0);
        half3 s = tex2D(_MainTex, uv + o.xy).rgb + tex2D(_MainTex, uv + o.zy).rgb +
                  tex2D(_MainTex, uv + o.xw).rgb + tex2D(_MainTex, uv + o.zw).rgb;
        return s * 0.25;
    }

    half3 Prefilter(half3 c)
    {
        c = min(c, 65000.0);
        half br = max(c.r, max(c.g, c.b));
        half knee = _BloomParams.y;
        half soft = clamp(br - _BloomParams.x + knee, 0.0, 2.0 * knee);
        soft = soft * soft / (4.0 * knee + 1e-4);
        half m = max(br - _BloomParams.x, soft) / max(br, 1e-4);
        return c * m;
    }

    half4 FragPrefilter(v2f_img i) : SV_Target { return half4(Prefilter(SampleBox(i.uv)), 1.0); }
    half4 FragDown(v2f_img i) : SV_Target { return half4(SampleBox(i.uv), 1.0); }

    half4 FragUp(v2f_img i) : SV_Target
    {
        half3 high = tex2D(_MainTex, i.uv).rgb;
        float2 t = _MainTex_TexelSize.xy;
        half3 low = (tex2D(_BloomLow, i.uv + float2(-t.x, -t.y)).rgb + tex2D(_BloomLow, i.uv + float2(t.x, -t.y)).rgb +
                     tex2D(_BloomLow, i.uv + float2(-t.x, t.y)).rgb + tex2D(_BloomLow, i.uv + float2(t.x, t.y)).rgb) * 0.25;
        return half4(lerp(high, low, _BloomParams.w), 1.0);
    }

    float3 NeutralCurve(float3 x, float a, float b, float c, float d, float e, float f)
    {
        return ((x * (a * x + c * b) + d * e) / (x * (a * x + b) + d * f)) - e / f;
    }

    float3 NeutralTonemap(float3 x)
    {
        const float a = 0.2, b = 0.29, c = 0.24, d = 0.272, e = 0.02, f = 0.3;
        const float whiteLevel = 5.3;
        float3 whiteScale = (1.0).xxx / NeutralCurve(whiteLevel.xxx, a, b, c, d, e, f);
        x = NeutralCurve(x * whiteScale, a, b, c, d, e, f);
        return x * whiteScale;
    }

    // SRP Core Color.hlsl (USE_PRECISE_LOGC 0) and ACEScc_MIDGRAY, as used by URP for Color Adjustments > Contrast
    float3 LinearToLogC(float3 x) { return 0.244161 * log10(5.555556 * x + 0.047996) + 0.386036; }
    float3 LogCToLinear(float3 x) { return (pow(10.0, (x - 0.386036) / 0.244161) - 0.047996) / 5.555556; }

    float3 ToSRGB(float3 c)
    {
        c = max(c, 0.0);
        return lerp(c * 12.92, 1.055 * pow(c, 1.0 / 2.4) - 0.055, step(0.0031308, c));
    }

    float3 ToLinear(float3 c)
    {
        return lerp(c / 12.92, pow((c + 0.055) / 1.055, 2.4), step(0.04045, c));
    }

    float3 ApplyLut2D(float3 uvw, float3 so)
    {
        uvw.z *= so.z;
        float shift = floor(uvw.z);
        uvw.xy = uvw.xy * so.z * so.xy + so.xy * 0.5;
        uvw.x += shift * so.y;
        float3 a = tex2Dlod(_UserLut, float4(uvw.xy, 0.0, 0.0)).rgb;
        float3 b = tex2Dlod(_UserLut, float4(uvw.xy + float2(so.y, 0.0), 0.0, 0.0)).rgb;
        return lerp(a, b, uvw.z - shift);
    }

    float Hash(float2 p)
    {
        p = frac(p * float2(443.897, 441.423));
        p += dot(p, p.yx + 19.19);
        return frac((p.x + p.y) * p.x);
    }

    half4 FragUber(v2f_img i) : SV_Target
    {
        float3 col = tex2D(_MainTex, i.uv).rgb;
        col += tex2D(_BloomTex, i.uv).rgb * _BloomParams.z * _BloomTint.rgb;
        float2 dist = abs(i.uv - 0.5) * _Vignette.x;
        float vf = pow(saturate(1.0 - dot(dist, dist)), _Vignette.y);
        col *= lerp(_VignetteColor.rgb, (1.0).xxx, vf);
        col *= _PostExposure;
        float3 lc = LinearToLogC(clamp(col, 0.0, 58.85));
        col = max(LogCToLinear((lc - 0.4135884) * _Contrast + 0.4135884), 0.0);
        col = saturate(NeutralTonemap(col));
        if (_UserLutParams.w > 0.0)
        {
            float3 s = ToSRGB(col);
            float3 g = ApplyLut2D(saturate(s), _UserLutParams.xyz);
            col = ToLinear(lerp(s, g, _UserLutParams.w));
        }
        float grain = (Hash(i.uv * _ScreenParams.xy + _GrainParams.zw * 1000.0) - 0.5) * 2.0;
        float lum = dot(col, float3(0.2126, 0.7152, 0.0722));
        float resp = lerp(1.0, 1.0 - sqrt(saturate(lum)), _GrainParams.y);
        col += col * grain * _GrainParams.x * resp * 0.5;
        return half4(col, 1.0);
    }
    ENDCG

    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass { CGPROGRAM
               #pragma vertex vert_img
               #pragma fragment FragPrefilter
               ENDCG }
        Pass { CGPROGRAM
               #pragma vertex vert_img
               #pragma fragment FragDown
               ENDCG }
        Pass { CGPROGRAM
               #pragma vertex vert_img
               #pragma fragment FragUp
               ENDCG }
        Pass { CGPROGRAM
               #pragma vertex vert_img
               #pragma fragment FragUber
               #pragma target 3.0
               ENDCG }
    }
    Fallback Off
}
