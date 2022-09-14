using System;
using UnityEngine;

public class SphereTracingCompute : MonoBehaviour
{
    [SerializeField] private ShaderEventChannel shaderEventChannel;
    [SerializeField] private ComputeShader shader;
    [SerializeField] private ComputeShader superResShader;
    [SerializeField] private ComputeShader rcasShader;
    [SerializeField] private Shader renderShader;
    [SerializeField] private Light mainLight;
    [SerializeField] private bool fixLightToCamera;
    [Range(1.3f, 2f), Tooltip("Ultra Quality 1.3, Quality 1.5f, Balanced 1.7f, Performance 2f"), SerializeField] private float scaleFactor = 1.3f;
    [SerializeField, Range(0f, 2f)] private float sharpness = 0.2f;
    [SerializeField, Range(0.5f, 1f)] private float quality = 0.5f;
    [SerializeField, Range(0, 2)] private int reflectionIterations;
    [SerializeField, Range(0f, 1f)] private float reflectionIntensity;
    [SerializeField, Range(0f, 1f)] private float aoIntensity;
    [SerializeField, Range(0f, 1f)] private float antiAliasing = 1f;

    private int scaledPixelWidth = 0;
    private int scaledPixelHeight = 0;
    private ComputeBuffer easuParametersCb, rcasParametersCb;
    private Material material;
    private int tracerKernel;
    private int srKernel;
    private int initKernel;
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
        texture.Create();
        
        easuParametersCb = new ComputeBuffer(4, sizeof(uint) * 4);
        rcasParametersCb = new ComputeBuffer(1, sizeof(uint) * 4);
        
        tracerKernel = shader.FindKernel("CSMain");
        srKernel = superResShader.FindKernel("FsrUpSample");
        initKernel = superResShader.FindKernel("Init");
        material = new Material(renderShader) {hideFlags = HideFlags.HideAndDontSave, mainTexture = texture};
        oldFov = Camera.fieldOfView;
        oldAntiAliasing = antiAliasing;
        oldAoIntensity = aoIntensity;
        oldReflectionIntensity = reflectionIntensity;
        oldReflectionIterations = reflectionIterations;
        oldQuality = quality;
        
        material.SetFloat(subpixelBlendingShaderProp, antiAliasing);
        //shader.SetTexture(tracerKernel, resultShaderProp, lr);
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
        //superResShader.SetTexture(srKernel, "tex", lr);
        //superResShader.SetTexture(srKernel, "Result", texture);
        //superResShader.SetTexture(initKernel, "tex", lr);
        //superResShader.SetTexture(initKernel, "Result", texture);
        //superResShader.SetTexture(blendKernel, "Result", texture);
        //superResShader.SetFloat("quality", quality);
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

        //ScalableBufferManager.ResizeBuffers(1f / scaleFactor, 1f / scaleFactor);
        
        UpdateSettings();
        material.SetTexture(bgTexShaderProp, src);
        material.SetPass(0);

        Graphics.Blit(texture, dest, material);
    }

    private void RunComputeShader()
    {
        scaledPixelWidth = (int)(Camera.pixelWidth / scaleFactor);
        scaledPixelHeight = (int)(Camera.pixelHeight / scaleFactor);
        
        lr = new RenderTexture(scaledPixelWidth, scaledPixelHeight, 4) {enableRandomWrite = true};
        lr.Create();
        
        RenderTexture rt = new RenderTexture(texture.width, texture.height, 4) {enableRandomWrite = true};
        rt.Create();
        
        shader.SetTexture(tracerKernel, resultShaderProp, lr);
        
        superResShader.SetVector("easuViewportSize", new Vector4(Camera.pixelWidth, cam.pixelHeight));
        superResShader.SetVector("easuInputImageSize", new Vector4(Camera.pixelWidth, cam.pixelHeight));
        superResShader.SetVector("easuOutputSize", new Vector4(rt.width, rt.height, 1f / rt.width, 1f / rt.height));
        superResShader.SetBuffer(initKernel, "easuParameters", easuParametersCb);
        
        superResShader.Dispatch(initKernel, 1, 1, 1); //init

        superResShader.SetTexture(srKernel, "inputTex", lr);
        superResShader.SetTexture(srKernel, "outputTex", rt);
        
        shader.Dispatch(tracerKernel, lr.width / 8, lr.height / 8, 1);
        
        const int ThreadGroupWorkRegionRim = 16;
        int dispatchX = (rt.width + ThreadGroupWorkRegionRim - 1) / ThreadGroupWorkRegionRim;
        int dispatchY = (rt.height + ThreadGroupWorkRegionRim - 1) / ThreadGroupWorkRegionRim;

        superResShader.SetBuffer(srKernel, "easuParameters", easuParametersCb);
        superResShader.Dispatch(srKernel, dispatchX, dispatchY, 1); //main
        
        rcasShader.SetBuffer(rcasShader.FindKernel("Init"), "rcasParameters", rcasParametersCb);
        rcasShader.SetFloat("rcasScale", sharpness);
        rcasShader.Dispatch(rcasShader.FindKernel("Init"), 1, 1, 1); //init

        int k = rcasShader.FindKernel("Sharpen");
        rcasShader.SetBuffer(k, "rcasParameters", rcasParametersCb);
        rcasShader.SetTexture(k, "inputTex", rt);
        rcasShader.SetTexture(k, "outputTex", texture);

        rcasShader.Dispatch(k, dispatchX, dispatchY, 1); //main
        
        lr.Release();
        rt.Release();
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
            superResShader.SetTexture(initKernel, "tex", lr);
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

    private void OnDestroy()
    {
        easuParametersCb?.Release();
        rcasParametersCb?.Release();
    }
}