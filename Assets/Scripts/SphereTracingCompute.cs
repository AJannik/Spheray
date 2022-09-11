using System;
using UnityEngine;

public class SphereTracingCompute : MonoBehaviour
{
    [SerializeField] private ShaderEventChannel shaderEventChannel;
    [SerializeField] private ComputeShader shader;
    [SerializeField] private ComputeShader superResShader;
    [SerializeField] private Shader renderShader;
    [SerializeField] private Light mainLight;
    [SerializeField] private bool fixLightToCamera;
    [SerializeField, Range(0, 2)] private int reflectionIterations;
    [SerializeField, Range(0f, 1f)] private float reflectionIntensity;
    [SerializeField, Range(0, 8)] private int aoIterations;
    [SerializeField, Range(0f, 1f)] private float aoIntensity;
    [SerializeField, Range(0.01f, 1f)] private float aoSize = 0.2f;
    [SerializeField, Range(0.00312f, 0.833f)] private float fxaaThreshold = 0.0312f;
    [SerializeField, Range(0.0063f, 0.9333f)] private float relativeThreshold = 0.063f;
    [SerializeField, Range(0f, 1f)] private float subPixelBlending = 1f;

    private Material material;
    private int tracerKernel;
    private int srKernel;
    private int fillKernel;
    private int blendKernel;
    private RenderTexture texture;
    private RenderTexture lr;
    private Camera cam;
    private float oldFov;
    private readonly int camToWorldShaderProp = Shader.PropertyToID("camToWorld");
    private readonly int lightPosShaderProp = Shader.PropertyToID("lightPos");
    private readonly int lightColorShaderProp = Shader.PropertyToID("lightColor");
    private readonly int lightIntensityShaderProp = Shader.PropertyToID("lightIntensity");
    private readonly int resultShaderProp = Shader.PropertyToID("Result");
    private readonly int primitivesShaderProp = Shader.PropertyToID("primitives");
    private readonly int numPrimitivesShaderProp = Shader.PropertyToID("numPrimitives");
    private readonly int bgTexShaderProp = Shader.PropertyToID("_BgTex");

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
        shaderEventChannel.LightChanged += UpdateLight;
    }

    private void OnDisable()
    {
        shaderEventChannel.UpdateShaderBuffer -= UpdateShaderBuffer;
        shaderEventChannel.LightChanged -= UpdateLight;
    }

    private void Awake()
    {
        texture = new RenderTexture(1920, 1080, 16) {enableRandomWrite = true};
        lr = new RenderTexture(1920 / 2, 1080 / 2, 8) {enableRandomWrite = true};
        lr.Create();
        texture.Create();
        tracerKernel = shader.FindKernel("CSMain");
        srKernel = superResShader.FindKernel("SuperResolution");
        fillKernel = superResShader.FindKernel("Fill");
        blendKernel = superResShader.FindKernel("Blend");
        shader.SetTexture(tracerKernel, resultShaderProp, lr);
        superResShader.SetTexture(srKernel, "tex", lr);
        superResShader.SetTexture(srKernel, "Result", texture);
        superResShader.SetTexture(fillKernel, "tex", lr);
        superResShader.SetTexture(fillKernel, "Result", texture);
        superResShader.SetTexture(blendKernel, "Result", texture);
        shader.SetInt(numPrimitivesShaderProp, 0);
        shader.SetFloat("maxDist", 64f);
        shader.SetFloat("epsilon", 0.004f);
        shader.SetInt("maxSteps", 128);
        oldFov = Camera.fieldOfView;
    }

    private void Update()
    {
        shader.SetInt("reflectionCount", reflectionIterations);
        shader.SetFloat("reflectionIntensity", reflectionIntensity);
        shader.SetInt("aoIterations", aoIterations);
        shader.SetFloat("aoIntensity", aoIntensity);
        shader.SetFloat("aoSize", aoSize);
        
        if (fixLightToCamera)
        {
            mainLight.transform.position = Camera.transform.position;
        }
        
        if (transform.hasChanged || Math.Abs(oldFov - Camera.fieldOfView) > 0.01f)
        {
            shader.SetMatrix(camToWorldShaderProp, Camera.cameraToWorldMatrix);
            oldFov = Camera.fieldOfView;
            transform.hasChanged = false;
            RunComputeShader();
        }
    }

    private void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        if (!material)
        {
            material = new Material(renderShader) {hideFlags = HideFlags.HideAndDontSave, mainTexture = texture};
        }
        
        material.SetFloat("contrastThreshold", fxaaThreshold);
        material.SetFloat("relativeThreshold", relativeThreshold);
        material.SetFloat("subpixelBlending", subPixelBlending);
        material.SetTexture(bgTexShaderProp, src);
        
        Graphics.Blit(texture, dest, material);
    }

    private void RunComputeShader()
    {
        shader.Dispatch(tracerKernel, lr.width / 8, lr.height / 8, 1);
        superResShader.Dispatch(srKernel, texture.width / 8, texture.height / 8, 1);
        superResShader.Dispatch(fillKernel, texture.width / 8, texture.height / 8, 1);
        superResShader.Dispatch(blendKernel, texture.width / 8, texture.height / 8, 1);
    }

    private void UpdateShaderBuffer(ComputeBuffer buffer)
    {
        shader.SetInt(numPrimitivesShaderProp, buffer.count);
        shader.SetBuffer(tracerKernel, primitivesShaderProp, buffer);
        RunComputeShader();
    }

    private void UpdateLight()
    {
        shader.SetVector(lightPosShaderProp, mainLight.transform.position);
        shader.SetVector(lightColorShaderProp, mainLight.color);
        shader.SetFloat(lightIntensityShaderProp, mainLight.intensity);
        RunComputeShader();
    }
}
