Shader "ShibuyaRush/XRay"
{
    // 建物・床・柱の向こうに隠れたプレイヤーを、シルエットで見せる(手前に物があって描かれていない部分だけを塗る)
    Properties { _Color ("Color", Color) = (1, 0.62, 0.15, 0.6) }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+20" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "XRay"
            ZTest Greater
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
            CBUFFER_END
            Varyings vert(Attributes v)
            {
                Varyings o; UNITY_SETUP_INSTANCE_ID(v); UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz); return o;
            }
            half4 frag(Varyings i) : SV_Target { return half4(_Color.rgb, _Color.a); }
            ENDHLSL
        }
    }
}
