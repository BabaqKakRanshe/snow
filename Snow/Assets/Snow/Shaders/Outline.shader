// Hover outline (URP): the mesh drawn again as an inverted hull pushed out by a fixed number of pixels, so the ring
// shows around the silhouette and the object itself hides the inside. Vertices move away from the mesh centre
// (_Center, object space) instead of along normals, so hard-edged meshes (switches, boxes) get a closed ring.
Shader "Snow/Outline"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.85, 0.3, 1)
        _Width ("Width, px", Float) = 3
        _Center ("Mesh centre, object space", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+50" "RenderType" = "Transparent" }
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Width;
                float4 _Center;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 dirOS = v.positionOS.xyz - _Center.xyz;
                dirOS = dot(dirOS, dirOS) > 1e-10 ? normalize(dirOS) : v.normalOS;
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 dirWS = TransformObjectToWorldDir(dirOS);
                float4 c0 = TransformWorldToHClip(posWS);
                float4 c1 = TransformWorldToHClip(posWS + dirWS * 0.01);
                float2 d = c1.xy / c1.w - c0.xy / c0.w;
                d = dot(d, d) > 1e-12 ? normalize(d) : float2(0, 0);
                c0.xy += d * (_Width * 2.0 / _ScreenParams.xy) * c0.w;
                o.positionCS = c0;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                return _Color;
            }
            ENDHLSL
        }
    }
}
