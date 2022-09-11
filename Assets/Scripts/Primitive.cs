using System;
using UnityEngine;

[Serializable]
public struct Primitive
{
    public Vector3 pos;
    public Vector3 size;
    public float bevel;
    public Matrix4x4 rotationMatrix;
    public int sdfOperation;
    public int parentIndex;
    public int type;
}