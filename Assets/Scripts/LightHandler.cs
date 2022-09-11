using System;
using UnityEngine;

public class LightHandler : MonoBehaviour
{
    [SerializeField] private ShaderEventChannel shaderEventChannel;
    private Light lightComponent;
    private Color oldColor;
    private float oldIntensity;

    private void Awake()
    {
        lightComponent = GetComponent<Light>();
    }

    private void Start()
    {
        oldColor = lightComponent.color;
        oldIntensity = lightComponent.intensity;
    }

    private void Update()
    {
        if (oldColor != lightComponent.color || transform.hasChanged || Math.Abs(lightComponent.intensity - oldIntensity) > 0.1f)
        {
            oldColor = lightComponent.color;
            oldIntensity = lightComponent.intensity;
            transform.hasChanged = false;
            shaderEventChannel.RaiseLightChanged();
        }
    }
}