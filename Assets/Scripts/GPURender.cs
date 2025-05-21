using Unity.Mathematics;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public class GPURender : ScriptableRendererFeature
{
    public ComputeShader CS_Cull;
    GPULod m_ScriptablePass;

    /// <inheritdoc/>
    public override void Create()
    {
        m_ScriptablePass = new GPULod(CS_Cull);

        // Configures where the render pass should be injected.
        m_ScriptablePass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
    }

    // Here you can inject one or multiple render passes in the renderer.
    // This method is called when setting up the renderer once per-camera.
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(m_ScriptablePass);

    }

    class GPULod : ScriptableRenderPass
    {
        public static ComputeShader CS_GPULod;
        static Vector4[] globalValueList = new Vector4[10]; //用于存储所有常量的，包括摄像机这些 
        static TextureHandle depthTextureHandle;
        static string
            GlobalValueName = "globalValueList",
            CosumeName = "_ConsumeList",
            AppendName = "_AppendList",
            FinalName = "_FinalList",
            TempName = "_TempList",
            CullingResultName = "_CullingResult",
            HiZSrcName = "_HiZSrcDepthMip",
            HiZDestName = "_HiZDestDepthMip",
            HiZWidth = "HiZWidth",
            HiZHeight = "HiZHeight",
            CameraDepthSize = "_CameraDepthSize",
            CameraDepthTex = "_CameraDepthTexture";

        

        GraphicsBuffer DispatchArgsBuffer;
        PingPongBuffer_Graphics PPBuffer;
        PingPongTexture_Graphics PPTexture;
        GraphicsBuffer FinalBuffer;

        RTHandle CullTexture;

        /// <summary>
        /// 初始化
        /// </summary>
        /// <param name="CS_Lod"></param>
        public GPULod(ComputeShader CS_Lod)
        {
            CS_GPULod = CS_Lod;

            //这里初始化的是每个Pass的Resource 
            PPBuffer = new PingPongBuffer_Graphics(65536, 4);
            PPTexture = new PingPongTexture_Graphics(Screen.width, Screen.height, GraphicsFormat.R32_SFloat, true, true);
            FinalBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Append, 65536, 4);
            DispatchArgsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 3, 4);

            //初始化分配函数。
            uint[] dispatchArgs = new uint[] { 1, 1, 1 };
            DispatchArgsBuffer.SetData(dispatchArgs);


            CullTexture = RTHandles.Alloc(
              width: 65536,
              height: 1,
              format: GraphicsFormat.R32_SFloat,
                enableRandomWrite: true,
              name: "GPUTexture"
            );


            //初始化全局的数据
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

            int KN_InitialBuffer = CS_GPULod.FindKernel("InitialBuffer");//初始化所有顶点的核
            int KN_InterBuffer = CS_GPULod.FindKernel("InterBuffer");//细分的核 现在只测试细分能否正常使用

            //这个纹理算是高度图吧
            string minmaxMapPath = "Assets/Texture/Texture.asset";
            Texture2D minmaxMap = AssetDatabase.LoadAssetAtPath<Texture2D>(minmaxMapPath);
            CS_GPULod.SetTexture(KN_InterBuffer, "MinMaxHeightMap", minmaxMap);
        }

        /// <summary>
        /// 初始化PassData数据的
        /// </summary>
        /// <param name="passData"></param>
        /// <param name="renderGraph"></param>
        /// <param name="builder"></param>
        private void InitialPass(ref PassData passData,ref RenderGraph renderGraph , IComputeRenderGraphBuilder builder)
        {
            passData.counts = FinalBuffer.count;

            PPTexture.Import(ref renderGraph);
            passData.HiZTexHandle = PPTexture.texture_Handle;
            builder.UseTexture(passData.HiZTexHandle.front_buffer_Handle, AccessFlags.ReadWrite);
            builder.UseTexture(passData.HiZTexHandle.back_buffer_Handle, AccessFlags.ReadWrite);

            passData.CullTexHandle = renderGraph.ImportTexture(CullTexture);
            builder.UseTexture(passData.CullTexHandle, AccessFlags.ReadWrite);

            passData.DispatchArgsBuffer = renderGraph.ImportBuffer(DispatchArgsBuffer);
            builder.UseBuffer(passData.DispatchArgsBuffer);

             PPBuffer.Import(ref renderGraph);
            passData.PPBuffer = PPBuffer.buffer_Handle;
            builder.UseBuffer(passData.PPBuffer.front_buffer_Handle);
            builder.UseBuffer(passData.PPBuffer.back_buffer_Handle);

            passData.FinalBuffer = renderGraph.ImportBuffer(FinalBuffer);
            builder.UseBuffer(passData.FinalBuffer);

        }
        
        /// <summary>
        /// 每个Pass需要的Data
        /// </summary>
        private class PassData
        {
            public int counts;

            public BufferHandle DispatchArgsBuffer;
            public PingPongBuffer_Graphics.PingPongBufer_Handle PPBuffer;
            public BufferHandle FinalBuffer;

            public TextureHandle CullTexHandle;
            public PingPongTexture_Graphics.PingPongTexture_Handle HiZTexHandle;
        }

        /// <summary>
        /// 执行函数的函数吧
        /// </summary>
        /// <param name="data"></param>
        /// <param name="context"></param>
        static void ExecutePass(PassData data, ComputeGraphContext context)
        {
            ComputeCommandBuffer cmd = context.cmd;
            HiZCreate(data, cmd);
            LODCulling(data, cmd);
        
        }

       
        /// <summary>
        /// HiZ的生成
        /// </summary>
        /// <param name="data"></param>
        /// <param name="cmd"></param>
        static void HiZCreate(PassData data, ComputeCommandBuffer cmd)
        {
            int KN_HiZInitial = CS_GPULod.FindKernel("InitialHiZ");
            int KN_HiZInter = CS_GPULod.FindKernel("InterHiZ");

            cmd.BeginSample("HiZ深度图创建");

            cmd.SetComputeIntParam(CS_GPULod, HiZWidth, Screen.width);
            cmd.SetComputeIntParam(CS_GPULod, HiZHeight, Screen.height);
            cmd.SetComputeTextureParam(CS_GPULod, KN_HiZInitial, CameraDepthTex, depthTextureHandle);
            cmd.SetComputeVectorParam(CS_GPULod, CameraDepthSize, new Vector2(Camera.main.pixelWidth, Camera.main.pixelHeight));
            cmd.SetComputeTextureParam(CS_GPULod, KN_HiZInitial, HiZDestName, data.HiZTexHandle.front_buffer_Handle);

            cmd.DispatchCompute(CS_GPULod,
                KN_HiZInitial,
                Mathf.CeilToInt(Screen.width / 8),
                Mathf.CeilToInt(Screen.height / 8),
                1);

            for (int i = 1; i <= 5; i++)
            {
                int width = Screen.width >> i;
                int height = Screen.height >> i;

                cmd.SetComputeIntParam(CS_GPULod, "CurrentMipIndex", i - 1);
                cmd.SetComputeTextureParam(CS_GPULod, KN_HiZInter, HiZSrcName, data.HiZTexHandle.front_buffer_Handle, i - 1);
                cmd.SetComputeTextureParam(CS_GPULod, KN_HiZInter, HiZDestName, data.HiZTexHandle.back_buffer_Handle, i);
                cmd.DispatchCompute(CS_GPULod, KN_HiZInter, Mathf.CeilToInt(width / 8), Mathf.CeilToInt(height / 8), 1);

                data.HiZTexHandle.Swap();
            }
           
            cmd.EndSample("HiZ深度图创建");
        }

        /// <summary>
        /// LOD的生成与剔除
        /// </summary>
        /// <param name="data"></param>
        /// <param name="cmd"></param>
        static void LODCulling(PassData data, ComputeCommandBuffer cmd)
        {
            int[] zeros = new int[data.counts];

            int KN_InitialBuffer = CS_GPULod.FindKernel("InitialBuffer");//初始化所有顶点的核
            int KN_InterBuffer = CS_GPULod.FindKernel("InterBuffer");//细分的核 现在只测试细分能否正常使用
            int KN_Output = CS_GPULod.FindKernel("Output"); //把值输出为Final

            cmd.BeginSample("初始化Buffer");

            cmd.SetBufferData(data.PPBuffer.front_buffer_Handle, zeros);
            cmd.SetBufferData(data.PPBuffer.back_buffer_Handle, zeros);
            cmd.SetBufferData(data.FinalBuffer, zeros);

            cmd.SetBufferCounterValue(data.FinalBuffer, 0);
            cmd.SetBufferCounterValue(data.PPBuffer.front_buffer_Handle, 0);
            cmd.SetBufferCounterValue(data.PPBuffer.back_buffer_Handle, 0);

            cmd.SetComputeVectorArrayParam(CS_GPULod, GlobalValueName, globalValueList);
            cmd.SetComputeBufferParam(CS_GPULod, KN_InitialBuffer, AppendName, data.PPBuffer.front_buffer_Handle);

            //因为之前上的那个纹理就是64x64
            cmd.DispatchCompute(CS_GPULod, KN_InitialBuffer, 16, 1, 1);

            cmd.EndSample("初始化Buffer");

            cmd.BeginSample("GPUDriven测试开始");

            for (int i = 0; i <= 5; i++)
            {

                //记录ConterValue ，用于Dispatch
                cmd.CopyCounterValue(data.PPBuffer.front_buffer_Handle, data.DispatchArgsBuffer, 0);

                //重置back_buffer 相当于清理
                cmd.SetBufferCounterValue(data.PPBuffer.back_buffer_Handle, 0);

                cmd.SetComputeBufferParam(CS_GPULod, KN_InterBuffer, CosumeName, data.PPBuffer.front_buffer_Handle);
                cmd.SetComputeBufferParam(CS_GPULod, KN_InterBuffer, AppendName, data.PPBuffer.back_buffer_Handle);
                cmd.SetComputeBufferParam(CS_GPULod, KN_InterBuffer, FinalName, data.FinalBuffer);
                cmd.DispatchCompute(CS_GPULod, KN_InterBuffer, data.DispatchArgsBuffer, 0);

                data.PPBuffer.Swap();
            }

            cmd.EndSample("GPUDriven测试开始");
            cmd.BeginSample("输出到纹理");

            cmd.CopyCounterValue(data.FinalBuffer, data.DispatchArgsBuffer, 0);
            cmd.SetComputeBufferParam(CS_GPULod, KN_Output, TempName, data.FinalBuffer);
            cmd.SetComputeTextureParam(CS_GPULod, KN_Output, CullingResultName, data.CullTexHandle);
            cmd.DispatchCompute(CS_GPULod, KN_Output, data.DispatchArgsBuffer, 0);

            cmd.EndSample("输出到纹理");
        }

       /// <summary>
       /// 记录Pass
       /// </summary>
       /// <param name="renderGraph"></param>
       /// <param name="frameData"></param>
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            const string passName = "GPULod";

            // This adds a raster render pass to the graph, specifying the name and the data type that will be passed to the ExecutePass function.
            using (var builder = renderGraph.AddComputePass<PassData>(passName, out var passData))
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();//获取当前帧的渲染资源数据
                depthTextureHandle = resourceData.activeDepthTexture;
                builder.UseTexture(depthTextureHandle, AccessFlags.Read);
                InitialPass(ref passData, ref renderGraph, builder);

                builder.SetRenderFunc((PassData data, ComputeGraphContext context) => ExecutePass(data, context));
            }
        }

    }
}
