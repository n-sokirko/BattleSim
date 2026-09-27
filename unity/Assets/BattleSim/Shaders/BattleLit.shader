// Основной шейдер игры (URP): текстура × цвет × цвет вершин × цвет копии,
// солнце с тенями, свет неба, туман. С ключом _BAKED_SKIN — солдаты и кони
// с запечённой анимацией (скиннинг на видеокарте, тысячи копий одним вызовом).
Shader "BattleSim/Lit"
{
    Properties
    {
        _BaseMap ("Texture", 2D) = "white" {}
        _BaseColor ("Color", Color) = (1, 1, 1, 1)
        _DetailMap ("Detail", 2D) = "gray" {}
        _Detail ("Detail strength", Float) = 0
        _Spec ("Specular", Float) = 0.06
        _InstColor ("Instance color", Color) = (1, 1, 1, 1)
        _Anim ("Anim rows", Vector) = (0, 0, 0, 0)
        _BakeTex ("Baked bones", 2D) = "black" {}
        _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ _BAKED_SKIN
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            #include "BattleCommon.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                half4 color       : TEXCOORD3;
                half fog          : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                float3 pos = v.positionOS.xyz, nrm = v.normalOS;
                ApplyBattleSkin(pos, nrm, v);
                o.positionWS = TransformObjectToWorld(pos);
                o.normalWS = TransformObjectToWorldNormal(nrm);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.color = v.color * UNITY_ACCESS_INSTANCED_PROP(BattleProps, _InstColor);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                half3 albedo = tex.rgb * _BaseColor.rgb * i.color.rgb;
                if (_Detail > 0.5)
                {
                    half dA = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, i.uv * 97.0).r;
                    half dB = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, i.uv * 23.0).g;
                    albedo *= 0.74 + dA * 0.38 + dB * 0.16;
                }
                float3 n = normalize(i.normalWS);
                if (!front) n = -n;
                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light light = GetMainLight(shadowCoord);
                half ndl = saturate(dot(n, light.direction));
                half3 direct = light.color * (ndl * light.shadowAttenuation * light.distanceAttenuation);
                half3 ambient = SampleSH(n);
                float3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));
                float3 h = normalize(light.direction + viewDir);
                half spec = pow(saturate(dot(n, h)), 24.0) * _Spec * light.shadowAttenuation;
                half3 col = albedo * (direct + ambient) + light.color * spec;
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ _BAKED_SKIN
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "BattleCommon.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 pos = v.positionOS.xyz, nrm = v.normalOS;
                ApplyBattleSkin(pos, nrm, v);
                float3 positionWS = TransformObjectToWorld(pos);
                float3 normalWS = TransformObjectToWorldNormal(nrm);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDir = normalize(_LightPosition - positionWS);
            #else
                float3 lightDir = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDir));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                o.positionCS = positionCS;
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ _BAKED_SKIN

            #include "BattleCommon.hlsl"

            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 pos = v.positionOS.xyz, nrm = v.normalOS;
                ApplyBattleSkin(pos, nrm, v);
                o.positionCS = TransformObjectToHClip(pos);
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
