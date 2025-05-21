using UnityEngine;
using UnityEngine.Rendering.RenderGraphModule;

public class PingPongBuffer
{
    public ComputeBuffer front_buffer;
    public ComputeBuffer back_buffer;

    public PingPongBuffer(int count,int stride)
    {
        front_buffer = new ComputeBuffer(count, stride, ComputeBufferType.Append);
        back_buffer = new ComputeBuffer(count, stride, ComputeBufferType.Append);
    }

    public void Swap()
    {
        (front_buffer, back_buffer) = (back_buffer, front_buffer);
    }

    public void Dispose()
    {
        front_buffer.Dispose();
        back_buffer.Dispose();
    }
}
public class PingPongBuffer_Graphics
{
    public struct PingPongBufer_Handle
    {
        public BufferHandle front_buffer_Handle;
        public BufferHandle back_buffer_Handle;

        public void Swap()
        {
            (front_buffer_Handle, back_buffer_Handle) = (back_buffer_Handle, front_buffer_Handle);
        }
    }

    public GraphicsBuffer front_buffer;
    public GraphicsBuffer back_buffer;

    public PingPongBufer_Handle buffer_Handle;

    public PingPongBuffer_Graphics(int count, int stride)
    {
        front_buffer = new GraphicsBuffer(GraphicsBuffer.Target.Append, count, stride);
        back_buffer = new GraphicsBuffer(GraphicsBuffer.Target.Append, count, stride);
        buffer_Handle = new PingPongBufer_Handle();
    }

    public void Swap()
    {
        (front_buffer, back_buffer) = (back_buffer, front_buffer);
    }

    public void Dispose()
    {
        front_buffer.Dispose();
        back_buffer.Dispose();
    }

    public void Import(ref RenderGraph renderGraph)
    {
        buffer_Handle.front_buffer_Handle = renderGraph.ImportBuffer(front_buffer);
        buffer_Handle.back_buffer_Handle = renderGraph.ImportBuffer(back_buffer);
    }

    public void Import(ref RenderGraph renderGraph,out PingPongBufer_Handle buffer_Handle)
    {
        buffer_Handle.front_buffer_Handle = renderGraph.ImportBuffer(front_buffer);
        buffer_Handle.back_buffer_Handle = renderGraph.ImportBuffer(back_buffer);
    }
}