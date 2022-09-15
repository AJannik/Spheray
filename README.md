# Spheray
Spheray is Project to provide a 3D Editor Software for working with SDF Models.
For Rendering Spheray uses a classical SphereTracing /Raymarching Algorithm

Currently there is no UI implemented. To use Spheray have have to use the Unity Editor Inspector.

# Quality and Performance
Best Quality is archieved when using "Scale Factor = 1" and "Aa Samples = 8". These settings are very demanding on your hardware!
For better performance you should start with increasing the Scale Factor. Scale Factor reduces the Render Dimensions via <code> ScreenPixelSize / ScaleFactor </code>
The lower quality render Image will be up scaled by AMDs FidelityFX FSR to your Screen size. While editing your SDFs it is advised to use the highst Scale Factor with lowest Aa Samples. You can always increase these Quality Settings after you edited your SDFs.
