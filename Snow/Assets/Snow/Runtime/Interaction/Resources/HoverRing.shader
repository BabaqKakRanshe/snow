// Hover outline, step 2 (HoverOutline.cs): drawn over the whole screen from the mask (_MainTex, point-filtered, the
// camera frame's size). A pixel outside a shape with the shape within _Radius mask texels gets the ring colour, so only
// the outer edge of the silhouette is drawn, the same width everywhere; inside may get a faint _Fill.
Shader "Snow/HoverRing"
{
    Properties
    {
        _MainTex ("Mask (R hover, G hint)", 2D) = "black" {}
        _HoverColor ("Hover colour", Color) = (1, 0.85, 0.3, 1)
        _HintColor ("Hint colour", Color) = (0.3, 0.9, 1, 0.9)
        _Texel ("Mask texel (1/w, 1/h)", Vector) = (0.002, 0.004, 0, 0)
        _Radius ("Ring width, mask texels (1-3)", Float) = 1
        _Fill ("Fill inside the hover shape", Range(0, 1)) = 0
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
            fixed4 _HoverColor, _HintColor;
            float4 _Texel;
            float _Radius, _Fill;

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
                float2 self = tex2Dlod(_MainTex, float4(i.uv, 0, 0)).rg;
                float2 near = self;
                int r = (int)clamp(_Radius, 1, 3);
                for (int y = -3; y <= 3; y++)
                    for (int x = -3; x <= 3; x++)
                    {
                        if (abs(x) > r || abs(y) > r) continue;
                        near = max(near, tex2Dlod(_MainTex, float4(i.uv + float2(x, y) * _Texel.xy, 0, 0)).rg);
                    }
                float2 ring = near - self;
                if (ring.r > 0.5) return _HoverColor;
                if (ring.g > 0.5) return _HintColor;
                if (self.r > 0.5 && _Fill > 0) return fixed4(_HoverColor.rgb, _Fill);
                return fixed4(0, 0, 0, 0);
            }
            ENDCG
        }
    }
}
