// Utility Plane v11: visible light beam in fog / falling snow (landing light cone, rotating beacon fans), URP only.
// Mesh: a pyramid from the lamp along its +Z (made by UtilityPlaneRig, Beam_<key>, child of the light, scale 1), drawn
// with its back faces so it also works with the camera inside the beam. Per pixel:
//   1. clip the view ray against the pyramid analytically (4 side planes + far cap) and against the scene depth;
//   2. integrate single scattering along that segment with equi-angular sampling around the lamp
//      (t = tc + h tan(phi): samples are spread evenly in the angle seen from the lamp, which removes the 1/d^2 peak;
//      the scattering angle is phi + pi/2, Henyey-Greenstein phase);
//   3. the beam profile (Gaussian core + skirt, or a rotating fan: narrow in azimuth, an elevation window in the
//      plane's frame) and slowly drifting 3D noise (snow / mist density) per sample; interleaved-gradient jitter
//      hides the step pattern.
// Requires the camera Depth Texture. Additive, ZTest Always (depth handled here). SRP Batcher compatible.
// The rig sets _Color, _Intensity, _Density and, for fans, _UpOS / _HzOS (the plane's up and the beam heading in the
// light's frame; constant while the beacon turns about the plane's up axis).
Shader "UtilityPlane/Beam"
{
    Properties
    {
        [HDR] _Color ("Colour (linear, set by the rig)", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity (light units, set by the rig)", Float) = 0
        _Density ("Scattering per metre (set by the rig from atmosphere)", Float) = 0.03
        _Gain ("Beam Gain", Float) = 1
        _G ("Forward Scatter g", Range(0, 0.9)) = 0.55
        _Length ("Length (m)", Float) = 60
        _XMin ("Pyramid X Min (deg)", Float) = -20
        _XMax ("Pyramid X Max (deg)", Float) = 20
        _YMin ("Pyramid Y Min (deg)", Float) = -20
        _YMax ("Pyramid Y Max (deg)", Float) = 20
        _CoreWidth ("Core Sigma (deg, 0 = fan)", Float) = 4.5
        _Skirt ("Skirt", Float) = 0.04
        _FanWidth ("Fan Sigma (deg)", Float) = 7
        _ElLo ("Fan Lowest Elevation (deg)", Float) = -40
        _ElHi ("Fan Highest Elevation (deg)", Float) = 8
        _UpOS ("Plane Up In Light Space", Vector) = (0, 1, 0, 0)
        _HzOS ("Beam Heading In Light Space", Vector) = (0, 0, 1, 0)
        _Noise ("Density Variation", Range(0, 1)) = 0.6
        _NoiseScale ("Variation Size (m)", Float) = 2.5
        _Wind ("Drift (m/s, world)", Vector) = (0.6, -0.9, 0.3, 0)
        _Dither ("Dither", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Beam"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest Always
            Cull Front

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            #define BEAM_STEPS 12

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Intensity;
                float _Density;
                float _Gain;
                float _G;
                float _Length;
                float _XMin;
                float _XMax;
                float _YMin;
                float _YMax;
                float _CoreWidth;
                float _Skirt;
                float _FanWidth;
                float _ElLo;
                float _ElHi;
                float4 _UpOS;
                float4 _HzOS;
                float _Noise;
                float _NoiseScale;
                float4 _Wind;
                float _Dither;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            float Win(float x, float lo, float hi, float soft)
            {
                return smoothstep(lo - soft, lo + soft, x) * (1.0 - smoothstep(hi - soft, hi + soft, x));
            }

            // beam profile, d = unit direction from the lamp in light space
            float Profile(float3 d)
            {
                float z = max(d.z, 1e-4);
                float ax = degrees(atan2(d.x, z));
                float ay = degrees(atan2(d.y, z));
                // fades to exactly 0 at the pyramid faces (no seam where the ray is clipped)
                float edge = smoothstep(_XMin, _XMin + 8.0, ax) * (1.0 - smoothstep(_XMax - 8.0, _XMax, ax)) *
                             smoothstep(_YMin, _YMin + 8.0, ay) * (1.0 - smoothstep(_YMax - 8.0, _YMax, ay));
                if (_CoreWidth > 0.0)
                {
                    float th = degrees(acos(clamp(d.z, -1.0, 1.0)));
                    return (exp(-0.5 * th * th / (_CoreWidth * _CoreWidth)) + _Skirt) * edge;
                }
                float3 up = _UpOS.xyz;
                float3 hz = _HzOS.xyz;
                float3 hx = cross(up, hz);
                float el = degrees(asin(clamp(dot(d, up), -1.0, 1.0)));
                float az = degrees(atan2(dot(d, hx), dot(d, hz)));
                return exp(-0.5 * az * az / (_FanWidth * _FanWidth)) * Win(el, _ElLo, _ElHi, 3.0) * edge;
            }

            float Hash3(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            float Noise3(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = Hash3(i), n100 = Hash3(i + float3(1, 0, 0)), n010 = Hash3(i + float3(0, 1, 0)), n110 = Hash3(i + float3(1, 1, 0));
                float n001 = Hash3(i + float3(0, 0, 1)), n101 = Hash3(i + float3(1, 0, 1)), n011 = Hash3(i + float3(0, 1, 1)), n111 = Hash3(i + float3(1, 1, 1));
                return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
                            lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
            }

            float FogVisibility(float viewZ, float ownExtinction)
            {
                #if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                if (IsFogEnabled())
                    return ComputeFogIntensity(ComputeFogFactorZ0ToFar(max(viewZ - _ProjectionParams.y, 0.0)));
                #endif
                return ownExtinction;
            }

            // keeps the part of the ray inside the half space n.p + c >= 0
            void ClipPlane(float3 n, float c, float3 o, float3 d, inout float t0, inout float t1)
            {
                float nd = dot(n, d);
                float f0 = dot(n, o) + c;
                if (abs(nd) < 1e-6)
                {
                    if (f0 < 0.0) t1 = -1.0;
                    return;
                }
                float t = -f0 / nd;
                if (nd > 0.0) t0 = max(t0, t);
                else t1 = min(t1, t);
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                if (_Intensity <= 0.0) return half4(0, 0, 0, 0);
                float3 ro = GetCameraPositionWS();
                float3 rd = normalize(i.positionWS - ro);

                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                float eyeDepth = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float3 camFwd = -UNITY_MATRIX_V[2].xyz;
                float cosFwd = max(dot(rd, camFwd), 1e-4);
                float T = eyeDepth / cosFwd;

                // light space (the beam object is the lamp: origin = lens, +Z = axis). objScale = object units per world metre
                // (1 unless the plane is scaled): t runs in object units, world distances are t / objScale.
                float3 o = TransformWorldToObject(ro);
                float3 dObj = mul((float3x3)GetWorldToObjectMatrix(), rd);
                float objScale = max(length(dObj), 1e-6);
                float3 d = dObj / objScale;
                float t0 = 0.0, t1 = T * objScale;
                ClipPlane(float3(1, 0, -tan(radians(_XMin))), 0.0, o, d, t0, t1);
                ClipPlane(float3(-1, 0, tan(radians(_XMax))), 0.0, o, d, t0, t1);
                ClipPlane(float3(0, 1, -tan(radians(_YMin))), 0.0, o, d, t0, t1);
                ClipPlane(float3(0, -1, tan(radians(_YMax))), 0.0, o, d, t0, t1);
                ClipPlane(float3(0, 0, -1), _Length, o, d, t0, t1);
                if (t1 <= t0) return half4(0, 0, 0, 0);

                // equi-angular samples around the lamp
                float tc = -dot(o, d);
                float hh = length(o + d * tc);
                float hs = max(hh, 0.25);                                   // the lamp itself is drawn by the glow
                float phi0 = atan((t0 - tc) / hs);
                float phi1 = atan((t1 - tc) / hs);
                float jitter = frac(52.9829189 * frac(dot(i.positionCS.xy, float2(0.06711056, 0.00583715))));
                float g = _G;
                float hgNorm = (1.0 - g * g) / (4.0 * PI);
                float3 drift = _Wind.xyz * _Time.y;
                float invScale = 1.0 / max(_NoiseScale, 0.1);
                float sum = 0.0;
                [loop]
                for (int k = 0; k < BEAM_STEPS; k++)
                {
                    float phi = lerp(phi0, phi1, (k + jitter) / BEAM_STEPS);
                    float t = tc + hs * tan(phi);
                    float3 p = o + d * t;
                    float r = max(length(p), 1e-3);
                    float prof = Profile(p / r) * (1.0 - smoothstep(0.7 * _Length, _Length, r));
                    float cosTh = -sin(phi);                                    // scattering angle = phi + pi/2
                    float hg = hgNorm * pow(max(1.0 + g * g - 2.0 * g * cosTh, 1e-4), -1.5);
                    float tw = t / objScale;
                    float3 pw = ro + rd * tw;
                    float n = Noise3((pw - drift) * invScale) * 0.65 + Noise3((pw - drift * 1.7) * (invScale * 2.7)) * 0.35;
                    float dens = max(1.0 + _Noise * (n - 0.5) * 2.0, 0.0);
                    float vis = FogVisibility(tw * cosFwd, exp(-_Density * (t + r) / objScale));
                    sum += prof * hg * dens * vis;
                }
                float L = _Density * _Intensity * objScale / hs * (phi1 - phi0) * sum / BEAM_STEPS * _Gain;
                float3 col = _Color.rgb * L;
                col *= 1.0 + (jitter - 0.5) * (_Dither * 0.06);
                return half4(col, 0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
