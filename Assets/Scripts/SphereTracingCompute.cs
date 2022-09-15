using System;
using UnityEngine;

public class SphereTracingCompute : MonoBehaviour
{
    [SerializeField] private ShaderEventChannel shaderEventChannel;
    [SerializeField] private ComputeShader tracerShader;
    [SerializeField] private ComputeShader easuShader;
    [SerializeField] private ComputeShader rcasShader;
    [SerializeField] private Light mainLight;
    [SerializeField] private bool fixLightToCamera;
    [Range(1f, 2f), Tooltip("Ultra Quality 1.3, Quality 1.5f, Balanced 1.7f, Performance 2f"), SerializeField] private float scaleFactor = 1.3f;
    [SerializeField, Range(0f, 2f)] private float sharpness = 0.2f;
    [SerializeField, Range(0, 2)] private int reflectionIterations;
    [SerializeField, Range(0f, 1f)] private float reflectionIntensity;
    [SerializeField, Range(0f, 1f)] private float aoIntensity;
    [SerializeField, Range(1, 8)] private int aaSamples;

    private int scaledPixelWidth = 0;
    private int scaledPixelHeight = 0;
    private ComputeBuffer easuParametersCb, rcasParametersCb;
    private int tracerKernel;
    private int easuKernelInit, easuKernel, rcasKernelInit, rcasKernel;
    private RenderTexture texture;
    private RenderTexture easuTex, rcasTex;
    private Camera cam;
    private float oldFov;
    private int oldReflectionIterations;
    private float oldReflectionIntensity;
    private float oldAoIntensity;
    private int oldAaSamples;
    private float oldScaleFactor;
    private readonly int camToWorldShaderProp = Shader.PropertyToID("camToWorld");
    private readonly int lightPosShaderProp = Shader.PropertyToID("lightPos");
    private readonly int lightColorShaderProp = Shader.PropertyToID("lightColor");
    private readonly int lightIntensityShaderProp = Shader.PropertyToID("lightIntensity");
    private readonly int resultShaderProp = Shader.PropertyToID("Result");
    private readonly int primitivesShaderProp = Shader.PropertyToID("primitives");
    private readonly int numPrimitivesShaderProp = Shader.PropertyToID("numPrimitives");
    private readonly int aaSamplesShaderProp = Shader.PropertyToID("aaSamples");

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
        scaledPixelWidth = (int)(Camera.pixelWidth / scaleFactor);
        scaledPixelHeight = (int)(Camera.pixelHeight / scaleFactor);
        texture = new RenderTexture(scaledPixelWidth, scaledPixelHeight, 0) {enableRandomWrite = true};
        texture.Create();

        easuParametersCb = new ComputeBuffer(4, sizeof(uint) * 4);
        rcasParametersCb = new ComputeBuffer(1, sizeof(uint) * 4);
        
        easuKernelInit = easuShader.FindKernel("Init");
        easuKernel = easuShader.FindKernel("FsrUpSample");
        rcasKernelInit = rcasShader.FindKernel("Init");
        rcasKernel = rcasShader.FindKernel("Sharpen");

        tracerKernel = tracerShader.FindKernel("CSMain");
        oldFov = Camera.fieldOfView;
        oldAoIntensity = aoIntensity;
        oldReflectionIntensity = reflectionIntensity;
        oldReflectionIterations = reflectionIterations;
        
        tracerShader.SetInt(numPrimitivesShaderProp, 0);
        tracerShader.SetFloat("maxDist", 64f);
        tracerShader.SetFloat("epsilon", 0.004f);
        tracerShader.SetInt("maxSteps", 128);
        tracerShader.SetInt(aaSamplesShaderProp, aaSamples);
        tracerShader.SetMatrix(camToWorldShaderProp, Camera.cameraToWorldMatrix);
        tracerShader.SetInt("reflectionCount", reflectionIterations);
        tracerShader.SetFloat("reflectionIntensity", reflectionIntensity);
        tracerShader.SetFloat("aoIntensity", aoIntensity);
        tracerShader.SetVector(lightPosShaderProp, mainLight.transform.position);
        tracerShader.SetVector(lightColorShaderProp, mainLight.color);
        tracerShader.SetFloat(lightIntensityShaderProp, mainLight.intensity);
        tracerShader.SetTexture(tracerKernel, resultShaderProp, texture);
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
        if (!easuTex)
        {
            easuTex = new RenderTexture(Camera.pixelWidth, Camera.pixelHeight, 0, src.format,
                RenderTextureReadWrite.sRGB) {enableRandomWrite = true};
            easuTex.Create();
            
            easuShader.SetVector("easuViewportSize", new Vector4(texture.width, texture.height));
            easuShader.SetVector("easuInputImageSize", new Vector4(texture.width, texture.height));
            easuShader.SetVector("easuOutputSize", new Vector4(easuTex.width, easuTex.height, 1f / easuTex.width, 1f / easuTex.height));
            easuShader.SetBuffer(easuKernelInit, "easuParameters", easuParametersCb);
            easuShader.SetBuffer(easuKernel, "easuParameters", easuParametersCb);
            easuShader.SetTexture(easuKernel, "inputTex", texture);
            easuShader.SetTexture(easuKernel, "outputTex", easuTex);
        }
        
        UpdateSettings();
        
        easuShader.Dispatch(easuKernelInit, 1, 1, 1);

        if (!rcasTex)
        {
            rcasTex = new RenderTexture(Camera.pixelWidth, Camera.pixelHeight, 0, src.format, 
                RenderTextureReadWrite.sRGB) {enableRandomWrite = true};
            rcasTex.Create();
            
            rcasShader.SetBuffer(rcasKernelInit, "rcasParameters", rcasParametersCb);
            rcasShader.SetBuffer(rcasKernel, "rcasParameters", rcasParametersCb);
            rcasShader.SetTexture(rcasKernel, "outputTex", rcasTex);
            rcasShader.SetTexture(rcasKernel, "inputTex", easuTex);
        }

        const int threadGroupWorkRegionRim = 16;
        int dispatchX = (easuTex.width + threadGroupWorkRegionRim - 1) / threadGroupWorkRegionRim;
        int dispatchY = (easuTex.height + threadGroupWorkRegionRim - 1) / threadGroupWorkRegionRim;

        easuShader.Dispatch(easuKernel, dispatchX, dispatchY, 1);

        rcasShader.SetFloat("rcasScale", sharpness);
        rcasShader.Dispatch(rcasKernelInit, 1, 1, 1);
        rcasShader.Dispatch(rcasKernel, dispatchX, dispatchY, 1);

        Graphics.Blit(rcasTex, dest);
    }

    private void RunComputeShader()
    {
        tracerShader.Dispatch(tracerKernel, texture.width / 8, texture.height / 8, 1);
    }

    private void UpdateSettings(bool update = false)
    {
        if (oldReflectionIterations != reflectionIterations ||
            Math.Abs(oldReflectionIntensity - reflectionIntensity) > 0.01)
        {
            oldReflectionIterations = reflectionIterations;
            oldReflectionIntensity = reflectionIntensity;
            tracerShader.SetInt("reflectionCount", reflectionIterations);
            tracerShader.SetFloat("reflectionIntensity", reflectionIntensity);
            update = true;
        }

        if (Math.Abs(oldAoIntensity - aoIntensity) > 0.01)
        {
            oldAoIntensity = aoIntensity;
            tracerShader.SetFloat("aoIntensity", aoIntensity);
            update = true;
        }

        if (transform.hasChanged || Math.Abs(oldFov - Camera.fieldOfView) > 0.01f)
        {
            oldFov = Camera.fieldOfView;
            transform.hasChanged = false;
            tracerShader.SetMatrix(camToWorldShaderProp, Camera.cameraToWorldMatrix);
            update = true;
        }

        if (aaSamples != oldAaSamples)
        {
            oldAaSamples = aaSamples;
            tracerShader.SetInt(aaSamplesShaderProp, aaSamples);
            update = true;
        }

        if (Math.Abs(oldScaleFactor - scaleFactor) > 0.01)
        {
            oldScaleFactor = scaleFactor;
            scaledPixelWidth = (int)(Camera.pixelWidth / scaleFactor);
            scaledPixelHeight = (int)(Camera.pixelHeight / scaleFactor);
            if (texture)
            {
                texture.Release();
            }
            
            texture = new RenderTexture(scaledPixelWidth, scaledPixelHeight, 0) {enableRandomWrite = true};
            texture.Create();
            tracerShader.SetTexture(tracerKernel, resultShaderProp, texture);
            
            easuShader.SetVector("easuViewportSize", new Vector4(texture.width, texture.height));
            easuShader.SetVector("easuInputImageSize", new Vector4(texture.width, texture.height));
            easuShader.SetTexture(easuKernel, "inputTex", texture);
            update = true;
        }

        if (update)
        {
            RunComputeShader();
        }
    }

    private void UpdateShaderBuffer(ComputeBuffer buffer)
    {
        tracerShader.SetInt(numPrimitivesShaderProp, buffer.count);
        tracerShader.SetBuffer(tracerKernel, primitivesShaderProp, buffer);
        RunComputeShader();
    }

    private void UpdateLight()
    {
        tracerShader.SetVector(lightPosShaderProp, mainLight.transform.position);
        tracerShader.SetVector(lightColorShaderProp, mainLight.color);
        tracerShader.SetFloat(lightIntensityShaderProp, mainLight.intensity);
        RunComputeShader();
    }
    
    private void OnDestroy()
    {
        easuParametersCb?.Release();
        rcasParametersCb?.Release();
        easuTex.Release();
        rcasTex.Release();
        texture.Release();
    }
}