Shader "SkyIsland/InteriorWindow"
{
    // Fake room behind window glass (interior mapping with a 2D room atlas). Built-in and URP.
    // Mesh contract (one quad per pane): UV0.x = tile + local u, UV0.y = bucket + local v, local u/v in [0.002, 0.998];
    // bucket = lit (0/1) + 2 * curtain (0 none, 1 tulle, 2 tulle + drapes, 3 drapes drawn).
    // The room box is the window: x, y in [0,1], depth _Depth (in window widths). Atlas tiles are pinhole renders from a
    // camera _CamDist window widths in front of the opening, framing it exactly: img = 0.5 + (p.xy - 0.5) * c / (c - p.z).
    // Tangent frame and the window aspect come from screen-space derivatives, so no tangents / extra channels are needed.
    Properties
    {
        _Atlas ("Rooms atlas", 2D) = "black" {}
        _Curtains ("Curtains (2x2, RGBA)", 2D) = "black" {}
        _Cols ("Atlas columns", Float) = 4
        _Rows ("Atlas rows", Float) = 4
        _Depth ("Room depth (window widths)", Float) = 1
        _CamDist ("Atlas camera distance (window widths)", Float) = 1
        _LitIntensity ("Lit room emission", Float) = 1.6
        _DarkIntensity ("Unlit room brightness", Float) = 0.05
        _LitTint ("Lit tint", Color) = (1, 0.9, 0.78, 1)
        _DarkTint ("Unlit tint", Color) = (0.55, 0.62, 0.85, 1)
        _ReflTop ("Reflection: sky", Color) = (0.05, 0.06, 0.12, 1)
        _ReflBottom ("Reflection: ground", Color) = (0.015, 0.015, 0.02, 1)
        _Fresnel ("Glass F0", Float) = 0.04
        _ReflStrength ("Reflection strength", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _Atlas, _Curtains;
            float4 _Atlas_TexelSize;
            float _Cols, _Rows, _Depth, _CamDist, _LitIntensity, _DarkIntensity, _Fresnel, _ReflStrength;
            half4 _LitTint, _DarkTint, _ReflTop, _ReflBottom;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 wpos : TEXCOORD1;
                float3 wn : TEXCOORD2;
                UNITY_FOG_COORDS(3)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.pos = mul(UNITY_MATRIX_VP, float4(o.wpos, 1.0));
                o.wn = UnityObjectToWorldNormal(v.normal);
                o.uv = v.uv;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                const float EPS = 0.002;
                float2 cell = floor(i.uv);
                float2 lp = saturate((i.uv - cell - EPS) / (1.0 - 2.0 * EPS));
                // tangent frame + aspect from derivatives
                float3 dpx = ddx(i.wpos), dpy = ddy(i.wpos);
                float2 dux = ddx(i.uv), duy = ddy(i.uv);
                float det = dux.x * duy.y - duy.x * dux.y;
                det = abs(det) < 1e-12 ? 1e-12 : det;
                float3 dPdu = (dpx * duy.y - dpy * dux.y) / det;
                float3 dPdv = (dpy * dux.x - dpx * duy.x) / det;
                float wl = max(length(dPdu), 1e-6), hl = max(length(dPdv), 1e-6);
                float3 T = dPdu / wl, B = dPdv / hl;
                float3 N = normalize(i.wn);
                if (dot(cross(T, B), N) < 0.0) { T = -T; lp.x = 1.0 - lp.x; }
                float3 V = normalize(i.wpos - _WorldSpaceCameraPos.xyz);            // eye -> surface
                float3 d = float3(dot(V, T), dot(V, B) * wl / hl, min(dot(V, N), -1e-4));   // window-width units, z into the room
                float tx = d.x > 0.0 ? (1.0 - lp.x) / d.x : -lp.x / min(d.x, -1e-6);
                float ty = d.y > 0.0 ? (1.0 - lp.y) / d.y : -lp.y / min(d.y, -1e-6);
                float tz = -_Depth / d.z;
                float t = min(min(tx, ty), tz);
                float3 h = float3(lp, 0.0) + d * t;
                float s = _CamDist / (_CamDist - h.z);
                float2 img = saturate(0.5 + (h.xy - 0.5) * s);
                float tile = cell.x, bucket = cell.y;
                float cx = fmod(tile, _Cols), cy = floor(tile / _Cols);
                float2 tsz = float2(1.0 / _Cols, 1.0 / _Rows);
                float2 t0 = float2(cx, _Rows - 1.0 - cy) * tsz;                     // tile origin (row 0 = top of the image)
                float2 auv = clamp(t0 + img * tsz, t0 + _Atlas_TexelSize.xy, t0 + tsz - _Atlas_TexelSize.xy);
                half3 room = tex2Dgrad(_Atlas, auv, ddx(lp) * tsz, ddy(lp) * tsz).rgb;
                float lit = fmod(bucket, 2.0);
                float curtain = floor(bucket * 0.5 + 0.01);
                half3 col = room * (lit > 0.5 ? _LitTint.rgb * _LitIntensity : _DarkTint.rgb * _DarkIntensity);
                if (curtain > 0.5)
                {
                    float ci = curtain - 1.0;                                        // 0 tulle, 1 drapes (alt 3), 2 drawn
                    if (abs(ci - 1.0) < 0.1 && fmod(tile, 2.0) > 0.5) ci = 3.0;
                    float2 c0 = float2(fmod(ci, 2.0), 1.0 - floor(ci * 0.5)) * 0.5;
                    half4 cu = tex2Dgrad(_Curtains, c0 + lp * 0.5, ddx(lp) * 0.5, ddy(lp) * 0.5);
                    half roomL = dot(room, half3(0.3, 0.5, 0.2));
                    half3 cl = cu.rgb * (lit > 0.5 ? _LitTint.rgb * _LitIntensity * (0.35 + 0.6 * roomL) : _DarkTint.rgb * _DarkIntensity * 1.6);
                    col = lerp(col, cl, cu.a);
                }
                // glass reflection (sky / ground gradient)
                float3 R = reflect(V, N);
                float cv = saturate(dot(-V, N));
                float F = _Fresnel + (1.0 - _Fresnel) * pow(1.0 - cv, 5.0);
                half3 refl = lerp(_ReflBottom.rgb, _ReflTop.rgb, saturate(R.y * 0.5 + 0.5)) * _ReflStrength;
                col = col * (1.0 - F) + refl * F;
                UNITY_APPLY_FOG(i.fogCoord, col);
                return half4(col, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
