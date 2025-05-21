using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

public class PingPongTexture
{
    public RenderTexture front_Tex;
    public RenderTexture back_Tex;

    public PingPongTexture(int width,int height,int depth,RenderTextureFormat format)
    {
        front_Tex = new RenderTexture(width, height, depth, format);
        back_Tex = new RenderTexture(width, height, depth, format);
        
        front_Tex.enableRandomWrite = true;
        back_Tex.enableRandomWrite = true;

        front_Tex.Create();
        back_Tex.Create();
    }

    public void Swap()
    {
        (front_Tex, back_Tex) = (back_Tex, front_Tex);
    }

    public void Dispose()
    {
        front_Tex.Release();
        back_Tex.Release();
    }
}

public class PingPongTexture_Graphics
{
    public RTHandle front_Tex;
    public RTHandle back_Tex;

    public struct PingPongTexture_Handle
    {
        public TextureHandle front_buffer_Handle;
        public TextureHandle back_buffer_Handle;

        public void Swap()
        {
            (front_buffer_Handle, back_buffer_Handle) = (back_buffer_Handle, front_buffer_Handle);
        }
    }
    public PingPongTexture_Handle texture_Handle;

    public PingPongTexture_Graphics(int Width, int Height, GraphicsFormat Format, bool EnableRandomWrite = true, bool UseMipmaps = false, bool AutoGenerateMips = false)
    {
        front_Tex = RTHandles.Alloc(
            width: Width,
            height: Height,
            format: Format,
            enableRandomWrite: EnableRandomWrite,
            useMipMap: UseMipmaps,
            autoGenerateMips: AutoGenerateMips,
            name: "Front_Tex");
        back_Tex = RTHandles.Alloc(
            width: Width,
            height: Height,
            format: Format,
            enableRandomWrite: EnableRandomWrite,
            useMipMap: UseMipmaps,
            autoGenerateMips: AutoGenerateMips,
            name: "Back_Tex");

        texture_Handle = new PingPongTexture_Handle();
    }

    public void Swap()
    {
        (front_Tex, back_Tex) = (back_Tex, front_Tex);
    }

    public void Dispose()
    {
        front_Tex.Release();
        back_Tex.Release();
    }

    public void Import(ref RenderGraph renderGraph)
    {
        texture_Handle.front_buffer_Handle = renderGraph.ImportTexture(front_Tex);
         texture_Handle.back_buffer_Handle = renderGraph.ImportTexture(back_Tex);       
    }

    public void Import(ref RenderGraph renderGraph, PingPongTexture_Handle texture_Handle)
    {
        texture_Handle.front_buffer_Handle = renderGraph.ImportTexture(front_Tex);
        texture_Handle.back_buffer_Handle = renderGraph.ImportTexture(back_Tex);
    }
}
