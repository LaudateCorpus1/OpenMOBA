﻿#include "helpers/lighting.hlsl"

struct PSInput {
   float4 positionObject : POSITION1;
   float4 positionWorld : POSITION2;
   float4 position : SV_Position;
   float4 normalWorld : NORMAL2;
   float4 normal : NORMAL3;
   float4 color : COLOR;
   float2 uv : TEXCOORD;
};

Texture2D diffuseMap;

PSInput VSMain(
   float3 position : POSITION, 
   float3 normal : NORMAL, 
   float4 color : COLOR, 
   float2 uv : TEXCOORD
) {
   PSInput result;

   result.positionObject = float4(position, 1);
   result.positionWorld = mul(world, float4(position, 1));
   result.position = mul(mul(projView, world), float4(position, 1));
   result.normalWorld = normalize(mul(world, float4(normal, 0)));
   result.normal = normalize(mul(mul(projView, world), float4(normal, 0)));
   result.color = color;
   result.uv = uv;

   return result;
}

float4 PSMain(PSInput input) : SV_TARGET {
   uint numStructs, structStride;
   SpotlightDescriptions.GetDimensions(numStructs, structStride);

   float4 diffuse = input.color * diffuseMap.Sample(DiffuseSampler, input.uv);
   //return float4((input.normalWorld + float3(1, 1, 1)) / 2, 1);

   float4 colorAccumulator = float4(0, 0, 0, diffuse.w);
   if (!shadowTestEnabled) {
      colorAccumulator = diffuse.x;
   } else {
      for (uint i = 0; i != numSpotlights; i++) {
         float3 lighting = computeSpotlightLighting(input.positionWorld, input.normalWorld, ShadowMaps, SpotlightDescriptions[i]);
         colorAccumulator += diffuse * float4(lighting, 0);
      }
   }

   return colorAccumulator;
}