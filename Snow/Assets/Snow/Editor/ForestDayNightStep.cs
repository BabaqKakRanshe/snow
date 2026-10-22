using System.Collections.Generic;
using System.IO;
using Snow.Levels.Forest;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Snow.EditorTools
{
    /// <summary>
    /// Forest integration, day and night (research 2, 21.10): the clock's references - the Snow/Forest/Sky shader (a copy of
    /// the SkyIsland sky with a moon and turning stars), the day grade Volume (its weight follows the day factor), and a day set
    /// of the reflection probes, baked here with the look at noon next to the night set the terrain step bakes; the clock swaps
    /// them at mid-dawn and mid-dusk. Runs after ForestExtrasStep (which adds the clock). Running it again replaces.
    /// </summary>
    public static class ForestDayNightStep
    {
        const string SkyShaderPath = "Assets/Snow/Shaders/Sky/SnowForestSky.shader";
        const string DayProfilePath = "Assets/Snow/Levels/Forest/Forest_DayGrade.asset";
        const string DayVolumeName = "ForestDayGrade";
        const float BakeHour = 12f;

        public static void Run(Transform root)
        {
            if (!root.TryGetComponent(out ForestDayNight dn)) { Debug.LogWarning("[Snow] Forest day/night: no ForestDayNight on the root."); return; }
            dn.forestSky = AssetDatabase.LoadAssetAtPath<Shader>(SkyShaderPath);

            // the day grade: less of the night's desaturation and vignette, a little more exposure; weight 0 at night
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(DayProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, DayProfilePath);
            }
            var adj = Get<ColorAdjustments>(profile);
            adj.postExposure.Override(0.35f);
            adj.saturation.Override(-14f);
            var vig = Get<Vignette>(profile);
            vig.intensity.Override(0.22f);
            EditorUtility.SetDirty(profile);
            Transform vt = root.Find(DayVolumeName);
            GameObject vgo = vt != null ? vt.gameObject : new GameObject(DayVolumeName);
            vgo.transform.SetParent(root, false);
            vgo.layer = 0;
            if (!vgo.TryGetComponent(out Volume v)) v = vgo.AddComponent<Volume>();
            v.isGlobal = true;
            v.priority = 12f;                                          // above ForestGrade (11) and the core's look (10)
            v.weight = 0f;
            v.sharedProfile = profile;
            EditorUtility.SetDirty(v);
            dn.dayVolume = v;

            // the day reflection probes, next to the night files
            var probes = new List<ReflectionProbe>();
            var night = new List<Texture>();
            var day = new List<Texture>();
            Transform probeRoot = root.Find("ReflectionProbes");
            int baked = 0;
            if (probeRoot != null)
            {
                foreach (ReflectionProbe probe in probeRoot.GetComponentsInChildren<ReflectionProbe>(true))
                    if (probe.mode == ReflectionProbeMode.Custom && probe.customBakedTexture != null) probes.Add(probe);
                try
                {
                    dn.EditorPreview(BakeHour);
                    foreach (ReflectionProbe probe in probes)
                    {
                        Texture nightTex = probe.customBakedTexture;
                        string nightPath = AssetDatabase.GetAssetPath(nightTex);
                        string dayPath = Path.Combine(Path.GetDirectoryName(nightPath) ?? "", Path.GetFileNameWithoutExtension(nightPath) + "_Day" + Path.GetExtension(nightPath)).Replace('\\', '/');
                        EditorUtility.DisplayProgressBar("Integrate Forest", "Day reflection probe " + probe.name, 0.99f);
                        bool ok = Lightmapping.BakeReflectionProbe(probe, dayPath);
                        probe.customBakedTexture = nightTex;                    // the scene keeps the night set; the clock swaps
                        if (!ok) { Debug.LogWarning($"[Snow] Forest day/night: day probe {probe.name} not baked."); continue; }
                        AssetDatabase.ImportAsset(dayPath);
                        night.Add(nightTex);
                        day.Add(AssetDatabase.LoadAssetAtPath<Texture>(dayPath));
                        baked++;
                    }
                }
                finally
                {
                    dn.EditorRestore();
                    EditorUtility.ClearProgressBar();
                }
            }
            if (baked == probes.Count && baked > 0)
            {
                dn.probes = probes.ToArray();
                dn.nightProbeTextures = night.ToArray();
                dn.dayProbeTextures = day.ToArray();
                // the default reflection: the location's sky cubemap at night, the largest day probe by day
                dn.nightReflection = RenderSettings.customReflectionTexture;
                int big = 0;
                for (int i = 1; i < probes.Count; i++) if (probes[i].size.sqrMagnitude > probes[big].size.sqrMagnitude) big = i;
                dn.dayReflection = day[big];
            }
            else
            {
                dn.probes = new ReflectionProbe[0];
                dn.nightProbeTextures = dn.dayProbeTextures = new Texture[0];
            }
            EditorUtility.SetDirty(dn);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Snow] Forest day/night: sky shader {(dn.forestSky != null ? "Snow/Forest/Sky" : "missing")}, day grade Volume (priority 12, weight by hour), " +
                      $"{baked} of {probes.Count} reflection probes baked at {BakeHour:00}:00 as the day set.");
        }

        static T Get<T>(VolumeProfile p) where T : VolumeComponent
        {
            if (!p.TryGet(out T c))
            {
                c = p.Add<T>(false);
                c.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(c, p);
            }
            c.active = true;
            return c;
        }
    }
}
