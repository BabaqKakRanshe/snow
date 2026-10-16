Shader "SkyIsland/Smoke"
{
    // Particle puffs for smoke, steam, spray, dust and embers (Built-in and URP). Like SkyIsland/LightGlow it is a
    // plain unlit CG pass without a LightMode tag, which URP renders as SRPDefaultUnlit.
    // Colour = texture RGB x particle (vertex) colour x _Color x _Intensity; alpha = texture A (density) x vertex alpha x _Color.a.
    // Premultiplied blending (One, OneMinusSrcAlpha): _Additive = 0 -> alpha-blended puffs that fade into the fog colour,
    // _Additive = 1 -> additive sparks (embers) that fade to black in fog.
    // Fades out near the camera so large puffs never fill the screen.
    Properties
    {
        _MainTex ("Puff (RGB colour, A density)", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Float) = 1
        [ToggleUI] _Additive ("Additive (embers)", Float) = 0
        _NearFade ("Fade when closer than (m)", Float) = 4
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            half4 _Color;
            float _Intensity, _Additive, _NearFade;

            struct appdata
            {
                float4 vertex : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float3 wpos : TEXCOORD1;
                UNITY_FOG_COORDS(2)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.color = v.color * _Color;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                half4 tex = tex2D(_MainTex, i.uv);
                float dist = distance(_WorldSpaceCameraPos.xyz, i.wpos);
                float nearFade = saturate((dist - _NearFade * 0.5) / max(_NearFade, 1e-3));
                half a = saturate(tex.a * i.color.a) * nearFade;
                half3 col = tex.rgb * i.color.rgb * _Intensity;
                // puffs blend into the fog colour, additive sparks are faded to black
                UNITY_APPLY_FOG_COLOR(i.fogCoord, col, unity_FogColor * (1.0 - _Additive));
                return half4(col * a, a * (1.0 - _Additive));
            }
            ENDCG
        }
    }
    Fallback Off
}
