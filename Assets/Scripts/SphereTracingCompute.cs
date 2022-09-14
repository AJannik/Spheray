using System;
using UnityEngine;

public class SphereTracingCompute : MonoBehaviour
{
    [SerializeField] private ShaderEventChannel shaderEventChannel;
    [SerializeField] private ComputeShader shader;
    [SerializeField] private Shader renderShader;
    [SerializeField] private Light mainLight;
    [SerializeField] private bool fixLightToCamera;
    [Range(1.3f, 2f), Tooltip("Ultra Quality 1.3, Quality 1.5f, Balanced 1.7f, Performance 2f"), SerializeField] private float scaleFactor = 1.3f;
    [SerializeField, Range(0, 2)] private int reflectionIterations;
    [SerializeField, Range(0f, 1f)] private float reflectionIntensity;
    [SerializeField, Range(0f, 1f)] private float aoIntensity;

    private Material material;
    private int tracerKernel;
    private RenderTexture texture;
    private Camera cam;
    private float oldFov;
    private int oldReflectionIterations;
    private float oldReflectionIntensity;
    private float oldAoIntensity;
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
        texture = new RenderTexture((int) (1920 / scaleFactor), (int) (1080 / scaleFactor), 16) {enableRandomWrite = true};
        texture.Create();

        tracerKernel = shader.FindKernel("CSMain");
        material = new Material(renderShader) {hideFlags = HideFlags.HideAndDontSave, mainTexture = texture};
        oldFov = Camera.fieldOfView;
        oldAoIntensity = aoIntensity;
        oldReflectionIntensity = reflectionIntensity;
        oldReflectionIterations = reflectionIterations;
        
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
        shader.SetTexture(tracerKernel, resultShaderProp, texture);
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
        ScalableBufferManager.ResizeBuffers(1f / scaleFactor, 1f / scaleFactor);

        UpdateSettings();
        //material.SetTexture(bgTexShaderProp, src);
        //material.SetPass(0);

        Graphics.Blit(texture, dest);
        //ScalableBufferManager.ResizeBuffers(1f, 1f);
    }

    private void RunComputeShader()
    {
        shader.Dispatch(tracerKernel, texture.width / 8, texture.height / 8, 1);
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