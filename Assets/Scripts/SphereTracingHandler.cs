using UnityEngine;

[RequireComponent(typeof(Camera))]
public class SphereTracingHandler : MonoBehaviour
{
    [SerializeField] private ShaderEventChannel shaderEventChannel;
    [SerializeField] private Shader shader;
    [SerializeField] private Light mainLight;
    [SerializeField] private bool fixLightToCamera;

    [SerializeField, Range(1, 8)] private int antiAliasing = 1;
    [SerializeField, Range(0, 8)] private int ambientOcclusionIterations = 0;
    [SerializeField, Range(0f, 1f)] private float ambientOcclusionStrength = 0;

    private Material sphereTracingMat;
    private Camera cam;
    private readonly int frustumShaderProp = Shader.PropertyToID("camFrustum");
    private readonly int camToWorldShaderProp = Shader.PropertyToID("camToWorld");
    private readonly int lightPosShaderProp = Shader.PropertyToID("lightPos");
    private readonly int lightColorShaderProp = Shader.PropertyToID("lightColor");
    private readonly int mainTexShaderProp = Shader.PropertyToID("mainTex");
    private readonly int aaSamplesShaderProp = Shader.PropertyToID("aaSamples");
    private readonly int aoIterationsShaderProp = Shader.PropertyToID("aoIterations");
    private readonly int aoIntensityShaderProp = Shader.PropertyToID("aoIntensity");
    private readonly int primitivesShaderProp = Shader.PropertyToID("primitives");
    private readonly int numPrimitivesShaderProp = Shader.PropertyToID("numPrimitives");

    public Material SphereTracingMat
    {
        get
        {
            if (!sphereTracingMat && shader)
            {
                sphereTracingMat = new Material(shader) {hideFlags = HideFlags.HideAndDontSave};
            }

            return sphereTracingMat;
        }
    }

    public Camera Camera
    {
        get
        {
            if (!cam)
            {
                cam = GetComponent<Camera>();
            }

            return cam;
        }
    }

    private void OnEnable()
    {
        shaderEventChannel.UpdateShaderBuffer += UpdateShaderBuffer;
    }

    private void OnDisable()
    {
        shaderEventChannel.UpdateShaderBuffer -= UpdateShaderBuffer;
    }

    private void Start()
    {
        SphereTracingMat.SetInt(numPrimitivesShaderProp, 0);
    }

    // Camera Setup Source: https://www.youtube.com/watch?v=82iBWIycU0o&list=PL3POsQzaCw53iK_EhOYR39h1J9Lvg-m-g&index=2
    private void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        if (!SphereTracingMat)
        {
            Graphics.Blit(src, dest);
            return;
        }

        if (fixLightToCamera)
        {
            mainLight.transform.position = Camera.transform.position;
        }

        SphereTracingMat.SetMatrix(frustumShaderProp, CamFrustum(Camera));
        SphereTracingMat.SetMatrix(camToWorldShaderProp, Camera.cameraToWorldMatrix);
        SphereTracingMat.SetVector(lightPosShaderProp, mainLight.transform.position);
        SphereTracingMat.SetVector(lightColorShaderProp, mainLight.color);
        SphereTracingMat.SetInt(aaSamplesShaderProp, antiAliasing);
        SphereTracingMat.SetInt(aoIterationsShaderProp, ambientOcclusionIterations);
        SphereTracingMat.SetFloat(aoIntensityShaderProp, ambientOcclusionStrength);

        RenderTexture.active = dest;
        SphereTracingMat.SetTexture(mainTexShaderProp, src);
        GL.PushMatrix();
        GL.LoadOrtho();
        SphereTracingMat.SetPass(0);
        GL.Begin(GL.QUADS);

        // bl
        GL.MultiTexCoord2(0, 0f, 0f);
        GL.Vertex3(0f, 0f, 3f);

        // br
        GL.MultiTexCoord2(0, 1f, 0f);
        GL.Vertex3(1f, 0f, 2f);

        // tr
        GL.MultiTexCoord2(0, 1f, 1f);
        GL.Vertex3(1f, 1f, 1f);

        // tl
        GL.MultiTexCoord2(0, 0f, 1f);
        GL.Vertex3(0f, 1f, 0f);

        GL.End();
        GL.PopMatrix();
    }

    private Matrix4x4 CamFrustum(Camera c)
    {
        Matrix4x4 frustum = Matrix4x4.identity;
        float fov = Mathf.Tan((c.fieldOfView * 0.5f) * Mathf.Deg2Rad);

        Vector3 up = Vector3.up * fov;
        Vector3 right = Vector3.right * fov * c.aspect;

        Vector3 tl = -Vector3.forward - right + up;
        Vector3 tr = -Vector3.forward + right + up;
        Vector3 br = -Vector3.forward + right - up;
        Vector3 bl = -Vector3.forward - right - up;

        frustum.SetRow(0, tl);
        frustum.SetRow(1, tr);
        frustum.SetRow(2, br);
        frustum.SetRow(3, bl);

        return frustum;
    }

    private void UpdateShaderBuffer(ComputeBuffer buffer)
    {
        SphereTracingMat.SetInt(numPrimitivesShaderProp, buffer.count);
        SphereTracingMat.SetBuffer(primitivesShaderProp, buffer);
    }
}