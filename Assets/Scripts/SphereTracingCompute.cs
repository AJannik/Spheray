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
    [SerializeField, Range(0.5f, 1f)] private float quality = 0.5f;
    [SerializeField, Range(0, 2)] private int reflectionIterations;
    [SerializeField, Range(0f, 1f)] private float reflectionIntensity;
    [SerializeField, Range(0f, 1f)] private float aoIntensity;
    [SerializeField, Range(0f, 1f)] private float antiAliasing = 1f;

    private Material material;
    private int tracerKernel;
    private int srKernel;
    private int fillKernel;
    private int blendKernel;
    private RenderTexture texture;
    private RenderTexture lr;
    private Camera cam;
    private float oldFov;
    private int oldReflectionIterations;
    private float oldReflectionIntensity;
    private float oldAoIntensity;
    private float oldAntiAliasing;
    private float oldQuality;
    private readonly int camToWorldShaderProp = Shader.PropertyToID("camToWorld");
    private readonly int lightPosShaderProp = Shader.PropertyToID("lightPos");
    private readonly int lightColorShaderProp = Shader.PropertyToID("lightColor");
    private readonly int lightIntensityShaderProp = Shader.PropertyToID("lightIntensity");
    private readonly int resultShaderProp = Shader.PropertyToID("Result");
    private readonly int primitivesShaderProp = Shader.PropertyToID("primitives");
    private readonly int numPrimitivesShaderProp = Shader.PropertyToID("numPrimitives");
    private readonly int bgTexShaderProp = Shader.PropertyToID("_BgTex");
    private readonly int subpixelBlendingShaderProp = Shader.PropertyToID("subpixelBlending");

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
        lr = new RenderTexture((int) (1920 * quality), (int) (1080 * quality), 8) {enableRandomWrite = true};
        lr.Create();
        texture.Create();
        
        tracerKernel = shader.FindKernel("CSMain");
        srKernel = superResShader.FindKernel("SuperResolution");
        fillKernel = superResShader.FindKernel("Fill");
        blendKernel = superResShader.FindKernel("Blend");
        material = new Material(renderShader) {hideFlags = HideFlags.HideAndDontSave, mainTexture = texture};
        oldFov = Camera.fieldOfView;
        oldAntiAliasing = antiAliasing;
        oldAoIntensity = aoIntensity;
        oldReflectionIntensity = reflectionIntensity;
        oldReflectionIterations = reflectionIterations;
        oldQuality = quality;
        
        material.SetFloat(subpixelBlendingShaderProp, antiAliasing);
        shader.SetTexture(tracerKernel, resultShaderProp, lr);
        shader.SetInt(numPrimitivesShaderProp, 0);
        shader.SetFloat("maxDist", 64f);
        shader.SetFloat("epsilon", 0.004f);
        shader.SetInt("maxSteps", 128);
        shader.SetMatrix(camToWorldShaderProp, Camera.cameraToWorldMatrix);
        shader.SetInt("reflectionCount", reflectionIterations);
        shader.SetFloat("reflectionIntensity", reflectionIntensity);
        shader.SetFloat("aoIntensity", aoIntensity);
        shader.SetVector(lightPosShaderProp, mainLight.transform.position);
        shader.SetVector(lightColorShaderProp, mainLight.color);
        shader.SetFloat(lightIntensityShaderProp, mainLight.intensity);
        superResShader.SetTexture(srKernel, "tex", lr);
        superResShader.SetTexture(srKernel, "Result", texture);
        superResShader.SetTexture(fillKernel, "tex", lr);
        superResShader.SetTexture(fillKernel, "Result", texture);
        superResShader.SetTexture(blendKernel, "Result", texture);
        superResShader.SetFloat("quality", quality);
    }

    private void Update()
    {
        if (fixLightToCamera)
        {
            mainLight.transform.position = Camera.transform.position;
        }
    }

    private void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        if (Math.Abs(oldAntiAliasing - antiAliasing) > 0.05)
        {
            material.SetFloat(subpixelBlendingShaderProp, antiAliasing);
            oldAntiAliasing = antiAliasing;
        }

        UpdateSettings();
        material.SetTexture(bgTexShaderProp, src);

        Graphics.Blit(texture, dest, material);
    }

    private void RunComputeShader()
    {
        shader.Dispatch(tracerKernel, lr.width / 8, lr.height / 8, 1);
        superResShader.Dispatch(srKernel, texture.width / 8, texture.height / 8, 1);
        if (quality < 0.9)
        {
            superResShader.Dispatch(fillKernel, texture.width / 8, texture.height / 8, 1);
            superResShader.Dispatch(blendKernel, texture.width / 8, texture.height / 8, 1);
        }
    }

    private void UpdateSettings(bool update = false)
    {
        if (oldReflectionIterations != reflectionIterations ||
            Math.Abs(oldReflectionIntensity - reflectionIntensity) > 0.01)
        {
            oldReflectionIterations = reflectionIterations;
            oldReflectionIntensity = reflectionIntensity;
            shader.SetInt("reflectionCount", reflectionIterations);
            shader.SetFloat("reflectionIntensity", reflectionIntensity);
            update = true;
        }

        if (Math.Abs(oldAoIntensity - aoIntensity) > 0.01)
        {
            oldAoIntensity = aoIntensity;
            shader.SetFloat("aoIntensity", aoIntensity);
            update = true;
        }

        if (transform.hasChanged || Math.Abs(oldFov - Camera.fieldOfView) > 0.01f)
        {
            oldFov = Camera.fieldOfView;
            transform.hasChanged = false;
            shader.SetMatrix(camToWorldShaderProp, Camera.cameraToWorldMatrix);
            update = true;
        }

        if (Math.Abs(quality - oldQuality) > 0.01)
        {
            lr = new RenderTexture((int) (1920 * quality), (int) (1080 * quality), 8) {enableRandomWrite = true};
            lr.Create();
            shader.SetTexture(tracerKernel, resultShaderProp, lr);
            superResShader.SetTexture(srKernel, "tex", lr);
            superResShader.SetTexture(fillKernel, "tex", lr);
            superResShader.SetFloat("quality", quality);
            oldQuality = quality;
            update = true;
        }

        if (update)
        {
            RunComputeShader();
        }
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