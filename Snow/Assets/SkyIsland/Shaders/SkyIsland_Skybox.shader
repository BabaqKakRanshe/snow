Shader "SkyIsland/Skybox"
{
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
            float4 _SunDir;
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
                // sun
                float cs = saturate(dot(d, normalize(_SunDir.xyz)));
                col += _SunColor.rgb * (pow(cs, _SunGlowPower) * _SunGlow + pow(cs, _SunDiscPower) * _SunDisc);
                return half4(col * _Exposure, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
