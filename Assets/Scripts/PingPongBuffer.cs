using UnityEngine;

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
