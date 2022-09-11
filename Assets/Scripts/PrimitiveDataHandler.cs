using System;
using UnityEngine;

public class PrimitiveDataHandler : MonoBehaviour
{
    [SerializeField] private ShaderEventChannel shaderEventChannel;
    public Operation operation = Operation.Union;  // 0 = union, 1 = difference, 2 = intersect
    public PrimitiveSdfType type = PrimitiveSdfType.Sphere;
    [Range(0f, 0.2f)] public float bevel = 0f;
    private Operation oldOperation = Operation.Union;
    private PrimitiveSdfType oldSdfType = PrimitiveSdfType.Sphere;
    private int oldSiblingIndex;
    private float oldBevel;

    private void Start()
    {
        oldSiblingIndex = transform.GetSiblingIndex();
        oldBevel = bevel;
    }

    private void Update()
    {
        if (oldSiblingIndex != transform.GetSiblingIndex())
        {
            oldOperation = operation;
            oldSdfType = type;
            oldSiblingIndex = transform.GetSiblingIndex();
            shaderEventChannel.RaiseHierarchyChanged();
            return;    
        }
        
        if (oldOperation != operation || oldSdfType != type || Math.Abs(oldBevel - bevel) > 0.01f)
        {
            oldOperation = operation;
            oldSdfType = type;
            oldBevel = bevel;
            shaderEventChannel.RaisePrimitiveValueChanged();
        }
    }
}

public enum Operation
{
    Union, Difference, Intersection
}

public enum PrimitiveSdfType
{
    Sphere, Box, Plane, Cylinder, Ellipsoid, Torus, HexPrism
}