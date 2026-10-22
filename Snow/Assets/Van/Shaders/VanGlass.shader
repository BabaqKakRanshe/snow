// Van v1: window glass of the van (13 panes, one renderer each), URP only. VanRig drives it per pane through a
// MaterialPropertyBlock; the material holds the defaults and the masks.
//
// Masks (Textures, Point, linear), all on UV1 (side / rear panes: FOG, 0..1 per pane; the windscreen lob: a copy of
// its own 0..1 UVs). RGB = the colour of the layer (PSX palette), A = when the texel freezes / snows over / fogs up:
// a layer shows where A > 1 - amount, so the amount covers about that share of the pane.
//   _FrostMask  T_Van_Frost  ice crystals (windscreen; the rig gives the side panes their own amount if you want)
//   _SnowMask   T_Van_Snow   wet snow stuck on the glass (windscreen)
//   _FogMask    T_Van_Fog    condensation inside (every pane): from the bottom and the edges first, runs down
//   _WipeMap    T_Van_WipeMap  R / G = the angle (/ 100, 255 = never) of Wiper_L / Wiper_R at which the blade passes
//   _Handprints T_Van_Handprints  2 x 2 atlas of hand prints (A)
// Per pane (MaterialPropertyBlock): _Frost, _Snow, _FogAmt 0..1; _Wipe = (reach L deg, reach R deg, strength 0..1,
// 1 = this pane has wipers); _HP0.._HP3 = hand print (centre u, v, size in u, atlas cell 0..3), _HPS = their strengths,
// _PrintAspect = the pane's v / u uv-per-metre ratio (a print is square on the glass).
// A print clears the fog (and melts the frost) under the palm: you see through it.
// Lighting of the layers: ambient (SH) + main light + every additional light (Forward and Forward+), wrapped
// (a fogged pane glows when the cab light is on, seen from outside too). The clear glass: tint + sky reflection.
// SRP Batcher compatible material; panes with a property block render outside the batcher (13 renderers).
Shader "Van/Glass"
{
    Properties
    {
        _BaseColor ("Glass Tint (A = opacity)", Color) = (0.16, 0.2, 0.22, 0.42)
        _Reflect ("Sky Reflection", Range(0, 1)) = 0.35
        [NoScaleOffset] _FrostMask ("Frost Mask", 2D) = "black" {}
        [NoScaleOffset] _SnowMask ("Snow Mask", 2D) = "black" {}
        [NoScaleOffset] _FogMask ("Fog Mask", 2D) = "black" {}
        [NoScaleOffset] _WipeMap ("Wipe Map (R Wiper_L, G Wiper_R)", 2D) = "white" {}
        [NoScaleOffset] _Handprints ("Hand Prints (2 x 2, A)", 2D) = "black" {}
        _Frost ("Frost", Range(0, 1)) = 0
        _Snow ("Snow", Range(0, 1)) = 0
        _FogAmt ("Fog", Range(0, 1)) = 0
        _Edge ("Layer Edge", Range(0.005, 0.2)) = 0.04
        _FrostAlpha ("Frost Opacity", Range(0, 1)) = 0.92
        _SnowAlpha ("Snow Opacity", Range(0, 1)) = 1
        _FogAlpha ("Fog Opacity", Range(0, 1)) = 0.8
        _Wipe ("Wipe (reach L, reach R, strength, on)", Vector) = (0, 0, 0, 0)
        _HP0 ("Print 0 (u, v, size, cell)", Vector) = (0, 0, 0, 0)
        _HP1 ("Print 1", Vector) = (0, 0, 0, 0)
        _HP2 ("Print 2", Vector) = (0, 0, 0, 0)
        _HP3 ("Print 3", Vector) = (0, 0, 0, 0)
        _HPS ("Print Strengths", Vector) = (0, 0, 0, 0)
        _PrintAspect ("Print V / U Size (the pane's uv aspect)", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            // Forward+: _FORWARD_PLUS in Unity 6000.0, _CLUSTER_LIGHT_LOOP from 6000.1
            #pragma multi_compile _ _FORWARD_PLUS _CLUSTER_LIGHT_LOOP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_FrostMask);  SAMPLER(sampler_FrostMask);
            TEXTURE2D(_SnowMask);   SAMPLER(sampler_SnowMask);
            TEXTURE2D(_FogMask);    SAMPLER(sampler_FogMask);
            TEXTURE2D(_WipeMap);    SAMPLER(sampler_WipeMap);
            TEXTURE2D(_Handprints); SAMPLER(sampler_Handprints);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Reflect;
                half _Frost;
                half _Snow;
                half _FogAmt;
                half _Edge;
                half _FrostAlpha;
                half _SnowAlpha;
                half _FogAlpha;
                float4 _Wipe;
                float4 _HP0;
                float4 _HP1;
                float4 _HP2;
                float4 _HP3;
                float4 _HPS;
                float _PrintAspect;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv1 : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv1 : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv1 = v.uv1;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            // layer coverage: shown where the mask alpha is above 1 - amount, with a soft edge; nothing at amount 0
            half Cover(half maskA, half amount)
            {
                half thr = 1.0h - amount;
                return smoothstep(thr - _Edge, thr + _Edge, maskA) * step(0.001h, amount);
            }

            half Print(float2 uv, float4 hp, half strength)
            {
                if (strength <= 0.001h || hp.z <= 1e-5) return 0.0h;
                float2 l = (uv - hp.xy) / float2(hp.z, hp.z * _PrintAspect) + 0.5;   // 0..1 inside the print (square on the glass)
                if (any(l < 0.0) || any(l > 1.0)) return 0.0h;
                float cell = floor(hp.w + 0.5);
                float2 a = (l + float2(fmod(cell, 2.0), floor(cell / 2.0))) * 0.5;
                return SAMPLE_TEXTURE2D(_Handprints, sampler_Handprints, a).a * strength;
            }

            half3 LayerLight(float3 positionWS, half3 n, float4 positionCS)
            {
                Light mainLight = GetMainLight();
                half wrapM = abs(dot(n, mainLight.direction)) * 0.5h + 0.5h;          // scattering: lit from both sides
                half3 lit = SampleSH(n) + mainLight.color * (wrapM * 0.6h);
            #if defined(_ADDITIONAL_LIGHTS) || defined(_FORWARD_PLUS) || defined(_CLUSTER_LIGHT_LOOP)
                InputData inputData = (InputData)0;
                inputData.positionWS = positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(positionCS);
                half4 shadowMask = half4(1, 1, 1, 1);
                uint lightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(lightCount)
                    Light l = GetAdditionalLight(lightIndex, positionWS, shadowMask);
                    half wrap = abs(dot(n, l.direction)) * 0.5h + 0.5h;
                    lit += l.color * (l.distanceAttenuation * l.shadowAttenuation * wrap);
                LIGHT_LOOP_END
            #endif
                return lit;
            }

            half4 frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float2 uv = i.uv1;
                half3 n = normalize(i.normalWS) * IS_FRONT_VFACE(face, 1.0h, -1.0h);
                half3 viewDir = SafeNormalize(GetWorldSpaceViewDir(i.positionWS));

                // wipers: a texel the blade has passed (its angle <= the reach of that wiper) is clear of snow / frost
                half4 wp = SAMPLE_TEXTURE2D(_WipeMap, sampler_WipeMap, uv);
                half aL = wp.r * (255.0h / 254.0h) * 100.0h, aR = wp.g * (255.0h / 254.0h) * 100.0h;
                half wipedL = step(wp.r, 0.998h) * step(aL, (half)_Wipe.x);
                half wipedR = step(wp.g, 0.998h) * step(aR, (half)_Wipe.y);
                half wiped = saturate(max(wipedL, wipedR) * (half)_Wipe.z * (half)_Wipe.w);

                half prints = saturate(Print(uv, _HP0, (half)_HPS.x) + Print(uv, _HP1, (half)_HPS.y) +
                                       Print(uv, _HP2, (half)_HPS.z) + Print(uv, _HP3, (half)_HPS.w));

                half4 fr = SAMPLE_TEXTURE2D(_FrostMask, sampler_FrostMask, uv);
                half4 sn = SAMPLE_TEXTURE2D(_SnowMask, sampler_SnowMask, uv);
                half4 fg = SAMPLE_TEXTURE2D(_FogMask, sampler_FogMask, uv);
                half fogA = Cover(fg.a, _FogAmt) * _FogAlpha * (1.0h - prints);
                half frostA = Cover(fr.a, _Frost) * _FrostAlpha * (1.0h - wiped) * (1.0h - 0.8h * prints);
                half snowA = Cover(sn.a, _Snow) * _SnowAlpha * (1.0h - wiped);

                // clear glass: tint + a sky reflection that grows at grazing angles
                half fresnel = pow(1.0h - saturate(abs(dot(n, viewDir))), 4.0h);
                half3 sky = SampleSH(reflect(-viewDir, n));
                half baseA = saturate(_BaseColor.a + fresnel * _Reflect);
                half3 P = (_BaseColor.rgb * SampleSH(n) + sky * (_Reflect * (0.3h + fresnel))) * baseA;   // premultiplied
                half A = baseA;

                half3 lit = (fogA + frostA + snowA) > 0.001h ? LayerLight(i.positionWS, n, i.positionCS) : half3(0, 0, 0);
                P = fg.rgb * lit * fogA + P * (1.0h - fogA);       A = fogA + A * (1.0h - fogA);
                P = fr.rgb * lit * frostA + P * (1.0h - frostA);   A = frostA + A * (1.0h - frostA);
                P = sn.rgb * lit * snowA + P * (1.0h - snowA);     A = snowA + A * (1.0h - snowA);

                half3 col = P / max(A, 1e-3h);
                col = MixFog(col, i.fogFactor);
                return half4(col, A);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
