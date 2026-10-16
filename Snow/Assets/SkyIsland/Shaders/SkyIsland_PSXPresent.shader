Shader "SkyIsland/PSXPresent"
{
    // v2.4 PSX output (UI shader for SkyIslandPSX's full-screen RawImage, Built-in and URP).
    // Samples the low-resolution frame at texel centres (nearest-neighbour upscale), converts to display (sRGB) values,
    // reduces every channel to _Levels steps with a 4x4 ordered (Bayer) dither at the internal resolution, back to linear.
    // Mirror of wk/post.py psx_present(): q = floor(c * L + 0.5 + d), d = (bayer / 16 - 0.46875) * dither.
    Properties
    {
        [PerRendererData] _MainTex ("Frame", 2D) = "black" {}
        _Levels ("Steps per channel (31 = 15-bit)", Float) = 31
        _Dither ("Dither strength", Range(0, 1)) = 1
        _Res ("Internal resolution (w, h)", Vector) = (427, 240, 0, 0)
    }
    SubShader
    {
        Tags { "Queue" = "Overlay" "IgnoreProjector" = "True" "RenderType" = "Opaque" "PreviewType" = "Plane" "CanUseSpriteAtlas" = "False" }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Always
        Blend Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Levels, _Dither;
            float4 _Res;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            static const float BAYER[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

            fixed4 frag (v2f i) : SV_Target
            {
                float2 res = max(_Res.xy, float2(1, 1));
                float2 px = floor(saturate(i.uv) * res - 1e-4);               // internal pixel
                float3 c = tex2Dlod(_MainTex, float4((px + 0.5) / res, 0, 0)).rgb;
            #ifndef UNITY_COLORSPACE_GAMMA
                c = LinearToGammaSpace(c);                                     // quantise display values, like the preview
            #endif
                int ix = (int)fmod(px.x, 4.0); int iy = (int)fmod(res.y - 1.0 - px.y, 4.0);   // row 0 = top, as in the preview
                float d = (BAYER[iy * 4 + ix] / 16.0 - 0.46875) * _Dither;
                float L = max(_Levels, 1.0);
                c = clamp(floor(c * L + 0.5 + d), 0.0, L) / L;
            #ifndef UNITY_COLORSPACE_GAMMA
                c = GammaToLinearSpace(c);
            #endif
                return fixed4(c, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
