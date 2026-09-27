// Вода: полупрозрачная гладь с отблеском солнца и отражением неба по краям.
Shader "BattleSim/Water"
{
    Properties
    {
        _BaseColor ("Color", Color) = (0.16, 0.44, 0.52, 0.8)
        _SkyColor ("Sky", Color) = (0.7, 0.78, 0.86, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-10" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _SkyColor;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half fog : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));
                // лёгкая рябь
                float2 p = i.positionWS.xz;
                float t = _Time.y;
                float3 n = normalize(float3(sin(p.x * 0.35 + t * 0.9) * 0.04 + sin(p.y * 0.21 - t * 0.7) * 0.03, 1, cos(p.y * 0.3 + t * 0.8) * 0.04));
                Light light = GetMainLight();
                float fres = pow(1.0 - saturate(dot(n, viewDir)), 4.0);
                float3 h = normalize(light.direction + viewDir);
                half spec = pow(saturate(dot(n, h)), 180.0) * 1.6;
                half3 col = lerp(_BaseColor.rgb * (0.35 + 0.65 * light.color * saturate(light.direction.y) + SampleSH(n) * 0.5), _SkyColor.rgb, fres * 0.6) + light.color * spec;
                col = MixFog(col, i.fog);
                return half4(col, saturate(_BaseColor.a + fres * 0.2));
            }
            ENDHLSL
        }
    }
}
