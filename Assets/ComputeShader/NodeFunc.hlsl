﻿#ifndef NODE_FUNC
#define NODE_FUNC

#include "./DataStructDefine.hlsl"

//获取某个LOD级别的Node尺寸 相当于是 一个Node覆盖的地形范围 一个LOD0的Node是 LOD1的一个Patch
inline float GetNodeSizeInLOD(GlobalValue gvalue,int LOD)
{
    return gvalue.MAX_LOD_PATCH_SIZE * gvalue.PATCH_NUM_IN_NODE * (1 << LOD);
}

//获取某个LOD级别，Terrain在一个维度上NODE的数量。相当LOD0就有（总边长除以LOD0NodeSize ）个 Node （很好理解）
inline int GetNodeNumInLOD(GlobalValue gvalue, int LOD)
{
    return floor(gvalue.REAL_TERRAIN_SIZE / GetNodeSizeInLOD(gvalue, LOD) + 0.1f);
}

//获取某个LOD级别，在一个维度上PATCH的长度（尺寸)。
inline float GetPatchSizeInLOD(GlobalValue gvalue, int LOD)
{
    return gvalue.MAX_LOD_PATCH_SIZE * (1 << LOD);
}
//得到Node在的中心位置
inline float2 GetNodeCenterPos(GlobalValue gvalue, uint2 nodeXY, uint LOD)
{
    float nodeSize = GetNodeSizeInLOD(gvalue, LOD);
    uint nodeCount = GetNodeNumInLOD(gvalue, LOD);
    float2 nodePos = nodeSize * (nodeXY + 0.5 - nodeCount * 0.5); //这里要减去nodeCount是因为我们默认了 索引0,0在左下角 整个分割中间的标准位置是0 ,0， 就是说实际的中间点是在变换的 中间的0.0位置这个点是锁定的
    return nodePos;
    
}
//打包一个GlobalValue
inline GlobalValue GetGlobalValue(float4 valueList[10])
{
    GlobalValue gvalue;
    gvalue.cameraWorldPos = float3(valueList[0].x, valueList[0].y, valueList[0].z);
    gvalue.fov = valueList[0].w;
    gvalue.MIN_LOD = valueList[1].x;
    gvalue.MAX_LOD = valueList[1].y;
    gvalue.MAX_LOD_PATCH_SIZE = valueList[1].z;
    gvalue.PATCH_NUM_IN_NODE = valueList[1].w;
    gvalue.REAL_TERRAIN_SIZE = valueList[2].x;
    gvalue.LodJudgeFactor = valueList[2].y;
    gvalue.near = valueList[2].z;
    gvalue.far = valueList[2].w;
    return gvalue;
}
//得到相机的节点XY
inline int2 GetCamearaNodeXY(GlobalValue gvlaue,float3 cameraWPos, uint LOD, float3 terrainPos)
{
    float3 relativePos = cameraWPos - terrainPos;//计算出相机与环境的相对位置
    float nodeSize = GetNodeSizeInLOD(gvlaue, LOD);//拿到当前lod下的Size
    int nodex = floor(relativePos.x / nodeSize); 
    int nodey = floor(relativePos.z / nodeSize);
    return int2(nodex, nodey);
}
inline void GetFrustumPlane(float4 globalValueList[10], out float4 planes[6])
{
    planes[0] = globalValueList[4];
    planes[1] = globalValueList[5];
    planes[2] = globalValueList[6];
    planes[3] = globalValueList[7];
    planes[4] = globalValueList[8];
    planes[5] = globalValueList[9];
}


inline float3 CalPointUVD(GlobalValue gvalue, float4x4 VPMatrix, float3 pos)
{
    float4 clipSpace = mul(VPMatrix, float4(pos, 1));
    
    float3 ndc = clipSpace.xyz / clipSpace.w;
    
#if SHADER_API_GLES3
    float3 uvd = (ndc + 1) * 0.5;
#else
    float3 uvd;
    uvd.xy = (ndc.xy + 1) * 0.5;
    uvd.z = 1 - ndc.z;
    
#endif
#if _REVERSE_Z
    uvd.z = 1 - uvd.z;
#endif
    return uvd;
}

//把坐标从世界空间变换到NDC空间 并且得到minPos 和 maxPos
inline void CalBoundUVD(GlobalValue gvalue, float4x4 VPMatrix, float3 minPos, float3 maxPos, out float3 minPosUVD, out float3 maxPosUVD)
{
    float3 pos0 = float3(minPos.x, minPos.y, minPos.z);
    float3 pos1 = float3(minPos.x, minPos.y, maxPos.z);
    float3 pos2 = float3(minPos.x, maxPos.y, minPos.z);
    float3 pos3 = float3(maxPos.x, minPos.y, minPos.z);
    float3 pos4 = float3(maxPos.x, maxPos.y, minPos.z);
    float3 pos5 = float3(maxPos.x, minPos.y, maxPos.z);
    float3 pos6 = float3(minPos.x, maxPos.y, maxPos.z);
    float3 pos7 = float3(maxPos.x, maxPos.y, maxPos.z);
    
    float3 uvd0 = CalPointUVD(gvalue, VPMatrix, pos0);
    float3 uvd1 = CalPointUVD(gvalue, VPMatrix, pos1);
    float3 uvd2 = CalPointUVD(gvalue, VPMatrix, pos2);
    float3 uvd3 = CalPointUVD(gvalue, VPMatrix, pos3);
    float3 uvd4 = CalPointUVD(gvalue, VPMatrix, pos4);
    float3 uvd5 = CalPointUVD(gvalue, VPMatrix, pos5);
    float3 uvd6 = CalPointUVD(gvalue, VPMatrix, pos6);
    float3 uvd7 = CalPointUVD(gvalue, VPMatrix, pos7);
    
    minPosUVD = min(min(min(uvd0, uvd1), min(uvd2, uvd3)), min(min(uvd4, uvd5), min(uvd6, uvd7)));
    maxPosUVD = max(max(max(uvd0, uvd1), max(uvd2, uvd3)), max(max(uvd4, uvd5), max(uvd6, uvd7)));
}

bool CompareDepth(float HizMapDepth, float obj_uvd_depth)
{
    //如果Map中的深度小于物体的深度 那么物体没有被遮挡
#if _REVERSE_Z
    HizMapDepth = 1 - HizMapDepth;
#endif
    return HizMapDepth < obj_uvd_depth;
}
#endif