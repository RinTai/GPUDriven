using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;


public class MipmapCreator : EditorWindow
{
    public Texture2D heightMap; //用于第一次输入的高度图纹理 原纹理

    public RenderTexture outputRT;
    //用于生成LOD的CS
    public ComputeShader mipmapCreatorCS;
    //输出纹理的大小 每次迭代都要重新设置
    public Vector2Int outputTextureSize;
    //Mip的层数
    public int mipmapCount;
    public string mOutputFileName;
    public List<RenderTexture> mipmaps = new List<RenderTexture>();

    private static uint RootNum;

    [MenuItem("Window/BuildMinMaxHeightMap")]
    public static void CreateEditorWindow()
    {
        EditorWindow.GetWindow(typeof(MipmapCreator));
    }

    public void OnGUI()
    {
        EditorGUILayout.BeginVertical();
        heightMap = EditorGUILayout.ObjectField(new GUIContent("高度图"), heightMap, typeof(Texture2D), true) as Texture2D;
        mOutputFileName = EditorGUILayout.TextField(new GUIContent("MinMaxHeighMap输出文件名，不需后缀"), mOutputFileName);
        outputTextureSize = EditorGUILayout.Vector2IntField(new GUIContent("输出MinMaxHeighMap mip0的分辨率"), outputTextureSize);
        mipmapCount = EditorGUILayout.IntField(new GUIContent("输出的mip层数"), mipmapCount);
        if (GUILayout.Button("生成"))
        {
            BuildMipMap();
        }
        EditorGUILayout.EndVertical();
    }

    private void BuildMipMap()
    {
        mipmaps.Clear();
        //创建初始的纹理
        RenderTextureDescriptor outputRTDesc = new RenderTextureDescriptor(outputTextureSize.x, outputTextureSize.y, RenderTextureFormat.RGFloat, 0, 1);
        outputRTDesc.autoGenerateMips = false;
        outputRTDesc.useMipMap = true;
        outputRTDesc.mipCount = mipmapCount;
        outputRTDesc.enableRandomWrite = true;
        outputRT = RenderTexture.GetTemporary(outputRTDesc);
        outputRT.filterMode = FilterMode.Point;
        outputRT.Create();

        int KN_BuildMaxMinByMap = mipmapCreatorCS.FindKernel("BuildMaxMinByMap");
        int KN_BuildMaxMinByMinMaxMap = mipmapCreatorCS.FindKernel("BuildMaxMinByMaxMinMap");

        mipmapCreatorCS.SetInts("srcTexSize",new int[2] {heightMap.width, heightMap.height});
        mipmapCreatorCS.SetInts("destTexSize",new int[2] {outputTextureSize.x,outputTextureSize.y});

        mipmapCreatorCS.SetTexture(KN_BuildMaxMinByMap, Shader.PropertyToID("readMap"), heightMap);
        mipmapCreatorCS.SetTexture(KN_BuildMaxMinByMap, Shader.PropertyToID("OutputMap"), outputRT);

        mipmapCreatorCS.Dispatch(KN_BuildMaxMinByMap,outputTextureSize.x / 16 ,outputTextureSize.y / 16,1);
        
        for(int i = 1; i < mipmapCount; i++)
        {
            Vector2Int destTexSize = new Vector2Int(outputTextureSize.x >> i,outputTextureSize.y >> i); //也行吧

            RenderTextureDescriptor inputRTDesc = new RenderTextureDescriptor(destTexSize.x * 2, destTexSize.y * 2, RenderTextureFormat.RGFloat, 0, 1);
            inputRTDesc.enableRandomWrite = true;
            inputRTDesc.autoGenerateMips = false;
            RenderTexture inputRT = RenderTexture.GetTemporary(inputRTDesc);
            inputRT.filterMode = FilterMode.Point;
            inputRT.Create();

            Graphics.CopyTexture(outputRT, 0, i - 1, inputRT, 0, 0);

            mipmapCreatorCS.SetInts("srcTexSize", new int[2] { destTexSize.x * 2, destTexSize.y * 2 });
            mipmapCreatorCS.SetInts("destTexSize", new int[2] { destTexSize.x, destTexSize.y });

            mipmapCreatorCS.SetTexture(KN_BuildMaxMinByMinMaxMap, Shader.PropertyToID("InputMap"), inputRT);
            mipmapCreatorCS.SetTexture(KN_BuildMaxMinByMinMaxMap, Shader.PropertyToID("OutputMap"), outputRT, i);

            mipmapCreatorCS.Dispatch(KN_BuildMaxMinByMinMaxMap, outputTextureSize.x / 16, outputTextureSize.y / 16, 1);

            mipmaps.Add(inputRT);//这里加inputRT就不需要在加初始的了，加的是上一次的output
            
            RootNum = (uint)destTexSize.x;
        }

        Texture2D texture2D = new Texture2D(outputRT.width, outputRT.height, TextureFormat.RGFloat, mipmapCount, false);
        texture2D.filterMode = FilterMode.Point;

        List<int> readResult = new List<int>();
        for (int i = 0; i < mipmapCount; i++)
        {
            ReadRenderTexture(outputRT, texture2D, i, mipmapCount, readResult, () => {
                AssetDatabase.CreateAsset(texture2D, "Assets/Texture/" + mOutputFileName + ".asset");
                AssetDatabase.Refresh();
                Dispose();
            });
        }
    }
    private void ReadRenderTexture(RenderTexture renderTexture, Texture2D tex2D, int mip, int mipcount, List<int> readResult, Action callback)
    {
        AsyncGPUReadback.Request(renderTexture, mip, (req) => {
            tex2D.SetPixelData(req.GetData<Vector2>(), mip);
            readResult.Add(mip);
            if (readResult.Count == mipcount)
            {
                callback();
            }
        });
    }

    //压缩数字
    private void Dispose()
    {
        RenderTexture.ReleaseTemporary(outputRT);
        foreach(var rt in mipmaps)
        {
            RenderTexture.ReleaseTemporary (rt);
        }
    }

    static public uint GetRootNum()
    {
        return RootNum;
    }
}
