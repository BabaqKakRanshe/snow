// Frost on the edges of the view (ScreenFrost.cs). _MainTex: R = crystal brightness, G = reach (0 at the screen edge,
// 1 deep inside). A pixel shows its crystal once _Amount passes its reach, so the frost grows in from the edges.
Shader "Snow/ScreenFrost"
{
    Properties
    {
        _MainTex ("Frost (R crystal, G reach)", 2D) = "black" {}
        _Amount ("Amount 0..1", Range(0, 1)) = 0
        _Edge ("Growth edge softness", Range(0.01, 0.3)) = 0.08
        _Tint ("Frost colour", Color) = (0.85, 0.92, 1, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.85
    }
    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off ZTest Always Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Amount, _Edge, _Opacity;
            fixed4 _Tint;

            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata_img v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 t = tex2D(_MainTex, i.uv);
                float shown = saturate((_Amount - t.g) / _Edge) * t.r;
                return fixed4(_Tint.rgb, shown * _Opacity);
            }
            ENDCG
        }
    }
}
