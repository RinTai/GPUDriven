using UnityEngine;

public class Blast : MonoBehaviour
{
    public ComputeShader CollsionDectiveCS;
    public ComputeShader CrashCS; //用于计算破坏效果

    MeshFilter m_meshFilter; //用于破坏的墙壁的Mesh

    private void Start()
    {
        m_meshFilter = GetComponent<MeshFilter>();
    }

    private void Update()
    {
        
    }
}
