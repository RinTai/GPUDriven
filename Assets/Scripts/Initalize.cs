using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine.Profiling;
using UnityEngine.Windows;
using System.IO;
using Mono.Cecil;
using UnityEditor;


public class Test : MonoBehaviour
{
    CommandBuffer cmd;

    public ComputeShader CS_Culling;

    Sampler sample;
    ComputeBuffer DispatchArgsBuffer;
    PingPongBuffer PPBuffer;
    ComputeBuffer FinalBuffer;
    Vector4[] globalValueList = new Vector4[10]; //用于存储所有常量的，包括摄像机这些 

    static string
        GlobalValueName = "globalValueList",
        CosumeName = "_ConsumeList",
        AppendName = "_AppendList",
        FinalName = "_FinalList";



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
            globalValueList[4 + i] = new float4(frustumPlanes[i].normal,frustumPlanes[i].distance);
        }

        string minmaxMapPath = "Assets/Texture/Texture.asset";
        Texture2D minmaxMap = AssetDatabase.LoadAssetAtPath<Texture2D>(minmaxMapPath);
        CS_Culling.SetTexture(KN_InterBuffer, "MinMaxHeightMap",minmaxMap);
    }
    void TestBuffer()
    {
        int counts = FinalBuffer.count;
        int[] zeros = new int[counts];
        cmd.Clear();

        int KN_InitialBuffer = CS_Culling.FindKernel("InitialBuffer");//初始化所有顶点的核
        int KN_InterBuffer = CS_Culling.FindKernel("InterBuffer");//细分的核 现在只测试细分能否正常使用

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
            cmd.CopyCounterValue(PPBuffer.front_buffer, DispatchArgsBuffer,0);

            //重置back_buffer 相当于清理
            cmd.SetBufferCounterValue(PPBuffer.back_buffer, 0);

            cmd.SetComputeBufferParam(CS_Culling, KN_InterBuffer, CosumeName, PPBuffer.front_buffer);
            cmd.SetComputeBufferParam(CS_Culling, KN_InterBuffer, AppendName, PPBuffer.back_buffer);
            cmd.SetComputeBufferParam(CS_Culling, KN_InterBuffer, FinalName, FinalBuffer);
            cmd.DispatchCompute(CS_Culling, KN_InterBuffer, DispatchArgsBuffer,0);

            PPBuffer.Swap();

        }

        cmd.EndSample("GPUDriven测试开始");

        Graphics.ExecuteCommandBuffer(cmd);


    }
    private void Start()
    {
        cmd = new CommandBuffer();
        cmd.name = "一个CMD";
        PPBuffer = new PingPongBuffer(65536, 4);
        FinalBuffer = new ComputeBuffer(65536, 4, ComputeBufferType.Append);
        DispatchArgsBuffer = new ComputeBuffer(3,4, ComputeBufferType.IndirectArguments);
        uint[] dispatchArgs = new uint[] { 1, 1, 1 };
        DispatchArgsBuffer.SetData(dispatchArgs);
        InitialGlobalVlaue();
        
    }
    private void Update()
    {
        TestBuffer();
    }
}
