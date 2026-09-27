// Кольца под солдатами и «призрак» отряда при расстановке: полупрозрачный цвет копии, поверх земли.
Shader "BattleSim/Unlit"
{
    Properties
    {
        _BaseColor ("Color", Color) = (1, 1, 1, 0.6)
        _InstColor ("Instance color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Offset -2, -2

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(UnlitProps)
                UNITY_DEFINE_INSTANCED_PROP(float4, _InstColor)
            UNITY_INSTANCING_BUFFER_END(UnlitProps)

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = _BaseColor * UNITY_ACCESS_INSTANCED_PROP(UnlitProps, _InstColor);
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return i.color; }
            ENDHLSL
        }
    }
}
