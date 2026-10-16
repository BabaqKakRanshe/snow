using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SkyIsland.EditorTools
{
    /// <summary>layout.emitters -> ParticleSystems with presets per type (smoke, steam, spray, dust, embers) and the
    /// SkyIsland/Smoke shader (alpha-blended puffs, additive embers).</summary>
    public static partial class SkyIslandBuilder
    {
        /// <summary>Particle texture (RGBA, alpha = density); the SkyIsland copy is used when the location has none.</summary>
        public const string SmokeTexture = "T_SmokePuff.png";

        /// <summary>One emitter type at scale 1 / rate 1: sizes in metres, speeds in m/s, rate per second, colour linear (alpha = opacity).</summary>
        struct EmitterPreset
        {
            public float lifeMin, lifeMax, speedMin, speedMax, sizeMin, sizeMax, grow, rate;
            public float gravity, coneAngle, radius, noise, noiseFrequency, drift, spin, fadeIn, fadeOut, maxScreenSize;
            public Color color;
            public bool additive;
        }

        static bool TryGetEmitterPreset(string type, out EmitterPreset p)
        {
            switch (type)
            {
                case "smoke":   // large slow dark puffs rising (chimneys, burning coal)
                    p = new EmitterPreset
                    {
                        lifeMin = 14f, lifeMax = 22f, speedMin = 1.2f, speedMax = 2.4f, sizeMin = 5f, sizeMax = 9f, grow = 3.2f, rate = 1.6f,
                        gravity = -0.012f, coneAngle = 9f, radius = 1.5f, noise = 0.6f, noiseFrequency = 0.08f, drift = 0.8f, spin = 0.08f,
                        fadeIn = 0.08f, fadeOut = 0.55f, maxScreenSize = 1.5f, color = new Color(0.05f, 0.046f, 0.042f, 0.8f),
                    };
                    return true;
                case "steam":   // white steam rising (vents, cooling, heating mains)
                    p = new EmitterPreset
                    {
                        lifeMin = 5f, lifeMax = 8f, speedMin = 1.6f, speedMax = 3f, sizeMin = 1.6f, sizeMax = 3.2f, grow = 3.5f, rate = 5f,
                        gravity = -0.03f, coneAngle = 14f, radius = 0.6f, noise = 0.5f, noiseFrequency = 0.25f, drift = 0.5f, spin = 0.15f,
                        fadeIn = 0.06f, fadeOut = 0.6f, maxScreenSize = 1.5f, color = new Color(0.78f, 0.79f, 0.8f, 0.5f),
                    };
                    return true;
                case "spray":   // fast, short-lived white mist (spillways, falls)
                    p = new EmitterPreset
                    {
                        lifeMin = 1.2f, lifeMax = 2.4f, speedMin = 7f, speedMax = 13f, sizeMin = 1.2f, sizeMax = 3f, grow = 3f, rate = 28f,
                        gravity = 0.45f, coneAngle = 28f, radius = 1.2f, noise = 1.2f, noiseFrequency = 0.6f, drift = 0.3f, spin = 0.4f,
                        fadeIn = 0.05f, fadeOut = 0.5f, maxScreenSize = 1f, color = new Color(0.8f, 0.85f, 0.86f, 0.35f),
                    };
                    return true;
                case "dust":    // low brownish drifting dust (benches, haul roads)
                    p = new EmitterPreset
                    {
                        lifeMin = 9f, lifeMax = 14f, speedMin = 0.2f, speedMax = 0.7f, sizeMin = 3.5f, sizeMax = 8f, grow = 2f, rate = 1.2f,
                        gravity = -0.004f, coneAngle = 75f, radius = 6f, noise = 0.4f, noiseFrequency = 0.1f, drift = 1.1f, spin = 0.05f,
                        fadeIn = 0.2f, fadeOut = 0.5f, maxScreenSize = 1.5f, color = new Color(0.3f, 0.24f, 0.17f, 0.3f),
                    };
                    return true;
                case "embers":  // small bright orange additive sparks rising
                    p = new EmitterPreset
                    {
                        lifeMin = 2f, lifeMax = 4.5f, speedMin = 1.2f, speedMax = 3.5f, sizeMin = 0.05f, sizeMax = 0.16f, grow = 0.6f, rate = 10f,
                        gravity = -0.12f, coneAngle = 22f, radius = 1.4f, noise = 0.9f, noiseFrequency = 0.9f, drift = 0.4f, spin = 0f,
                        fadeIn = 0.05f, fadeOut = 0.35f, maxScreenSize = 0.5f, color = new Color(1f, 0.36f, 0.08f, 1f), additive = true,
                    };
                    return true;
                default:
                    p = default;
                    return false;
            }
        }

        static void AddEmitters(Context ctx, Transform root, AxisFix fix)
        {
            var defs = ctx.layout.emitters;
            if (defs == null || defs.Length == 0) return;
            if (ctx.pipe == Pipe.HDRP)
            {
                Debug.LogWarning($"[SkyIsland] HDRP: particle emitters (SkyIsland/Smoke, Built-in/URP only) of {ctx.id} are skipped.");
                return;
            }
            var alphaMat = SmokeMaterial(ctx, false);
            var addMat = SmokeMaterial(ctx, true);
            if (alphaMat == null || addMat == null) return;
            var group = new GameObject("Emitters").transform;
            group.SetParent(root, false);
            int n = 0;
            for (int i = 0; i < defs.Length; i++)
            {
                var e = defs[i];
                if (e == null) continue;
                string type = string.IsNullOrEmpty(e.type) ? "" : e.type.ToLowerInvariant();
                if (!TryGetEmitterPreset(type, out var preset))
                {
                    Debug.LogWarning($"[SkyIsland] {ctx.id}: emitter {i}: unknown type '{e.type}' (smoke, steam, spray, dust, embers).");
                    continue;
                }
                CreateEmitter(group, $"Emitter_{type}_{i}", fix.P(J.V3(e.p)), preset, e, preset.additive ? addMat : alphaMat, (uint)(7919 * (i + 1)));
                n++;
            }
            Debug.Log($"[SkyIsland] {ctx.id}: {n} particle emitter(s).");
        }

        static void CreateEmitter(Transform parent, string name, Vector3 position, EmitterPreset p, EmitterDef e, Material mat, uint seed)
        {
            float k = e.scale > 0f ? e.scale : 1f;
            float rate = p.rate * (e.rate > 0f ? e.rate : 1f);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);   // cone axis (+Z) points up

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);   // duration / seed can only change while stopped
            ps.useAutoRandomSeed = false;
            ps.randomSeed = seed;                                              // deterministic look

            var main = ps.main;
            main.duration = 10f;
            main.loop = true;
            main.prewarm = true;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(p.lifeMin, p.lifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(p.speedMin * k, p.speedMax * k);
            main.startSize = new ParticleSystem.MinMaxCurve(p.sizeMin * k, p.sizeMax * k);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 2f * Mathf.PI);
            var lin = J.Has(e.color, 3) ? new Color(e.color[0], e.color[1], e.color[2], p.color.a) : p.color;
            var gam = new Color(lin.r, lin.g, lin.b).gamma;                    // Unity colours are gamma-space values
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(gam.r, gam.g, gam.b, lin.a));
            main.gravityModifier = new ParticleSystem.MinMaxCurve(p.gravity);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.Clamp(Mathf.CeilToInt(rate * p.lifeMax * 1.25f) + 4, 8, 5000);

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(rate);

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = p.coneAngle;
            shape.radius = Mathf.Max(0.01f, p.radius * k);
            shape.radiusThickness = 1f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            float fadeInEnd = Mathf.Clamp(p.fadeIn, 0.01f, 0.45f);
            float fadeOutStart = Mathf.Clamp(1f - p.fadeOut, fadeInEnd + 0.01f, 0.99f);
            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fadeInEnd),
                    new GradientAlphaKey(1f, fadeOutStart), new GradientAlphaKey(0f, 1f),
                });
            col.color = new ParticleSystem.MinMaxGradient(fade);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            float sizeMul = Mathf.Max(1f, p.grow);
            size.size = new ParticleSystem.MinMaxCurve(sizeMul, AnimationCurve.EaseInOut(0f, 1f / sizeMul, 1f, p.grow / sizeMul));

            if (p.spin > 0f)
            {
                var rot = ps.rotationOverLifetime;
                rot.enabled = true;
                rot.z = new ParticleSystem.MinMaxCurve(-p.spin, p.spin);
            }

            // light wind (same direction for every emitter); all three axes must use the same curve mode
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(p.drift * k);
            vel.y = new ParticleSystem.MinMaxCurve(0f);
            vel.z = new ParticleSystem.MinMaxCurve(p.drift * k * 0.35f);

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = new ParticleSystem.MinMaxCurve(p.noise * k);
            noise.frequency = p.noiseFrequency / k;
            noise.scrollSpeed = new ParticleSystem.MinMaxCurve(0.15f);
            noise.damping = true;
            noise.quality = ParticleSystemNoiseQuality.Medium;

            var r = go.GetComponent<ParticleSystemRenderer>();   // added together with the ParticleSystem
            if (r == null) r = go.AddComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sharedMaterial = mat;
            r.sortMode = p.additive ? ParticleSystemSortMode.None : ParticleSystemSortMode.Distance;
            r.minParticleSize = 0f;
            r.maxParticleSize = p.maxScreenSize;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        /// <summary>&lt;root&gt;/Materials/&lt;Id&gt;_Smoke.mat (alpha blend) or _SmokeAdditive.mat (embers).</summary>
        static Material SmokeMaterial(Context ctx, bool additive)
        {
            var shader = Shader.Find("SkyIsland/Smoke");
            if (shader == null)
            {
                Debug.LogError("[SkyIsland] shader SkyIsland/Smoke not found (update the SkyIsland Core package); emitters skipped.");
                return null;
            }
            EnsureFolder(ctx.root + "/Materials");
            string name = ctx.AssetName(additive ? "SmokeAdditive" : "Smoke");
            string path = $"{ctx.root}/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = shader;
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(ctx.Tex(SmokeTexture));
            if (tex == null) Debug.LogWarning($"[SkyIsland] {ctx.Tex(SmokeTexture)} not found: particles render as plain squares (update the SkyIsland Core package).");
            m.SetTexture("_MainTex", tex);
            m.SetColor("_Color", Color.white);
            float emit = ctx.look != null && ctx.look.emissionScale > 0f ? ctx.look.emissionScale : 1f;
            m.SetFloat("_Intensity", additive ? 5f * emit : 1f);
            m.SetFloat("_Additive", additive ? 1f : 0f);
            m.SetFloat("_NearFade", additive ? 1f : 4f);
            m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
