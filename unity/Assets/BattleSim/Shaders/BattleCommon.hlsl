#ifndef BATTLE_COMMON_INCLUDED
#define BATTLE_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

TEXTURE2D(_BaseMap);    SAMPLER(sampler_BaseMap);
TEXTURE2D(_DetailMap);  SAMPLER(sampler_DetailMap);
TEXTURE2D(_BakeTex);

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4 _BaseColor;
    half _Detail;
    half _Spec;
    half _Cull;
CBUFFER_END

UNITY_INSTANCING_BUFFER_START(BattleProps)
    UNITY_DEFINE_INSTANCED_PROP(float4, _InstColor)
    UNITY_DEFINE_INSTANCED_PROP(float4, _Anim)
UNITY_INSTANCING_BUFFER_END(BattleProps)

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float2 uv         : TEXCOORD0;
    float4 color      : COLOR;
#ifdef _BAKED_SKIN
    float4 bones      : TEXCOORD2;
    float4 weights    : TEXCOORD3;
#endif
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

// Запечённая анимация: строка текстуры = кадр, 3 текселя = строки матрицы 3×4 кости
float3x4 BoneMat(float row, float b)
{
    int2 p = int2((int)b * 3, (int)row);
    float4 r0 = LOAD_TEXTURE2D_LOD(_BakeTex, p, 0);
    float4 r1 = LOAD_TEXTURE2D_LOD(_BakeTex, p + int2(1, 0), 0);
    float4 r2 = LOAD_TEXTURE2D_LOD(_BakeTex, p + int2(2, 0), 0);
    return float3x4(r0, r1, r2);
}

float3x4 SkinMat(float row, float4 i, float4 w)
{
    return BoneMat(row, i.x) * w.x + BoneMat(row, i.y) * w.y + BoneMat(row, i.z) * w.z + BoneMat(row, i.w) * w.w;
}

void ApplyBattleSkin(inout float3 pos, inout float3 nrm, Attributes v)
{
#ifdef _BAKED_SKIN
    float4 anim = UNITY_ACCESS_INSTANCED_PROP(BattleProps, _Anim);
    float3x4 m = SkinMat(anim.x, v.bones, v.weights);
    if (anim.z > 0.001)
        m = m * (1.0 - anim.z) + SkinMat(anim.y, v.bones, v.weights) * anim.z;
    pos = mul(m, float4(pos, 1.0));
    nrm = normalize(mul((float3x3)m, nrm));
#endif
}

#endif
