using System.Collections.Generic;
using System.Linq;
using Snow.Net;
using Snow.Vehicles.Van;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Snow.EditorTools
{
    /// <summary>
    /// Tools / Snow / Van / Build P09 Van Bench (VAN3_INTEGRATION_UNITY.md В1.3): packed snow 400 × 400 m, slopes of 10 / 20 /
    /// 30 % with a plateau each, bumps 0.10–0.25 m, an ice patch, a wall for collisions, the van in the bench state and four
    /// spawn points by its left side. Rebuilds the van prefab first. The scene goes to the end of Build Settings (the first
    /// scene stays). Player.prefab and BenchBuilder are used, not changed.
    /// </summary>
    public static class VanBenchBuilder
    {
        public const string ScenePath = BenchBuilder.Folder + "/P09_VanBench.unity";
        public const float RampLength = 40f, RampWidth = 8f, RampStartZ = 10f;
        public static readonly (float grade, float x)[] Ramps = { (0.10f, 20f), (0.20f, 35f), (0.30f, 50f) };
        // 70 m of ice: a locked stop from 35 km/h at μ 0.10 takes ~48 m (a 30 m patch let the van out onto snow, 21.10)
        public static readonly Vector3 IceCenter = new Vector3(-40f, 0f, 50f), IceSize = new Vector3(30f, 0.02f, 70f);
        public static readonly Vector3 WallCenter = new Vector3(0f, 1.5f, 120f);

        [MenuItem("Tools/Snow/Van/Build P09 Van Bench")]
        public static void Build()
        {
            GameObject variant = VanPrefabBuilder.Build();
            if (variant == null) return;
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(BenchBuilder.PlayerPrefabPath);
            NetworkPrefabsList list = BenchBuilder.EnsurePrefabList(player);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            Material snow = Mat("M_Bench_PackedSnow", new Color(0.86f, 0.88f, 0.9f));
            Material ice = Mat("M_Bench_Ice", new Color(0.62f, 0.78f, 0.9f));
            Material concrete = Mat("M_Bench_Concrete", new Color(0.45f, 0.45f, 0.43f));
            Material marker = Mat("M_Bench_Marker", new Color(0.9f, 0.35f, 0.1f));

            var ground = BenchBuilder.Box("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(400f, 1f, 400f), 0f);
            ground.GetComponent<Renderer>().sharedMaterial = snow;
            ground.AddComponent<VanSurface>().kind = SurfaceKind.PackedSnow;

            var course = new GameObject("Course").transform;
            foreach (var (grade, x) in Ramps) Ramp(course, grade, x, snow);

            // bumps: half-buried cylinders across a lane, 0.10..0.25 m high (§5 В1.3)
            var bumps = new GameObject("Bumps").transform;
            bumps.SetParent(course, false);
            for (int i = 0; i < 16; i++)
            {
                float h = 0.10f + 0.15f * (i % 4) / 3f, r = 0.4f;
                var b = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                b.name = $"Bump_{i:00}_{h * 100f:0}cm";
                b.transform.SetParent(bumps, false);
                b.transform.SetPositionAndRotation(new Vector3(-15f, h - r, 20f + 2.5f * i), Quaternion.Euler(0f, 0f, 90f));
                b.transform.localScale = new Vector3(2f * r, 4f, 2f * r);
                b.isStatic = true;
                b.GetComponent<Renderer>().sharedMaterial = snow;
            }

            var icePatch = BenchBuilder.Box("Ice", IceCenter, IceSize, 0f, course);
            icePatch.GetComponent<Renderer>().sharedMaterial = ice;
            icePatch.AddComponent<VanSurface>().kind = SurfaceKind.Ice;

            var wall = BenchBuilder.Box("Wall", WallCenter, new Vector3(20f, 3f, 1f), 0f, course);
            wall.GetComponent<Renderer>().sharedMaterial = concrete;

            // a 200 m loop to drive (AT-V02): corner posts without colliders
            for (int i = 0; i < 4; i++)
            {
                var post = BenchBuilder.Box($"LoopPost_{i}", new Vector3(i % 2 == 0 ? -25f : 25f, 1f, i < 2 ? 25f : 75f), new Vector3(0.3f, 2f, 0.3f), 0f, course);
                post.GetComponent<Renderer>().sharedMaterial = marker;
                Object.DestroyImmediate(post.GetComponent<Collider>());
            }

            var van = (GameObject)PrefabUtility.InstantiatePrefab(variant);
            van.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            van.GetComponent<VanVehicle>().startState = VanVehicle.StartState.Bench;

            var spawns = new List<Transform>();
            var spawnRoot = new GameObject("SpawnPoints").transform;
            for (int i = 0; i < SnowNetworkBootstrap.MaxPlayers; i++)
            {
                var sp = new GameObject($"Spawn_{i}").transform;
                sp.SetParent(spawnRoot, false);
                sp.position = new Vector3(-2.6f - 1.0f * i, 0f, 1.6f - 0.6f * i);
                sp.rotation = Quaternion.Euler(0f, 90f, 0f);           // facing the driver's door
                spawns.Add(sp);
            }
            BenchBuilder.CreateNetworkManager(player, list, spawns.ToArray());

            var mainCam = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).FirstOrDefault();
            if (mainCam != null) mainCam.transform.SetPositionAndRotation(new Vector3(-9f, 4f, -8f), Quaternion.Euler(15f, 45f, 0f));

            BenchBuilder.SaveNetworkScene(scene, ScenePath, false);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Snow] P09 van bench built: {ScenePath}");
        }

        /// <summary>A slope rising towards +z from the ground at RampStartZ, then a 20 m plateau at the top.</summary>
        static void Ramp(Transform parent, float grade, float x, Material m)
        {
            float a = Mathf.Atan(grade), deg = a * Mathf.Rad2Deg, thick = 1f;
            Vector3 topCenter = new Vector3(x, 0.5f * RampLength * Mathf.Sin(a), RampStartZ + 0.5f * RampLength * Mathf.Cos(a));
            Vector3 up = new Vector3(0f, Mathf.Cos(a), -Mathf.Sin(a));
            var slope = BenchBuilder.Box($"Slope_{grade * 100f:0}pct", topCenter - up * (0.5f * thick), new Vector3(RampWidth, thick, RampLength), -deg, parent);
            slope.GetComponent<Renderer>().sharedMaterial = m;
            float h = RampLength * Mathf.Sin(a), z1 = RampStartZ + RampLength * Mathf.Cos(a);
            var top = BenchBuilder.Box($"Plateau_{grade * 100f:0}pct", new Vector3(x, h - 0.5f * thick, z1 + 10f), new Vector3(RampWidth, thick, 20f), 0f, parent);
            top.GetComponent<Renderer>().sharedMaterial = m;
        }

        static Material Mat(string name, Color color)
        {
            string path = $"{VanPrefabBuilder.Folder}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }
    }
}
