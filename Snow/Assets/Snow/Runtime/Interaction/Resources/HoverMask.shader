// Hover outline, step 1 (HoverOutline.cs): the highlighted meshes drawn flat into a small mask, R = hover, G = hint.
// No depth: the mask is the whole silhouette, the ring is drawn around its outer edge only.
Shader "Snow/HoverMask"
{
    Properties
    {
        _MaskColor ("Mask channel", Color) = (1, 0, 0, 1)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        Pass
        {
            Name "Mask"
            Cull Off
            ZWrite Off
            ZTest Always
            Blend One One

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _MaskColor;
            CBUFFER_END

            float4 Vert(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS.xyz);
            }

            half4 Frag() : SV_Target
            {
                return _MaskColor;
            }
            ENDHLSL
        }
    }
}
