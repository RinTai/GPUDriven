#ifndef DATA_STRUCT_DEFINE
#define DATA_STRUCT_DEFINE

struct GlobalValue
{
    float3 cameraWorldPos;
    float fov;
    
    int MIN_LOD;
    int MAX_LOD;
    
    float MAX_LOD_PATCH_SIZE; //在最高 LOD 下（通常分辨率最高状态）的每个 Patch（地形块）的尺寸。
    float PATCH_NUM_IN_NODE; //表示一个节点内包含多少个 Patch 一个维度
    float REAL_TERRAIN_SIZE; //整个地形的实际尺寸
    float LodJudgeFactor; //一个调整参数
};
#endif