// Частицы боя: мягкий круглый клуб, всегда лицом к камере. Края растворяются, у самой камеры клуб
// тает (не заслоняет крупный план), вдали уходит в туман. Цвет и прозрачность — цвет копии (_InstColor),
// в w.x копии матрицы — «мягкость»: 0 — пыль (размытый край), 1 — искра (плотная точка).
Shader "BattleSim/Puff"
{
    Properties
    {
        _InstColor ("Instance color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        Pass
        {
            Name "Puff"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            UNITY_INSTANCING_BUFFER_START(PuffProps)
                UNITY_DEFINE_INSTANCED_PROP(float4, _InstColor)
            UNITY_INSTANCING_BUFFER_END(PuffProps)

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : TEXCOORD1;
                half fog : TEXCOORD2;
                half near : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 center = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
                float size = length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20));
                float3 right = UNITY_MATRIX_V[0].xyz, up = UNITY_MATRIX_V[1].xyz;
                float3 ws = center + (right * v.positionOS.x + up * v.positionOS.y) * size;
                o.positionCS = TransformWorldToHClip(ws);
                o.uv = v.uv;
                o.color = (half4)UNITY_ACCESS_INSTANCED_PROP(PuffProps, _InstColor);
                o.fog = ComputeFogFactor(o.positionCS.z);
                // у камеры — тает: клуб в полуметре от объектива не должен закрывать кадр
                float dist = length(_WorldSpaceCameraPos - center);
                o.near = saturate((dist - 2.0 - size) / 6.0);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float r = length(i.uv);
                // мягкий круг: плотная середина, размытый край
                half a = saturate(1.0 - r);
                a = a * a * (3.0 - 2.0 * a);
                half3 col = MixFog(i.color.rgb, i.fog);
                return half4(col, i.color.a * a * i.near);
            }
            ENDHLSL
        }
    }
}
