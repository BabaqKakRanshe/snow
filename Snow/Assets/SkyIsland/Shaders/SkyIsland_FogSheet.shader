Shader "SkyIsland/FogSheet"
{
    // Large transparent haze layers under the island (sea of fog). Built-in and URP.
    Properties
    {
        _Color ("Colour", Color) = (0.455, 0.392, 0.145, 1)
        _NoiseTex ("Noise", 2D) = "gray" {}
        _Scale ("Noise scale (1/m)", Float) = 0.0011
        _Speed ("Drift (m/s)", Vector) = (1.2, 0.5, 0, 0)
        _Density ("Coverage", Range(0, 1)) = 0.6
        _Softness ("Edge softness", Range(0.01, 1)) = 0.35
        _Opacity ("Opacity", Range(0, 1)) = 0.85
        _FadeNear ("Fade near (m)", Float) = 40
        _FadeFar ("Fade far (m)", Float) = 220
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            half4 _Color;
            sampler2D _NoiseTex;
            float _Scale, _Density, _Softness, _Opacity, _FadeNear, _FadeFar;
            float4 _Speed;

            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float3 wpos : TEXCOORD0; UNITY_FOG_COORDS(1) UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                float2 p = (i.wpos.xz + _Time.y * _Speed.xy) * _Scale;
                float n = tex2D(_NoiseTex, p).r * 0.65 + tex2D(_NoiseTex, p * 3.7 + 0.31).r * 0.35;
                float a = saturate((n - (1.0 - _Density)) / _Softness) * _Opacity;
                float dist = distance(_WorldSpaceCameraPos.xyz, i.wpos);
                a *= saturate((dist - _FadeNear) / max(1.0, _FadeFar - _FadeNear));
                half3 col = _Color.rgb;
                UNITY_APPLY_FOG(i.fogCoord, col);
                return half4(col, a);
            }
            ENDCG
        }
    }
    Fallback Off
}
