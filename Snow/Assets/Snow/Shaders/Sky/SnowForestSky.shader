Shader "Snow/Forest/Sky"
{
    // Snow (research 2, 21.10): copy of SkyIsland/Skybox (the core stays untouched) for the Forest day/night cycle - a
    // separate moon disc and glow, and stars turning about the celestial pole with the hour (ForestDayNight drives them).
    // Procedural haze sky for the sky-island location. Works in Built-in and URP.
    // Colours are set from script in linear space (converted with .gamma) so they match the calibrated look.
    Properties
    {
        _HorizonColor ("Horizon", Color) = (0.455, 0.392, 0.145, 1)
        _BandColor ("Bright band", Color) = (0.49, 0.42, 0.161, 1)
        _ZenithColor ("Zenith", Color) = (0.365, 0.312, 0.102, 1)
        _BelowColor ("Below horizon", Color) = (0.38, 0.325, 0.114, 1)
        _BandHeight ("Band height", Range(0, 1)) = 0.2
        _BandWidth ("Band width", Range(0.01, 1)) = 0.16
        _CloudTex ("Cloud noise", 2D) = "gray" {}
        _CloudScale ("Cloud scale", Float) = 0.35
        _CloudContrast ("Cloud contrast", Range(0, 1)) = 0.34
        _CloudSpeed ("Cloud drift", Float) = 0.004
        _SunDir ("Direction to sun", Vector) = (-0.306, 0.342, 0.888, 0)
        _SunColor ("Sun colour", Color) = (1, 0.93, 0.7, 1)
        _SunGlow ("Sun glow", Float) = 0.07
        _SunGlowPower ("Sun glow power", Float) = 6
        _SunDisc ("Sun disc", Float) = 0.6
        _SunDiscPower ("Sun disc power", Float) = 3000
        _Exposure ("Exposure", Float) = 1
        _StarColor ("Star colour", Color) = (0.8, 0.85, 1, 1)
        _StarIntensity ("Star intensity (0 = no stars)", Float) = 0
        _StarDensity ("Star density", Range(0, 1)) = 0.06
        _StarScale ("Star cells per unit direction", Float) = 220
        _StarSize ("Star size (cell fraction)", Float) = 0.32
        _StarAxis ("Celestial pole (direction)", Vector) = (0, 0.86, 0.5, 0)
        _StarAngle ("Star rotation (radians)", Float) = 0
        _MoonDir ("Direction to moon", Vector) = (0.3, 0.6, -0.7, 0)
        _MoonColor ("Moon colour", Color) = (0, 0, 0, 1)
        _MoonGlow ("Moon glow", Float) = 0.07
        _MoonGlowPower ("Moon glow power", Float) = 6
        _MoonDisc ("Moon disc", Float) = 0.6
        _MoonDiscPower ("Moon disc power", Float) = 3000
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            half4 _HorizonColor, _BandColor, _ZenithColor, _BelowColor, _SunColor;
            float _BandHeight, _BandWidth, _CloudScale, _CloudContrast, _CloudSpeed;
            float _SunGlow, _SunGlowPower, _SunDisc, _SunDiscPower, _Exposure;
            half4 _StarColor;
            float _StarIntensity, _StarDensity, _StarScale, _StarSize;
            float4 _SunDir;
            float4 _StarAxis, _MoonDir;
            half4 _MoonColor;
            float _StarAngle, _MoonGlow, _MoonGlowPower, _MoonDisc, _MoonDiscPower;
            sampler2D _CloudTex;

            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float up = d.y;
                float e = saturate(up);
                float t = smoothstep(0.0, 1.0, pow(e, 0.55));
                float3 col = lerp(_HorizonColor.rgb, _ZenithColor.rgb, t);
                float bw = exp(-pow((e - _BandHeight) / _BandWidth, 2.0));
                col += (_BandColor.rgb - _HorizonColor.rgb) * bw;
                float b = smoothstep(0.0, 0.25, -up);
                col = lerp(col, _BelowColor.rgb, b);
                // clouds projected on a dome
                float k = 1.0 / (max(up, 0.0) + 0.22);
                float2 uv = d.xz * k * _CloudScale + float2(_Time.y * _CloudSpeed, _Time.y * _CloudSpeed * 0.37);
                float c = tex2Dlod(_CloudTex, float4(uv, 0, 0)).r;
                float fade = smoothstep(-0.02, 0.35, up);
                col *= 1.0 + (c - 0.5) * 2.0 * _CloudContrast * fade;
                // v2.4 stars: one candidate per cell of a direction grid (mirror of wk/post.py stars())
                if (_StarIntensity > 0)
                {
                    // the sky turns about the pole: rotate the view direction back by the hour's angle (Rodrigues)
                    // a material copied from the SkyIsland sky has these vectors zeroed: never normalize zero (NaN = black sky)
                    float3 ax = dot(_StarAxis.xyz, _StarAxis.xyz) > 1e-6 ? normalize(_StarAxis.xyz) : float3(0.0, 0.883, 0.469);
                    float ca = cos(-_StarAngle), sa = sin(-_StarAngle);
                    float3 ds = d * ca + cross(ax, d) * sa + ax * dot(ax, d) * (1.0 - ca);
                    float3 sp = ds * _StarScale;
                    float3 cell = floor(sp);
                    float3 h = frac(sin(float3(dot(cell, float3(127.1, 311.7, 74.7)), dot(cell, float3(269.5, 183.3, 246.1)),
                                               dot(cell, float3(113.5, 271.9, 124.6)))) * 43758.5453);
                    float dist = length(sp - (cell + 0.25 + 0.5 * h));
                    float sb = saturate(1.0 - dist / max(_StarSize, 1e-3)) * step(1.0 - _StarDensity, h.x) * (0.35 + 0.65 * h.z);
                    col += _StarColor.rgb * (sb * _StarIntensity * smoothstep(0.02, 0.25, up));
                }
                // sun
                float cs = saturate(dot(d, normalize(_SunDir.xyz)));
                col += _SunColor.rgb * (pow(cs, _SunGlowPower) * _SunGlow + pow(cs, _SunDiscPower) * _SunDisc);
                // moon
                float cm = dot(_MoonDir.xyz, _MoonDir.xyz) > 1e-6 ? saturate(dot(d, normalize(_MoonDir.xyz))) : 0.0;
                col += _MoonColor.rgb * (pow(cm, _MoonGlowPower) * _MoonGlow + pow(cm, _MoonDiscPower) * _MoonDisc);
                return half4(col * _Exposure, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
