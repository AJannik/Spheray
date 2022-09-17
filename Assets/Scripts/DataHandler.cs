using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;

public class DataHandler : MonoBehaviour
{
    [SerializeField] private ShaderEventChannel shaderEventChannel;
    [SerializeField] private UiEventChannel uiEventChannel;
    [SerializeField] private GameObject scrollView;
    [SerializeField] private GameObject uiPrefab;
    [SerializeField] private GameObject prefab;

    private ComputeBuffer primitiveBuffer;
    [SerializeField] private List<Transform> primitiveObjects = new List<Transform>();

    private void OnEnable()
    {
        shaderEventChannel.PrimitiveValueChanged += CreateBuffer;
        shaderEventChannel.HierarchyChanged += RebuildHierarchy;
        uiEventChannel.SaveSDF += SaveSDF;
        uiEventChannel.LoadSDF += LoadSDF;
    }

    private void OnDisable()
    {
        shaderEventChannel.PrimitiveValueChanged -= CreateBuffer;
        shaderEventChannel.HierarchyChanged -= RebuildHierarchy;
        uiEventChannel.SaveSDF -= SaveSDF;
        uiEventChannel.LoadSDF -= LoadSDF;
    }

    private void Start()
    {
        GameObject go = Instantiate(prefab, transform);
        go.name = $"Primitive {primitiveObjects.Count}";
        primitiveObjects.Add(go.transform);

        if (primitiveObjects.Count > 0)
        {
            CreateBuffer();
        }
    }

    private void Update()
    {
        if (primitiveObjects.Count >= 20)
        {
            Debug.LogError("Max number of 20 Primitives reached!");
            return;
        }
        
        if (Input.GetKeyDown(KeyCode.B))
        {
            SpawnPrimitive(Operation.Union);
        }
        else if (Input.GetKeyDown(KeyCode.N))
        {
            SpawnPrimitive(Operation.Difference);
        }
        else if (Input.GetKeyDown(KeyCode.M))
        {
            SpawnPrimitive(Operation.Intersection);
        }

        if (transform.childCount > 0)
        {
            UpdateDataTransforms();
        }
    }

    private void SpawnPrimitive(Operation op)
    {
        GameObject go = Instantiate(prefab, primitiveObjects[0].transform);
        go.name = $"Primitive {primitiveObjects.Count}";
        go.GetComponent<PrimitiveDataHandler>().operation = op;
        primitiveObjects.Add(go.transform);
        GameObject ui = Instantiate(uiPrefab, scrollView.transform);
        ui.GetComponentInChildren<TextMeshProUGUI>().text = go.name;
    }

    private void CreateBuffer()
    {
        List<Primitive> primitives = new List<Primitive>();
        foreach (Transform t in primitiveObjects)
        {
            Matrix4x4 m = Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
            PrimitiveDataHandler pdh = t.GetComponent<PrimitiveDataHandler>();
            primitives.Add(new Primitive
            {
                pos = t.position, size = t.localScale, bevel = pdh.bevel, rotationMatrix = m, parentIndex = primitiveObjects.IndexOf(t.parent),
                sdfOperation = (int) pdh.operation, type = (int) pdh.type
            });
        }

        primitiveBuffer?.Release();
        primitiveBuffer = new ComputeBuffer(primitives.Count, System.Runtime.InteropServices.Marshal.SizeOf(typeof(Primitive)));
        primitiveBuffer.SetData(primitives);
        shaderEventChannel.RaiseUpdateShaderBuffer(primitiveBuffer);
    }

    private void UpdateDataTransforms()
    {
        bool hasChanged = false;
        foreach (Transform t in primitiveObjects)
        {
            if (!t)
            {
                hasChanged = true;
            }
            else if (t.transform.hasChanged)
            {
                t.transform.hasChanged = false;
                hasChanged = true;
            }
        }

        if (hasChanged)
        {
            RebuildHierarchy();
        }
    }

    private void RebuildHierarchy()
    {
        primitiveObjects = RebuildListBufferOptimised(transform.GetChild(0));
        CreateBuffer();
    }

    private List<Transform> RebuildListBufferOptimised(Transform root)
    {
        List<Transform> list = new List<Transform>();
        Queue<Transform> queue = new Queue<Transform>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            Transform node = queue.Dequeue();
            list.Add(node);

            for (int i = node.childCount - 1; i >= 0; i--)
            {
                queue.Enqueue(node.GetChild(i));
            }
        }

        return list;
    }
    
    private List<Transform> GetListInOrder(Transform root)
    {
        List<Transform> list = new List<Transform>();
        Queue<Transform> queue = new Queue<Transform>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            Transform node = queue.Dequeue();
            list.Add(node);

            for (int i = 0; i < node.childCount; i++)
            {
                queue.Enqueue(node.GetChild(i));
            }
        }

        return list;
    }

    private void SaveSDF(string fileName)
    {
        File.WriteAllText(Application.dataPath + $"/data/{fileName}.json", GenerateJson());
        Debug.Log($"Saved {fileName}.json successfully.");
    }
    
    private string GenerateJson()
    {
        List<Transform> primitivesInOrder = GetListInOrder(transform.GetChild(0));
        DataHolder data = new() {primitives = new List<Primitive>()};
        foreach (Transform t in primitivesInOrder)
        {
            Matrix4x4 m = Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
            PrimitiveDataHandler pdh = t.GetComponent<PrimitiveDataHandler>();
            data.primitives.Add(new Primitive
            {
                pos = t.position, size = t.localScale, bevel = pdh.bevel, rotationMatrix = m, parentIndex = primitiveObjects.IndexOf(t.parent),
                sdfOperation = (int) pdh.operation, type = (int) pdh.type
            });
        }

        return JsonUtility.ToJson(data);
    }

    private void LoadSDF(DataHolder data)
    {
        DeleteOldHierarchy();
        foreach (Primitive prim in data.primitives)
        {
            GameObject go;
            if (prim.parentIndex < 0)
            {
                go = Instantiate(prefab, transform);
            }
            else
            {
                go = Instantiate(prefab, primitiveObjects[prim.parentIndex].transform);
            }
            
            go.name = $"Primitive {primitiveObjects.Count}";
            go.transform.position = prim.pos;
            go.transform.rotation = prim.rotationMatrix.rotation;
            go.transform.localScale = prim.size;
            PrimitiveDataHandler pdh = go.GetComponent<PrimitiveDataHandler>();
            pdh.operation = (Operation) prim.sdfOperation;
            pdh.bevel = prim.bevel;
            pdh.type = (PrimitiveSdfType) prim.type;
            primitiveObjects.Add(go.transform);
            GameObject ui = Instantiate(uiPrefab, scrollView.transform);
            ui.GetComponentInChildren<TextMeshProUGUI>().text = go.name;
        }
        
        RebuildHierarchy();
    }

    private void DeleteOldHierarchy()
    {
        for (int i = primitiveObjects.Count - 1; i >= 0; i--)
        {
            Destroy(primitiveObjects[i].gameObject);
        }

        primitiveObjects.Clear();
    }

    private void OnDestroy()
    {
        primitiveBuffer?.Release();
    }
}