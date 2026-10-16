// Utility Plane v11: glow of a lamp in the air around it (fog, snow, haze), URP only. One camera-facing quad per lamp,
// created by UtilityPlaneRig as a child of the light (Glow_<key>); the quad's transform is the lamp: origin = lens
// centre, local axes = the emission pattern frame (+Z = middle of the sector / beam, +Y = up).
//
// Per pixel, analytically (no ray marching):
//   lens core  - the lamp itself: a disc of _CoreSize metres, never smaller than _MinPixels so a distant lamp stays a
//                point; seen only from inside its pattern (sector, beam, rotating fan) and only where no geometry is
//                in front of it (camera depth texture).
//   halo       - single scattering of the lamp's light by the air along the view ray, clipped at the scene depth:
//                sigma * I / h * integral of the phase function over the scattering angles the ray sweeps. With the
//                equi-angular substitution t = tc + h tan(phi) the scattering angle is phi + pi/2, so a
//                Henyey-Greenstein forward lobe (small-angle form) and an isotropic part both integrate in closed form.
//                The forward lobe is the aureole you see around lights in fog: its angular size does not depend on
//                distance. _Iso = 0 for lamps whose side scatter is drawn by UtilityPlane/Beam.
// Requires the camera Depth Texture (URP asset: Depth Texture on). Additive, ZTest Always (depth handled here).
// SRP Batcher compatible: all material properties in UnityPerMaterial. The rig sets _Color, _Intensity, _Density.
Shader "UtilityPlane/Airlight"
{
    Properties
    {
        [HDR] _Color ("Colour (linear, set by the rig)", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity (light units, set by the rig)", Float) = 0
        _Density ("Scattering per metre (set by the rig from atmosphere)", Float) = 0.03
        _CoreSize ("Lens Radius (m)", Float) = 0.035
        _CoreGain ("Lens Brightness", Float) = 2
        _MinPixels ("Lens Min Size (px)", Float) = 1.5
        _Halo ("Halo Gain", Float) = 1
        _HaloClip ("Halo Soft Clip (no flat discs without tonemapping)", Range(0, 2)) = 0.6
        _Iso ("Side Scatter Share", Range(0, 1)) = 1
        _G ("Forward Scatter g", Range(0, 0.95)) = 0.85
        _Forward ("Forward Lobe Share", Range(0, 1)) = 0.6
        _Sector ("Sector Half Angle (deg, 180 = all round)", Float) = 180
        _SectorSoft ("Sector Edge (deg)", Float) = 3
        _ElevPower ("Vertical Falloff (cos power, 0 = none)", Float) = 0
        _BeamWidth ("Beam Sigma (deg, 0 = none)", Float) = 0
        _FanWidth ("Rotating Fan Sigma (deg, 0 = none)", Float) = 0
        _ElLo ("Fan Lowest Elevation (deg)", Float) = -90
        _ElHi ("Fan Highest Elevation (deg)", Float) = 90
        _Floor ("Lens Glow Outside The Pattern", Range(0, 1)) = 0.05
        _RadiusScale ("Halo Window (x camera distance)", Float) = 0.5
        _RadiusMin ("Halo Window Min (m)", Float) = 2
        _RadiusMax ("Halo Window Max (m)", Float) = 60
        _Dither ("Dither", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Pass
        {
            Name "Airlight"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Intensity;
                float _Density;
                float _CoreSize;
                float _CoreGain;
                float _MinPixels;
                float _Halo;
                float _HaloClip;
                float _Iso;
                float _G;
                float _Forward;
                float _Sector;
                float _SectorSoft;
                float _ElevPower;
                float _BeamWidth;
                float _FanWidth;
                float _ElLo;
                float _ElHi;
                float _Floor;
                float _RadiusScale;
                float _RadiusMin;
                float _RadiusMax;
                float _Dither;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;          // quad corners (+-1, +-1, 0)
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;         // point on the quad: the view ray passes through it
                float4 center : TEXCOORD1;             // lamp centre (world), halo window radius
                float lampVis : TEXCOORD2;             // 0..1: is the lamp itself in view (not behind geometry)
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float WindowRadius(float dist)
            {
                return clamp(dist * _RadiusScale, _RadiusMin, _RadiusMax);
            }

            static const float2 kVisTaps[5] = { float2(0, 0), float2(1.5, 0), float2(-1.5, 0), float2(0, 1.5), float2(0, -1.5) };

            // How much of the lamp the camera sees (5 depth taps around its screen position). Forward scattering - the
            // aureole - is light travelling from the lamp to the eye: when wing, hull or a wall hides the lamp, the air on
            // that line is in their shadow and the aureole goes. Lamp off screen or behind the camera: the taps go to the
            // nearest screen point in its direction, 3 px inside the edge. From the cabin that point is the wall, so a lamp
            // just outside the frame does not glow through it, and nothing pops when the lamp crosses the edge.
            // Compared as distances along each tap's ray (k: metres per metre of eye depth).
            float LampVisibility(float3 c, float tol)
            {
                float4 cs = TransformWorldToHClip(c);
                float2 d = float2(cs.x, cs.y * _ProjectionParams.x);
                float2 ndc = cs.w > 1e-4 ? d / cs.w : d * 1e4;
                float2 px = _ScaledScreenParams.zw - 1.0;                       // one pixel in UV
                float2 lim = 1.0 - 6.0 * px;                                    // 3 px inside the edge, in NDC
                float2 a = max(abs(ndc), 1e-6);
                ndc *= min(1.0, min(lim.x / a.x, lim.y / a.y));
                float2 uv = ndc * 0.5 + 0.5;
                float2 invP = 1.0 / max(float2(abs(UNITY_MATRIX_P[0][0]), abs(UNITY_MATRIX_P[1][1])), 1e-6);
                float lampDist = distance(c, GetCameraPositionWS());
                float v = 0.0;
                [unroll]
                for (int i = 0; i < 5; i++)
                {
                    float2 suv = uv + kVisTaps[i] * px;
                    float k = length(float3((suv * 2.0 - 1.0) * invP, 1.0));
                    float2 tuv = ClampAndScaleUVForBilinear(UnityStereoTransformScreenSpaceTex(suv), _CameraDepthTexture_TexelSize.xy);
                    float raw = SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, sampler_PointClamp, tuv, 0).r;
                    v += step(lampDist - tol * k, LinearEyeDepth(raw, _ZBufferParams) * k);
                }
                return v * 0.2;
            }

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 c = TransformObjectToWorld(float3(0, 0, 0));
                float3 toCam = GetCameraPositionWS() - c;
                float dist = max(length(toCam), 1e-4);
                float R = WindowRadius(dist);
                float2 k = v.positionOS.xy;
                float3 posWS;
                if (dist < R * 1.05 + _ProjectionParams.y * 4.0)
                {
                    // camera inside the halo: cover the screen with a quad just beyond the near plane
                    float4 cs = float4(k.x, k.y, UNITY_NEAR_CLIP_VALUE * 0.999, 1.0);
                    #if !UNITY_REVERSED_Z
                    cs.z = -0.999;
                    #endif
                    float4 ws = mul(UNITY_MATRIX_I_VP, cs);
                    posWS = ws.xyz / ws.w;
                    o.positionCS = cs;
                }
                else
                {
                    // billboard through the lamp, sized to the tangent cone of the halo window sphere
                    float3 f = -toCam / dist;
                    float3 up0 = abs(f.y) > 0.99 ? float3(1, 0, 0) : float3(0, 1, 0);
                    float3 rx = normalize(cross(up0, f));
                    float3 ry = cross(f, rx);
                    float s = R * dist * rsqrt(max(dist * dist - R * R, 1e-4));
                    posWS = c + (rx * k.x + ry * k.y) * s;
                    o.positionCS = TransformWorldToHClip(posWS);
                }
                o.positionWS = posWS;
                o.center = float4(c, R);
                o.lampVis = LampVisibility(c, 2.0 * _CoreSize + 0.05);
                return o;
            }

            // emission pattern in the quad's local frame (d: unit direction from the lamp)
            float Pattern(float3 d)
            {
                float az = degrees(atan2(d.x, d.z));
                float el = degrees(asin(clamp(d.y, -1.0, 1.0)));
                float p = 1.0 - smoothstep(_Sector - _SectorSoft, _Sector + _SectorSoft, abs(az));
                if (_ElevPower > 0.0) p *= pow(max(cos(radians(el)), 1e-4), _ElevPower);
                if (_BeamWidth > 0.0)
                {
                    float th = degrees(acos(clamp(d.z, -1.0, 1.0)));
                    p *= exp(-0.5 * th * th / (_BeamWidth * _BeamWidth));
                }
                if (_FanWidth > 0.0)
                {
                    p *= exp(-0.5 * az * az / (_FanWidth * _FanWidth));
                    p *= smoothstep(_ElLo - 3.0, _ElLo + 3.0, el) * (1.0 - smoothstep(_ElHi - 3.0, _ElHi + 3.0, el));
                }
                return max(p, _Floor);
            }

            // integral of the small-angle Henyey-Greenstein phase function over the scattering angle, 0..theta
            float HGInt(float theta, float g)
            {
                float a = 1.0 - g;
                return (1.0 - g * g) / (4.0 * PI) * theta / (a * a * sqrt(a * a + g * theta * theta));
            }

            float FogVisibility(float viewZ, float ownExtinction)
            {
                #if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                if (IsFogEnabled())
                    return ComputeFogIntensity(ComputeFogFactorZ0ToFar(max(viewZ - _ProjectionParams.y, 0.0)));
                #endif
                return ownExtinction;
            }

            float Dither(float2 pixel)                                   // interleaved gradient noise, 0..1
            {
                return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                if (_Intensity <= 0.0) return half4(0, 0, 0, 0);
                float3 ro = GetCameraPositionWS();
                float3 rd = normalize(i.positionWS - ro);
                float3 c = i.center.xyz;
                float R = i.center.w;
                float3 toL = c - ro;
                float dist = max(length(toL), 1e-4);
                float tc = dot(toL, rd);
                float h2 = max(dist * dist - tc * tc, 0.0);
                float h = sqrt(h2);
                float hs = sqrt(h2 + _CoreSize * _CoreSize);

                // scene depth along this ray
                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                float eyeDepth = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float3 camFwd = -UNITY_MATRIX_V[2].xyz;
                float T = eyeDepth / max(dot(rd, camFwd), 1e-4);

                float3 dCam = TransformWorldToObjectDir(-toL);                 // lamp -> camera, pattern frame
                float pCam = Pattern(dCam);

                // lens core
                float pixAng = 2.0 / (abs(UNITY_MATRIX_P[1][1]) * _ScaledScreenParams.y);
                float lensAng = _CoreSize / dist;
                float coreAng = max(lensAng, _MinPixels * pixAng);
                float ang = atan2(h, max(tc, 1e-4));
                float core = 1.0 - smoothstep(coreAng * 0.5, coreAng, ang);
                core *= max(saturate(lensAng * lensAng / (coreAng * coreAng)), 0.2);
                core *= step(tc - 2.0 * _CoreSize, T) * step(0.0, tc);
                float coreRad = _CoreGain * pCam * core;

                // halo: scattering angles swept by the visible part of the ray
                float t1 = min(T, tc + 4.0 * R);
                float th0 = atan(-tc / hs) + HALF_PI;
                float th1 = max(atan((t1 - tc) / hs) + HALF_PI, th0);
                float fwdLobe = (HGInt(th1, _G) - HGInt(th0, _G)) * i.lampVis;
                float isoPart = (th1 - th0) * (1.0 / (4.0 * PI));
                float3 dSide = h > 1e-3 ? TransformWorldToObjectDir(ro + rd * tc - c) : dCam;
                float pSide = Pattern(dSide);
                float win = 1.0 - smoothstep(0.35 * R, R, h);
                float halo = _Density / hs * (_Forward * fwdLobe * pCam + (1.0 - _Forward) * _Iso * isoPart * pSide) * _Halo * win;

                float vis = FogVisibility(dot(toL, camFwd), exp(-_Density * dist));
                float haloRad = _Intensity * halo * vis;
                haloRad = haloRad / (1.0 + _HaloClip * haloRad);              // soft shoulder: a glow, not a flat disc
                float3 col = _Color.rgb * (_Intensity * coreRad * vis + haloRad);
                col *= 1.0 + (Dither(i.positionCS.xy) - 0.5) * (_Dither * 0.06);   // breaks banding in R11G11B10
                return half4(col, 0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
