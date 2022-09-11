Shader "SphereTracing/SDF Editor"
{
    Properties
    {
        epsilon ("Epsilon", float) = 0.004
        maxSteps ("Max Steps", int) = 128
        maxDist ("Max Distance", float) = 64
        aoStepSize("AO Step Size", float) = 0.8
    }
    SubShader
    {
        // No culling or depth
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct Primitive
            {
                float3 pos;
                float3 size;
                float bevel;
                float4x4 rotationMatrix;
                int sdfOperation;
                int parentIndex;
                int type;
            };

            StructuredBuffer<Primitive> primitives;
            int numPrimitives;
            sampler2D mainTex;
            uniform float4x4 camFrustum, camToWorld;
            float epsilon; // stripe artifacts if too small
            int maxSteps;
            float maxDist;
            float3 lightPos;
            float3 lightColor;
            int aaSamples;
            int aoIterations;
            float aoStepSize;
            float aoIntensity;

            struct Ray
            {
                float3 origin; // origin of ray
                float3 dir; // direction of ray (unit length assumed)
            };

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float3 ray : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                const half index = v.vertex.z;
                v.vertex.z = 0;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;

                o.ray = camFrustum[(int)index].xyz;
                o.ray /= abs(o.ray.z);
                o.ray = mul(camToWorld, o.ray);

                return o;
            }

            float3 transform(float3 p, Primitive primitive)
            {
                p -= primitive.pos;
                return mul(float4(p, 1), primitive.rotationMatrix).xyz;
            }

            float4 applyFog(float4 color, float dist, float fallOff, float4 fogColor, Ray cameraRay, float3 toLight,
                            float4 lightColor)
            {
                const float fogAmount = 1 - exp(-dist * fallOff);
                const float sunAmount = max(0, dot(cameraRay.dir, toLight));
                fogColor = lerp(fogColor, lightColor, pow(sunAmount, 32));
                return lerp(color, fogColor, fogAmount);
            }

            float sdSphere(float3 p, float r)
            {
                return length(p) - r;
            }

            float sdPlane(float3 p, float3 normal, float d)
            {
                return dot(p, normal) - d;
            }

            float sdCylinder(float3 p, float3 c)
            {
                return length(p.xz - c.xy) - c.z;
            }

            float sdBox(float3 p, float3 b)
            {
                const float3 d = abs(p) - b;
                return min(max(d.x, max(d.y, d.z)), 0.0) + length(max(d, 0.0));
            }

            float sdEllipsoid(float3 p, float3 r)
            {
                const float k0 = length(p / r);
                const float k1 = length(p / (r * r));
                return k0 * (k0 - 1.0) / k1;
            }

            float sdRoundedCylinder(float3 p, float ra, float rb, float h)
            {
                const float2 d = float2(length(p.xz) - 2.0 * ra + rb, abs(p.y) - h);
                return min(max(d.x, d.y), 0.0) + length(max(d, 0.0)) - rb;
            }

            float sdHexPrism(float3 p, float3 h)
            {
                const float3 k = float3(-0.8660254, 0.5, 0.57735);
                p = abs(p);
                p.xy -= 2.0 * min(dot(k.xy, p.xy), 0.0) * k.xy;
                const float2 d = float2(
                    length(p.xy - float2(clamp(p.x, -k.z * h.x, k.z * h.x), h.x)) * sign(p.y - h.x),
                    p.z - h.y);
                return min(max(d.x, d.y), 0.0) + length(max(d, 0.0));
            }

            float sdCapsule(float3 p, float3 a, float3 b, float r)
            {
                const float3 pa = p - a;
                const float3 ba = b - a;
                const float h = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0);
                return length(pa - ba * h) - r;
            }

            float sdRoundBox(float3 p, float3 b, float r)
            {
                const float3 q = abs(p) - b;
                return length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0) - r;
            }

            float smin(float a, float b, float k)
            {
                const float h = clamp(0.5 + 0.5 * (b - a) / k, 0, 1);
                return lerp(b, a, h) - k * h * (1 - h);
            }

            float smax(float a, float b, float k)
            {
                k = -k;
                const float h = clamp(0.5 + 0.5 * (b - a) / k, 0, 1);
                return lerp(b, a, h) - k * h * (1 - h);
            }

            float intersectSDF(float distA, float distB, float smooth)
            {
                return smax(distA, distB, smooth);
            }

            float unionSDF(float distA, float distB, float smooth)
            {
                return smin(distA, distB, smooth);
            }

            float differenceSDF(float distA, float distB, float smooth)
            {
                return smax(distA, -distB, smooth);
            }

            float3 opRepeat(float3 p, float3 interval)
            {
                return fmod(p + 0.5 * interval, interval) - 0.5 * interval;
            }

            fixed4 materialColor(float3 p)
            {
                return float4(0.8, 0.2, 0.2, 1);
            }

            float getPrimitiveSdf(float3 p, Primitive primitive)
            {
                switch (primitive.type)
                {
                case 0:
                    return sdSphere(p, primitive.size.x);
                case 1:
                    return sdBox(p, primitive.size);
                default:
                    return -1;
                }
            }

            float getSDF(float parent, float current, int index)
            {
                switch (primitives[index].sdfOperation)
                {
                case 0:
                    return unionSDF(parent, current, 0.03);
                case 1:
                    return differenceSDF(parent, current, 0.03);
                case 2:
                    return intersectSDF(parent, current, 0.03);
                default:
                    return current;
                }
            }

            float distFunc(float3 p)
            {
                if (numPrimitives < 1)
                {
                    return -1;
                }

                float distFields[20];

                for (int i = 0; i < numPrimitives; i++)
                {
                    distFields[i] = getPrimitiveSdf(transform(p, primitives[i]), primitives[i]);
                }

                for (int ind = numPrimitives - 1; ind > 0; ind--)
                {
                    distFields[primitives[ind].parentIndex] = getSDF(distFields[primitives[ind].parentIndex],
                                                                     distFields[ind], ind);
                }

                return distFields[0];
            }

            float3 calcNormal(float3 p)
            {
                const float2 k = float2(1, -1);
                return normalize(
                    k.xyy * distFunc(p + k.xyy * epsilon) +
                    k.yyx * distFunc(p + k.yyx * epsilon) +
                    k.yxy * distFunc(p + k.yxy * epsilon) +
                    k.xxx * distFunc(p + k.xxx * epsilon));
            }


            float rayCast(Ray ray, float t)
            {
                for (int i = 0; i < maxSteps && t < maxDist; i++)
                {
                    const float3 p = ray.origin + t * ray.dir;
                    const float dist = distFunc(p);
                    t += dist;
                    if (epsilon > dist) return t;
                }

                return -1;
            }


            bool hit(float t)
            {
                return -0.5 < t;
            }

            float ambientOcclusion(float3 p, float3 normal)
            {
                float ao = 0;
                for (int i = 1; i <= aoIterations; i++)
                {
                    const float dist = aoStepSize * i;
                    ao += max(0, (dist - distFunc(p + normal * dist)) / dist);
                }

                return 1 - ao * aoIntensity;
            }

            float3 shadeLocal(float3 p, float3 normal)
            {
                const float3 toLight = normalize(lightPos);
                Ray ray;
                ray.origin = p;
                ray.dir = toLight;

                float ao = ambientOcclusion(p, normal);

                if (hit(rayCast(ray, 0.1)))
                {
                    // shadow
                    return 0.1 * lightColor * materialColor(p) * dot(normal, toLight) * ao;
                }

                return lightColor * materialColor(p) * dot(normal, toLight) * ao;
            }

            float3 traceReflected(Ray ray)
            {
                const float t = rayCast(ray, epsilon);
                if (!hit(t))
                {
                    return float4(0, 0, 0, 0);
                }

                const float3 p = ray.origin + t * ray.dir;
                const float3 normal = calcNormal(p);

                return shadeLocal(p, normal);
            }

            float4 trace(Ray ray)
            {
                const float t = rayCast(ray, epsilon);
                if (!hit(t))
                {
                    return float4(0, 0, 0, 0);
                }

                const float3 p = ray.origin + t * ray.dir;
                const float3 normal = calcNormal(p);

                // reflection
                /*
                if (sdPlane(p, float3(0, 1, 0), -1.5) < 0.001)
                {
                    const float3 r = reflect(ray.dir, normal);
                    Ray newRay;
                    newRay.origin = p + epsilon * normal;
                    newRay.dir = r;
                    //return traceReflected(newRay);
                }
                */

                return float4(shadeLocal(p, normal), 1);
            }

            float2 rand2(float2 uv)
            {
                float noiseX = (frac(sin(dot(uv, float2(12.9898, 78.233))) * 43758.5453));
                float noiseY = (frac(sin(dot(uv, float2(12.9898, 78.233) * 2.0)) * 43758.5453));
                return float2(noiseX, noiseY);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                //camera setup
                Ray cameraRay;
                cameraRay.origin = _WorldSpaceCameraPos;
                cameraRay.dir = normalize(i.ray.xyz);

                const fixed3 background = tex2D(mainTex, i.uv);
                fixed4 color = trace(cameraRay);
                for (int aa = 1; aa < aaSamples; aa++)
                {
                    Ray ray;
                    ray.origin = _WorldSpaceCameraPos + float4(rand2(i.uv), 0, 0) * 0.001;
                    ray.dir = normalize(i.ray.xyz + float4(rand2(i.uv), 0, 0) * 0.0001);
                    color += trace(ray);
                }

                color /= float(aaSamples);

                //color = applyFog(color, t, .5, vec3(.1, .5, .8), cameraRay, toLight, lightColor);
                return fixed4(background * (1.0 - color.w) + color.xyz * color.w, 1.0);
            }
            ENDCG
        }
    }
}