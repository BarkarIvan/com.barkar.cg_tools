#ifndef CUSTOM_SURFACE_INCLUDED
#define CUSTOM_SURFACE_INCLUDED

struct CustomLitData
{
    float3 positionWS;
    float3  V; //ViewDirWS
    float3  N; //NormalWS
    float3  B; //BinormalWS
    float3  T; //TangentWS
    //float2 ScreenUV;
};

struct CustomSurfaceData
{
    float3 albedo;
    float3 specular;
   // half3 normalTS;
  //  float2 uvs;
    half  metallic;
    half  roughness;
    half  occlusion;
    half  alpha;
};

#endif