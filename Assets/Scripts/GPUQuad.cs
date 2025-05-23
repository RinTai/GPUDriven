using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine.Profiling;
using UnityEngine.Windows;
using System.IO;
using Mono.Cecil;
using UnityEditor;


public class GPUQuad : MonoBehaviour
{
    CommandBuffer cmd;

    public ComputeShader CS_Culling;

    int time = 0;
    ComputeBuffer DispatchArgsBuffer;
    PingPongBuffer PPBuffer;

    ComputeBuffer FinalBuffer;
    RenderTexture CullTexture;
    RenderTexture HiZTexture;

    uint[] tempUse = new uint[65536];
    Vector4[] globalValueList = new Vector4[10]; //用于存储所有常量的，包括摄像机这些 

    static string
        GlobalValueName = "globalValueList",
        CosumeName = "_ConsumeList",
        AppendName = "_AppendList",
        FinalName = "_FinalList",
        TempName = "_TempList",
        CullingResultName = "_CullingResult",
        HiZSrcName = "HiZSrcDepthMip",
        HiZDestName = "HiZDestDepthMip",
        HiZWidth = "HiZWidth",
        HiZHeight = "HiZHeight",
        CameraDepthSize = "_CameraDepthSize",
        CameraDepthTex = "_CameraDepthTexture";


    private void Awake()
    {
        cmd = new CommandBuffer();
        cmd.name = "一个CMD";
        PPBuffer = new PingPongBuffer(65536, 4);
        FinalBuffer = new ComputeBuffer(65536, 4, ComputeBufferType.Append);
        DispatchArgsBuffer = new ComputeBuffer(3, 4, ComputeBufferType.IndirectArguments);


        HiZTexture = new RenderTexture(Screen.width, Screen.height, 0);
        HiZTexture.format = RenderTextureFormat.RFloat;
        HiZTexture.enableRandomWrite = true;
        HiZTexture.useMipMap = true;
        HiZTexture.Create();


        uint[] dispatchArgs = new uint[] { 1, 1, 1 };
        DispatchArgsBuffer.SetData(dispatchArgs);

        CullTexture = new RenderTexture(65536, 1, 1);
        CullTexture.enableRandomWrite = true;
        CullTexture.format = RenderTextureFormat.RFloat;
        CullTexture.Create();

        InitialGlobalVlaue();

    }
    private void Update()
    {
        HizCreate();
        LODCreate();
        if (time == 0)
        {
         
            time += 1;
        }
        if (time == 1)
        {
            DrawMeshIndirectGraphics();
            time++;
        }
    }




    void InitialGlobalVlaue()
    {
        int KN_InitialBuffer = CS_Culling.FindKernel("InitialBuffer");//初始化所有顶点的核
        int KN_InterBuffer = CS_Culling.FindKernel("InterBuffer");//细分的核 现在只测试细分能否正常使用

        globalValueList = new Vector4[10];
        globalValueList[0].x = Camera.main.transform.position.x;
        globalValueList[0].y = Camera.main.transform.position.y;
        globalValueList[0].z = Camera.main.transform.position.z;
        globalValueList[0].w = Camera.main.fieldOfView;
        globalValueList[1].x = 0;
        globalValueList[1].y = 5;
        globalValueList[1].z = 0.078125f;
        globalValueList[1].w = 2;
        globalValueList[2].x = 20;
        globalValueList[2].y = 0.1f;

        //4 - 9 索引上用于表示视锥体的6个面
        Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(Camera.main);
        for (int i = 0; i < frustumPlanes.Length; i++)
        {
            globalValueList[4 + i] = new float4(frustumPlanes[i].normal, frustumPlanes[i].distance);
        }

        string minmaxMapPath = "Assets/Texture/Texture.asset";
        Texture2D minmaxMap = AssetDatabase.LoadAssetAtPath<Texture2D>(minmaxMapPath);
        CS_Culling.SetTexture(KN_InterBuffer, "MinMaxHeightMap", minmaxMap);
    }
    void LODCreate()
    {
        int counts = FinalBuffer.count;
        int[] zeros = new int[counts];

        int KN_InitialBuffer = CS_Culling.FindKernel("InitialBuffer");//初始化所有顶点的核
        int KN_InterBuffer = CS_Culling.FindKernel("InterBuffer");//细分的核 现在只测试细分能否正常使用
        int KN_Output = CS_Culling.FindKernel("Output"); //把值输出为Final

        cmd.BeginSample("初始化Buffer");

        cmd.SetBufferData(PPBuffer.front_buffer, zeros);
        cmd.SetBufferData(PPBuffer.back_buffer, zeros);
        cmd.SetBufferData(FinalBuffer, zeros);

        cmd.SetBufferCounterValue(FinalBuffer, 0);
        cmd.SetBufferCounterValue(PPBuffer.front_buffer, 0);
        cmd.SetBufferCounterValue(PPBuffer.back_buffer, 0);

        cmd.SetComputeVectorArrayParam(CS_Culling, GlobalValueName, globalValueList);
        cmd.SetComputeBufferParam(CS_Culling, KN_InitialBuffer, AppendName, PPBuffer.front_buffer);

        //因为之前上的那个纹理就是64x64
        cmd.DispatchCompute(CS_Culling, KN_InitialBuffer, 16, 1, 1);

        cmd.EndSample("初始化Buffer");

        cmd.BeginSample("GPUDriven测试开始");

        for (int i = 0; i <= 5; i++)
        {

            //记录ConterValue ，用于Dispatch
            cmd.CopyCounterValue(PPBuffer.front_buffer, DispatchArgsBuffer, 0);

            //重置back_buffer 相当于清理
            cmd.SetBufferCounterValue(PPBuffer.back_buffer, 0);

            cmd.SetComputeBufferParam(CS_Culling, KN_InterBuffer, CosumeName, PPBuffer.front_buffer);
            cmd.SetComputeBufferParam(CS_Culling, KN_InterBuffer, AppendName, PPBuffer.back_buffer);
            cmd.SetComputeBufferParam(CS_Culling, KN_InterBuffer, FinalName, FinalBuffer);
            cmd.DispatchCompute(CS_Culling, KN_InterBuffer, DispatchArgsBuffer, 0);

            PPBuffer.Swap();

        }

        cmd.EndSample("GPUDriven测试开始");


        cmd.BeginSample("输出到纹理");

        cmd.CopyCounterValue(FinalBuffer, DispatchArgsBuffer, 0);
        cmd.SetComputeBufferParam(CS_Culling, KN_Output, TempName, FinalBuffer);
        cmd.SetComputeTextureParam(CS_Culling, KN_Output, CullingResultName, CullTexture);
        cmd.DispatchCompute(CS_Culling, KN_Output, DispatchArgsBuffer, 0);

        cmd.EndSample("输出到纹理");
        Graphics.ExecuteCommandBuffer(cmd);
        cmd.Clear();

    }

    //Hiz的创建
    void HizCreate()
    {
        int KN_HiZInitial = CS_Culling.FindKernel("InitialHiZ");
        int KN_HiZBuild = CS_Culling.FindKernel("InterHiZ");

        cmd.Clear();
        cmd.BeginSample("HiZ深度图创建");

        cmd.SetComputeIntParam(CS_Culling, HiZWidth, Screen.width);
        cmd.SetComputeIntParam(CS_Culling ,HiZHeight, Screen.height);

        cmd.SetComputeVectorParam(CS_Culling, CameraDepthSize, new Vector2(Camera.main.pixelWidth, Camera.main.pixelHeight));
        cmd.SetComputeTextureParam(CS_Culling, KN_HiZInitial, HiZDestName, HiZTexture);

        cmd.DispatchCompute(CS_Culling,
            KN_HiZInitial,
            Mathf.CeilToInt(Screen.width / 8 + 1),
            Mathf.CeilToInt(Screen.height / 8 + 1),
            1);

        for(int i = 1; i <= 8; i++)
        {
            int width = Screen.width >> i;
            int height = Screen.height >> i;
      
            cmd.SetComputeTextureParam(CS_Culling, KN_HiZBuild, HiZSrcName, HiZTexture, i - 1);
            cmd.SetComputeTextureParam(CS_Culling, KN_HiZBuild, HiZDestName, HiZTexture, i);
            cmd.DispatchCompute(CS_Culling,
           KN_HiZBuild,
           Mathf.CeilToInt(width / 8 + 1),
           Mathf.CeilToInt(height / 8) + 1,
           1);
            
        }

        cmd.EndSample("HiZ深度图创建");
    }




    public void DrawMeshIndirectGraphics()
    {
        FinalBuffer.GetData(tempUse);
        for (int i = 0; i < 65536; i++)
        {
            EncodeXZLOD((uint)tempUse[i], out uint x, out uint z, out uint LOD);
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            float2 centerPos = GetNodeCenterPos(new uint2(x, z), (int)LOD);
            cube.transform.position = new Vector3(centerPos.x, 0, centerPos.y);

            float size = GetNodeSizeInLOD((int)LOD);
            cube.transform.localScale = new Vector3(size, size, size);

            cube.name = new Vector3(x, z, LOD).ToString();
        }

    }

    private void EncodeXZLOD(uint xzLOD, out uint x, out uint z, out uint LOD)
    {
        x = (xzLOD >> 16) & 0xFFFF; //x右移16位占32 - 16 16位
        z = (xzLOD >> 4) & 0xFFF; //z 占16 - 4 12位
        LOD = xzLOD & 0xF; //LOD4 - 0 四位
    }

    float GetNodeSizeInLOD(int LOD)
    {
        return 0.078125f * 2 * (1 << LOD);
    }

    int GetNodeNumInLOD(int LOD)
    {
        return (int)Mathf.Floor(20 / GetNodeSizeInLOD(LOD) + 0.1f);
    }

    float2 GetNodeCenterPos(uint2 nodeXY, int LOD)
    {
        float nodeSize = GetNodeSizeInLOD(LOD);
        int nodeCount = GetNodeNumInLOD(LOD);
        float2 nodePos = nodeSize * (new Vector2((float)nodeXY.x + 0.5f, nodeXY.y + 0.5f) - new Vector2(nodeCount * 0.5f, nodeCount * 0.5f)); //这里要减去nodeCount是因为我们默认了 索引0,0在左下角 整个分割中间的标准位置是0 ,0， 就是说实际的中间点是在变换的 中间的0.0位置这个点是锁定的
        return nodePos;
    }

}
