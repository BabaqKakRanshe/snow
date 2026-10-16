Shader "SkyIsland/LightGlow"
{
    // Soft camera-facing halo around a lamp in fog (Built-in and URP).
    // Put it on a Quad (Unity's built-in quad, -0.5..0.5): the object's pivot is the lamp position,
    // the object's scale is the halo diameter in metres. Additive, fogged like any surface.
    Properties
    {
        _Color ("Colour (linear, normalised)", Color) = (1, 0.62, 0.3, 1)
        _Intensity ("Intensity", Float) = 1
        _Softness ("Falloff exponent", Float) = 2.6
        _NearFade ("Fade when closer than (m)", Float) = 3
        _FogFloor ("Least brightness kept in fog (beacons stay visible)", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+10" "RenderType" = "Transparent" "IgnoreProjector" = "True" "DisableBatching" = "True" "PreviewType" = "Plane" }
        Blend One One
        ZWrite Off
        ZTest LEqual
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            half4 _Color;
            float _Intensity, _Softness, _NearFade, _FogFloor;

            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float fade : TEXCOORD1;
                UNITY_FOG_COORDS(2)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 centre = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
                float size = length(float3(unity_ObjectToWorld[0].x, unity_ObjectToWorld[1].x, unity_ObjectToWorld[2].x));
                float3 right = UNITY_MATRIX_V[0].xyz;
                float3 up = UNITY_MATRIX_V[1].xyz;
                float3 toCam = _WorldSpaceCameraPos.xyz - centre;
                float dist = length(toCam);
                // pull the quad towards the camera so it is not swallowed by the lamp housing / wall behind it
                float3 wpos = centre + (right * v.vertex.x + up * v.vertex.y) * size + toCam / max(dist, 1e-3) * min(size * 0.5, 1.5);
                o.pos = mul(UNITY_MATRIX_VP, float4(wpos, 1.0));
                o.uv = v.vertex.xy * 2.0;
                o.fade = saturate((dist - _NearFade * 0.5) / max(_NearFade, 1e-3));
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                float t = saturate(1.0 - dot(i.uv, i.uv));
                float a = pow(t, _Softness) * 0.55 + pow(t, _Softness * 6.0) * 0.45;   // wide soft halo + brighter core
                half3 col = _Color.rgb * (_Intensity * a * i.fade);
                half3 raw = col;
                UNITY_APPLY_FOG_COLOR(i.fogCoord, col, half4(0, 0, 0, 0));
                col = max(col, raw * _FogFloor);                                  // v2.4 fog floor (look.fog.glowFloor)
                return half4(col, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
