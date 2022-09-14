using System;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

[Serializable]
[PostProcess(typeof(AmdFidelityFxFsrEffectRenderer), PostProcessEvent.AfterStack,
    "Spheray/AMD Fidelity FX/FSR")]
public sealed class AmdFidelityFxFsrEffect : PostProcessEffectSettings
{
    [Header("FSR Compute Shaders")] public ComputeShader computeShaderEasu;

    public ComputeShader computeShaderRcas;

    [Header("Edge Adaptive Scale Upsampling")]
    [Range(1.3f, 2f), Tooltip("Ultra Quality 1.3, Quality 1.5, Balanced 1.7, Performance 2")]
    public FloatParameter scaleFactor = new FloatParameter {value = 1.3f};

    [Header("Robust Contrast Adaptive Sharpen")]
    public BoolParameter sharpening = new BoolParameter {value = true};

    [Range(0f, 2f), Tooltip("0 = sharpest, 2 = less sharp")]
    public FloatParameter sharpness = new FloatParameter {value = 0.2f};
}

public sealed class AmdFidelityFxFsrEffectRenderer : PostProcessEffectRenderer<AmdFidelityFxFsrEffect>
{
    // Robust Contrast Adaptive Sharpening
    private readonly int rcasScaleShaderProp = Shader.PropertyToID("rcasScale");
    private readonly int rcasParametersShaderProp = Shader.PropertyToID("rcasParameters");

    // Edge Adaptive Spatial Upsampling
    private readonly int easuViewportSizeShaderProp = Shader.PropertyToID("easuViewportSize");
    private readonly int easuInputImageSizeShaderProp = Shader.PropertyToID("easuInputImageSize");
    private readonly int easuOutputSizeShaderProp = Shader.PropertyToID("easuOutputSize");
    private readonly int easuParametersShaderProp = Shader.PropertyToID("easuParameters");

    private readonly int inputTexShaderProp = Shader.PropertyToID("inputTex");
    private readonly int outputTexShaderProp = Shader.PropertyToID("outputTex");
    private int easuInitKernel, easuKernel, rcasInitKernel, rcasKernel;

    private RenderTexture easuOutput, rcasOutput;
    private ComputeBuffer easuParametersCb, rcasParametersCb;

    //private Camera cam;
    private int scaledPixelWidth;
    private int scaledPixelHeight;
    private bool isRcasSetup;

    public override void Release()
    {
        if (easuOutput)
        {
            easuOutput.Release();
            easuOutput = null;
        }

        if (easuParametersCb != null)
        {
            easuParametersCb.Dispose();
            easuParametersCb = null;
        }

        if (rcasOutput)
        {
            rcasOutput.Release();
            rcasOutput = null;
        }

        if (rcasParametersCb != null)
        {
            rcasParametersCb.Dispose();
            rcasParametersCb = null;
        }

        ScalableBufferManager.ResizeBuffers(1f, 1f);

        isRcasSetup = false;
        base.Release();
    }

    public override void Init()
    {
        settings.computeShaderEasu = (ComputeShader) Resources.Load("EASU");
        settings.computeShaderRcas = (ComputeShader) Resources.Load("RCAS");

        easuParametersCb = new ComputeBuffer(4, sizeof(uint) * 4);
        rcasParametersCb = new ComputeBuffer(1, sizeof(uint) * 4);

        easuInitKernel = settings.computeShaderEasu.FindKernel("Init");
        easuKernel = settings.computeShaderEasu.FindKernel("FsrUpSample");
        rcasInitKernel = settings.computeShaderRcas.FindKernel("Init");
        rcasKernel = settings.computeShaderRcas.FindKernel("Sharpen");

        base.Init();
    }

    public override void Render(PostProcessRenderContext context)
    {
        if (easuOutput == null || scaledPixelWidth != context.camera.scaledPixelWidth ||
            scaledPixelHeight != context.camera.scaledPixelHeight || isRcasSetup == false && settings.sharpening)
        {
            scaledPixelWidth = context.camera.scaledPixelWidth;
            scaledPixelHeight = context.camera.scaledPixelHeight;
            float normalizedScale = (settings.scaleFactor - 1.3f) / (2f - 1.3f);
            //Ultra Quality -0.38f, Quality -0.58f, Balanced -0.79f, Performance -1f
            float mipBias = -Mathf.Lerp(0.38f, 1f, normalizedScale);

            //EASU
            if (easuOutput)
            {
                easuOutput.Release();
            }

            easuOutput = new RenderTexture(context.camera.pixelWidth, context.camera.pixelHeight, 0,
                context.sourceFormat, RenderTextureReadWrite.sRGB) {enableRandomWrite = true, mipMapBias = mipBias};
            easuOutput.Create();

            //RCAS
            if (settings.sharpening)
            {
                if (rcasOutput)
                {
                    rcasOutput.Release();
                }

                rcasOutput = new RenderTexture(context.camera.pixelWidth, context.camera.pixelHeight, 0,
                    context.sourceFormat, RenderTextureReadWrite.sRGB) {enableRandomWrite = true};
                rcasOutput.Create();
            }
        }

        //EASU
        context.command.SetComputeVectorParam(settings.computeShaderEasu, easuViewportSizeShaderProp,
            new Vector4(context.camera.pixelWidth, context.camera.pixelHeight));
        context.command.SetComputeVectorParam(settings.computeShaderEasu, easuInputImageSizeShaderProp,
            new Vector4(context.camera.scaledPixelWidth, context.camera.scaledPixelHeight));
        context.command.SetComputeVectorParam(settings.computeShaderEasu, easuOutputSizeShaderProp,
            new Vector4(easuOutput.width, easuOutput.height, 1f / easuOutput.width, 1f / easuOutput.height));
        context.command.SetComputeBufferParam(settings.computeShaderEasu, 1, easuParametersShaderProp, easuParametersCb);

        context.command.DispatchCompute(settings.computeShaderEasu, easuInitKernel, 1, 1, 1);

        context.command.SetComputeTextureParam(settings.computeShaderEasu, 0, inputTexShaderProp, context.source);
        context.command.SetComputeTextureParam(settings.computeShaderEasu, 0, outputTexShaderProp, easuOutput);

        const int threadGroupWorkRegionRim = 16;
        int dispatchX = (easuOutput.width + threadGroupWorkRegionRim - 1) / threadGroupWorkRegionRim;
        int dispatchY = (easuOutput.height + threadGroupWorkRegionRim - 1) / threadGroupWorkRegionRim;

        context.command.SetComputeBufferParam(settings.computeShaderEasu, easuKernel, easuParametersShaderProp, easuParametersCb);
        context.command.DispatchCompute(settings.computeShaderEasu, easuKernel, dispatchX, dispatchY, 1);

        //RCAS
        if (settings.sharpening)
        {
            context.command.SetComputeBufferParam(settings.computeShaderRcas, rcasInitKernel, rcasParametersShaderProp, rcasParametersCb);
            context.command.SetComputeFloatParam(settings.computeShaderRcas, rcasScaleShaderProp, settings.sharpness);
            context.command.DispatchCompute(settings.computeShaderRcas, rcasInitKernel, 1, 1, 1);

            context.command.SetComputeBufferParam(settings.computeShaderRcas, rcasKernel, rcasParametersShaderProp, rcasParametersCb);
            context.command.SetComputeTextureParam(settings.computeShaderRcas, rcasKernel, inputTexShaderProp, easuOutput);
            context.command.SetComputeTextureParam(settings.computeShaderRcas, rcasKernel, outputTexShaderProp, rcasOutput);

            context.command.DispatchCompute(settings.computeShaderRcas, rcasKernel, dispatchX, dispatchY, 1);
        }

        context.command.Blit(settings.sharpening ? rcasOutput : easuOutput, context.destination);
    }
}