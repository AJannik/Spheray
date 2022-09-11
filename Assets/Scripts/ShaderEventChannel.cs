using System;
using UnityEngine;

[CreateAssetMenu(fileName = "EventChannel", menuName = "EventChannel/Shader")]
public class ShaderEventChannel : ScriptableObject
{
    public Action<ComputeBuffer> UpdateShaderBuffer;
    public Action PrimitiveValueChanged;
    public Action HierarchyChanged;
    public Action LightChanged;

    public void RaiseUpdateShaderBuffer(ComputeBuffer buffer)
    {
        UpdateShaderBuffer?.Invoke(buffer);
    }

    public void RaisePrimitiveValueChanged()
    {
        PrimitiveValueChanged?.Invoke();
    }

    public void RaiseHierarchyChanged()
    {
        HierarchyChanged?.Invoke();
    }

    public void RaiseLightChanged()
    {
        LightChanged?.Invoke();
    }
}