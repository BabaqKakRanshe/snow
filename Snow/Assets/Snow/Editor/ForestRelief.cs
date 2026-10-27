using System;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace Snow.EditorTools
{
    /// <summary>
    /// The Forest's relief (research 2, 21.10, docs/notes/FOREST_RESEARCH_2_2026-10-21.md section 3). Works on the bake's
    /// global height grid (h[z, x], world metres, samples <c>step</c> apart from (x0, z0)); the package's square keeps its
    /// mesh heights except for the micro relief.
    ///  - <see cref="Generate"/>: the five generated tiles are Karelian selgas - stony ridges running NW-SE (anisotropic
    ///    ridged noise with a domain warp), a redistribution curve for flat lows and steep sides, a landmark hill to the
    ///    north-east, a bog in the south-east corner and two ravines draining into it; faded into the square's (smoothed)
    ///    edge over the blend range, so the tiles meet the square without a step or edge stripes;
    ///  - <see cref="Erode"/>: droplet hydraulic erosion (after Sebastian Lague, MIT) and thermal erosion down to the talus
    ///    angle, both only on the generated land (weight 0 in the square, fading in with the blend);
    ///  - <see cref="Micro"/>: hummocks and windthrow pits with their mounds everywhere except roads, trails, asphalt,
    ///    the yard and the ground around buildings;
    ///  - the hand layer: sculpting on the tiles survives a re-bake as a delta file (captured by the baker's menu).
    /// Deterministic: the same inputs give the same heights.
    /// </summary>
    public static class ForestRelief
    {
        // selgas: NW-SE ridges (Karelia), metres
        const float RidgeAlong = 650f, RidgeAcross = 210f, RidgeAmp = 19f, HillScale = 260f, HillAmp = 6f, WarpScale = 190f, WarpAmp = 55f;
        const float Redistribute = 1.35f;
        static readonly Vector2 RidgeAxis = new Vector2(0.7071f, -0.7071f);       // NW -> SE in (x east, z north)
        // features of the generated land (world x, z)
        static readonly Vector2 HillCentre = new Vector2(1010f, 280f);
        const float HillRadius = 150f, HillHeight = 24f;
        public static readonly Vector2 BogCentre = new Vector2(975f, -975f);
        public const float BogRadius = 160f;
        static readonly Vector2[][] Ravines =
        {
            new[] { new Vector2(660f, 560f), new Vector2(810f, 320f), new Vector2(870f, 40f), new Vector2(950f, -280f), new Vector2(995f, -590f), new Vector2(985f, -860f) },
            new[] { new Vector2(-560f, -660f), new Vector2(-330f, -770f), new Vector2(-60f, -835f), new Vector2(250f, -900f), new Vector2(560f, -960f), new Vector2(860f, -990f) },
        };
        const float RavineDepth = 8f, RavineHalfWidth = 16f, RavineFloor = 1.6f;
        // the square's edge: mirrored this deep (the south gorge gets its far bank)
        const float MirrorDepth = 110f;
        // erosion
        public const float TalusDeg = 38f;
        // micro relief
        const float MicroAmpSquare = 0.2f, MicroAmpNew = 0.38f;

        // ------------------------------------------------------------------ macro
        /// <summary>Height of the generated land at (x, z) before it is faded into the square, world metres above the
        /// square's edge level (0 = the edge).</summary>
        public static float Macro(float x, float z, out float bog, out float ravine)
        {
            float qx = x + WarpAmp * Fbm(x / WarpScale + 5.2f, z / WarpScale + 1.3f, 2);
            float qz = z + WarpAmp * Fbm(x / WarpScale + 9.7f, z / WarpScale + 3.1f, 2);
            float u = (qx * RidgeAxis.x + qz * RidgeAxis.y) / RidgeAlong;
            float v = (qx * -RidgeAxis.y + qz * RidgeAxis.x) / RidgeAcross;
            float ridge = 0f, amp = 1f, norm = 0f;
            for (int o = 0; o < 3; o++)
            {
                float r = 1f - Mathf.Abs(2f * Mathf.PerlinNoise(u * (1 << o) + 17.3f * o + 3.1f, v * (1 << o) + 41.7f * o + 7.9f) - 1f);
                ridge += amp * r * r;
                norm += amp;
                amp *= 0.45f;
            }
            ridge /= norm;
            float hills = Mathf.PerlinNoise(qx / HillScale + 71.3f, qz / HillScale + 13.9f);
            float t = Mathf.Clamp01(ridge * 0.8f + hills * 0.2f);
            float hgt = RidgeAmp * Mathf.Pow(t, Redistribute) + HillAmp * (hills - 0.5f) * 2f - RidgeAmp * 0.3f;

            float dh = Vector2.Distance(new Vector2(x, z), HillCentre) / HillRadius;
            hgt += HillHeight * Mathf.Exp(-dh * dh * 2.2f) * (0.85f + 0.15f * ridge);

            // ravines: V profile with a narrow flat floor, deepest in the middle of their run
            ravine = 0f;
            foreach (Vector2[] line in Ravines)
            {
                float d = DistanceToPolyline(new Vector2(x, z), line, out float along);
                float k = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - RavineFloor) / (RavineHalfWidth - RavineFloor)));
                k *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(along * 6f)) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - along) * 4f) * 0.6f + 0.4f);
                ravine = Mathf.Max(ravine, k);
            }
            hgt -= RavineDepth * ravine;

            // bog: a flat floor in the south-east corner, the ravines end in it
            float db = Vector2.Distance(new Vector2(x, z), BogCentre);
            bog = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((db - BogRadius * 0.55f) / (BogRadius * 0.45f)));
            float bogLevel = -RidgeAmp * 0.35f - 2f;
            hgt = Mathf.Lerp(hgt, Mathf.Min(hgt, bogLevel), bog);
            return hgt;
        }

        /// <summary>The bog's weight at (x, z), 0..1 (vegetation and paint use it).</summary>
        public static float BogWeight(float x, float z)
        {
            float db = Vector2.Distance(new Vector2(x, z), BogCentre);
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((db - BogRadius * 0.55f) / (BogRadius * 0.45f)));
        }

        /// <summary>The generated tiles around the square [gx0..gx1] x [gz0..gz1] (sample indices). The square's border is not
        /// a level to carry outwards: along its south side runs a ~70 m gorge (bottom ~-60 m, banks ~+10 m) and along the east
        /// a ditch, so the land outside mirrors the square's own heights across the edge up to <see cref="MirrorDepth"/> m (the
        /// gorge gets its far bank), then takes the bank level smoothed along the edge, and far away the mean bank level (no
        /// stripes straight out of the edge); the macro relief is added on top, faded in over <paramref name="blend"/> m.
        /// Returns the generated land's weight per sample (0 in the square, 1 past the blend).</summary>
        public static float[,] Generate(float[,] h, int gn, float step, float x0, float z0, int gx0, int gx1, int gz0, int gz1, float blend)
        {
            var weight = new float[gn, gn];
            int dm = Mathf.RoundToInt(MirrorDepth / step);
            int perim = 2 * (gx1 - gx0) + 2 * (gz1 - gz0);
            // bank level: the square's height MirrorDepth inside the edge, smoothed along the edge (radius 120 m)
            var bank = new float[perim];
            for (int i = 0; i < perim; i++)
            {
                RingPoint(i, gx0, gx1, gz0, gz1, out int rx, out int rz);
                int ix = Mathf.Clamp(rx + (rx == gx0 ? dm : rx == gx1 ? -dm : 0), gx0, gx1);
                int iz = Mathf.Clamp(rz + (rz == gz0 ? dm : rz == gz1 ? -dm : 0), gz0, gz1);
                bank[i] = h[iz, ix];
            }
            int sr = Mathf.RoundToInt(120f / step);
            var bankS = new float[perim];
            double sum = 0;
            for (int k = -sr; k <= sr; k++) sum += bank[(k % perim + perim) % perim];
            for (int i = 0; i < perim; i++)
            {
                bankS[i] = (float)(sum / (2 * sr + 1));
                sum += bank[(i + sr + 1) % perim] - bank[((i - sr) % perim + perim) % perim];
            }
            // the mean bank level of the sides that border generated land (south and east)
            double meanSum = 0;
            int meanN = 0;
            for (int i = 0; i < (gx1 - gx0) + (gz1 - gz0); i++) { meanSum += bank[i]; meanN++; }
            float mean = (float)(meanSum / Mathf.Max(1, meanN));
            // the macro relief's level along the same sides: far from the edge it replaces the edge point's own value (that
            // value is constant along the edge's normal and drew stripes across the whole generated land)
            double macroSum = 0;
            for (int i = 0; i < meanN; i++) { RingPoint(i, gx0, gx1, gz0, gz1, out int rx, out int rz); macroSum += Macro(x0 + rx * step, z0 + rz * step, out _, out _); }
            float macroMean = (float)(macroSum / Mathf.Max(1, meanN));

            for (int z = 0; z < gn; z++)
                for (int x = 0; x < gn; x++)
                {
                    if (x >= gx0 && x <= gx1 && z >= gz0 && z <= gz1) continue;
                    int bx = Mathf.Clamp(x, gx0, gx1), bz = Mathf.Clamp(z, gz0, gz1);
                    float wx = x0 + x * step, wz = z0 + z * step, bwx = x0 + bx * step, bwz = z0 + bz * step;
                    float d = Mathf.Sqrt((wx - bwx) * (wx - bwx) + (wz - bwz) * (wz - bwz));
                    int mx = x > gx1 ? gx1 - Mathf.Min(x - gx1, dm) : x < gx0 ? gx0 + Mathf.Min(gx0 - x, dm) : x;
                    int mz = z > gz1 ? gz1 - Mathf.Min(z - gz1, dm) : z < gz0 ? gz0 + Mathf.Min(gz0 - z, dm) : z;
                    float mirror = h[mz, mx];
                    int ri = RingIndex(bx, bz, gx0, gx1, gz0, gz1);
                    float b = Mathf.Lerp(mirror, bankS[ri], Mathf.SmoothStep(0f, 1f, (d - MirrorDepth * 0.6f) / MirrorDepth));
                    b = Mathf.Lerp(b, mean, Mathf.SmoothStep(0f, 1f, (d - 250f) / 350f));
                    float w = Mathf.SmoothStep(0f, 1f, d / blend);
                    float m = Macro(wx, wz, out _, out _) - Mathf.Lerp(Macro(bwx, bwz, out _, out _), macroMean, Mathf.SmoothStep(0f, 1f, d / (2f * blend)));
                    h[z, x] = b + w * m;
                    weight[z, x] = w;
                }
            return weight;
        }

        // ------------------------------------------------------------------ erosion
        /// <summary>Droplet hydraulic erosion (Sebastian Lague's algorithm, MIT; on the CPU there is no write race) and
        /// thermal erosion to <see cref="TalusDeg"/>; both scaled by <paramref name="weight"/> (0 in the square).</summary>
        public static string Erode(float[,] h, float[,] weight, int gn, float step, int drops, int thermalIters, int seed)
        {
            // erosion brush: radius 3 samples
            const int br = 3;
            var bx = new System.Collections.Generic.List<int>();
            var bz = new System.Collections.Generic.List<int>();
            var bw = new System.Collections.Generic.List<float>();
            float wsum = 0f;
            for (int j = -br; j <= br; j++)
                for (int i = -br; i <= br; i++)
                {
                    float d = Mathf.Sqrt(i * i + j * j);
                    if (d > br) continue;
                    bx.Add(i); bz.Add(j); bw.Add(1f - d / br); wsum += 1f - d / br;
                }
            for (int k = 0; k < bw.Count; k++) bw[k] /= wsum;

            const float inertia = 0.05f, capacityFactor = 4f, minCapacity = 0.01f, erodeSpeed = 0.3f, depositSpeed = 0.3f,
                        evaporate = 0.015f, gravity = 4f, maxStep = 1.5f;
            const int lifetime = 40;
            var rnd = new System.Random(seed);
            int spawned = 0;
            double moved = 0;
            for (int n = 0; n < drops * 4 && spawned < drops; n++)
            {
                float px = (float)(rnd.NextDouble() * (gn - 2 * br - 2)) + br, pz = (float)(rnd.NextDouble() * (gn - 2 * br - 2)) + br;
                if (weight[(int)pz, (int)px] < 0.5f) continue;
                spawned++;
                float dirX = 0f, dirZ = 0f, speed = 1f, water = 1f, sediment = 0f;
                for (int life = 0; life < lifetime; life++)
                {
                    int nx = (int)px, nz = (int)pz;
                    float ox = px - nx, oz = pz - nz;
                    HeightGrad(h, nx, nz, ox, oz, out float hgt, out float gx, out float gz);
                    // gradient per sample -> per metre for the physics
                    dirX = dirX * inertia - gx * (1f - inertia);
                    dirZ = dirZ * inertia - gz * (1f - inertia);
                    float len = Mathf.Sqrt(dirX * dirX + dirZ * dirZ);
                    if (len < 1e-6f) break;
                    dirX /= len; dirZ /= len;
                    px += dirX; pz += dirZ;
                    if (px < br + 1 || pz < br + 1 || px >= gn - br - 2 || pz >= gn - br - 2) break;
                    float wgt = weight[(int)pz, (int)px];
                    if (wgt <= 0f) break;
                    HeightGrad(h, (int)px, (int)pz, px - (int)px, pz - (int)pz, out float newH, out _, out _);
                    float dh = Mathf.Clamp(newH - hgt, -maxStep, maxStep);
                    float capacity = Mathf.Max(-dh * speed * water * capacityFactor, minCapacity);
                    if (sediment > capacity || dh > 0f)
                    {
                        float dep = dh > 0f ? Mathf.Min(dh, sediment) : (sediment - capacity) * depositSpeed;
                        sediment -= dep;
                        // bilinear deposit at the old position
                        h[nz, nx] += dep * (1 - ox) * (1 - oz) * wgt;
                        h[nz, nx + 1] += dep * ox * (1 - oz) * wgt;
                        h[nz + 1, nx] += dep * (1 - ox) * oz * wgt;
                        h[nz + 1, nx + 1] += dep * ox * oz * wgt;
                    }
                    else
                    {
                        float er = Mathf.Min((capacity - sediment) * erodeSpeed, -dh);
                        for (int k = 0; k < bw.Count; k++)
                        {
                            int ex = nx + bx[k], ez = nz + bz[k];
                            float take = er * bw[k] * weight[ez, ex];
                            h[ez, ex] -= take;
                            sediment += take;
                            moved += take;
                        }
                    }
                    speed = Mathf.Sqrt(Mathf.Max(0f, speed * speed + dh * gravity));
                    water *= 1f - evaporate;
                }
            }

            // thermal: material slides down where the slope exceeds the talus angle
            float talus = Mathf.Tan(TalusDeg * Mathf.Deg2Rad) * step;
            int slid = 0;
            for (int it = 0; it < thermalIters; it++)
            {
                slid = 0;
                for (int z = 1; z < gn - 1; z++)
                    for (int x = 1; x < gn - 1; x++)
                    {
                        float w = weight[z, x];
                        if (w <= 0f) continue;
                        float c = h[z, x];
                        for (int k = 0; k < 4; k++)
                        {
                            int ax = x + (k == 0 ? 1 : k == 1 ? -1 : 0), az = z + (k == 2 ? 1 : k == 3 ? -1 : 0);
                            float d = c - h[az, ax];
                            if (d <= talus) continue;
                            float m = (d - talus) * 0.25f * Mathf.Min(w, weight[az, ax]);
                            if (m <= 0f) continue;
                            h[z, x] -= m;
                            h[az, ax] += m;
                            c -= m;
                            slid++;
                        }
                    }
            }
            return $"erosion: {spawned} droplets (lifetime {lifetime}, brush r {br}), {moved:0} m³-samples moved; thermal {thermalIters} passes to {TalusDeg:0}°, last pass {slid} slides";
        }

        static void HeightGrad(float[,] h, int x, int z, float ox, float oz, out float height, out float gx, out float gz)
        {
            float nw = h[z, x], ne = h[z, x + 1], sw = h[z + 1, x], se = h[z + 1, x + 1];
            gx = (ne - nw) * (1 - oz) + (se - sw) * oz;
            gz = (sw - nw) * (1 - ox) + (se - ne) * ox;
            height = nw * (1 - ox) * (1 - oz) + ne * ox * (1 - oz) + sw * (1 - ox) * oz + se * ox * oz;
        }

        // ------------------------------------------------------------------ micro relief
        /// <summary>Hummocks (cellular-ish bumps 1.5-4 m) and windthrows (a pit with its root mound next to it) scaled by
        /// <paramref name="free"/> (0 on roads, trails, asphalt, around buildings and in the yard, 1 elsewhere) and by the
        /// amplitude of the square / the generated land.</summary>
        public static string Micro(float[,] h, float[,] free, float[,] weight, int gn, float step, float x0, float z0, int seed)
        {
            for (int z = 0; z < gn; z++)
                for (int x = 0; x < gn; x++)
                {
                    float f = free[z, x];
                    if (f <= 0f) continue;
                    float wx = x0 + x * step, wz = z0 + z * step;
                    float n1 = Mathf.PerlinNoise(wx / 3.1f + 13.7f, wz / 3.1f + 2.9f);
                    float n2 = Mathf.PerlinNoise(wx / 1.3f + 7.1f, wz / 1.3f + 19.3f);
                    float bump = Mathf.Max(0f, n1 - 0.45f) * 1.8f + (n2 - 0.5f) * 0.35f;
                    float amp = Mathf.Lerp(MicroAmpSquare, MicroAmpNew, weight[z, x]);
                    h[z, x] += bump * amp * f;
                }
            var rnd = new System.Random(seed);
            int throws = 0;
            int count = (int)(gn * step * gn * step / 10000f * 2.5f);          // ~2.5 per hectare
            for (int i = 0; i < count; i++)
            {
                int cx = rnd.Next(4, gn - 4), cz = rnd.Next(4, gn - 4);
                if (free[cz, cx] < 0.99f) continue;
                float ang = (float)(rnd.NextDouble() * Math.PI * 2);
                float pr = 1.0f + (float)rnd.NextDouble() * 0.6f, depth = 0.35f + (float)rnd.NextDouble() * 0.25f;
                int mx = cx + Mathf.RoundToInt(Mathf.Cos(ang) * 1.7f / step), mz = cz + Mathf.RoundToInt(Mathf.Sin(ang) * 1.7f / step);
                Dent(h, gn, step, cx, cz, pr, -depth);
                Dent(h, gn, step, mx, mz, pr * 0.85f, depth * 1.2f);
                throws++;
            }
            return $"micro relief: hummocks {MicroAmpSquare}-{MicroAmpNew} m (square / new land), {throws} windthrows (pit + root mound), none on roads, trails, asphalt, the yard and around buildings";
        }

        static void Dent(float[,] h, int gn, float step, int cx, int cz, float radius, float amount)
        {
            int r = Mathf.CeilToInt(radius / step);
            for (int j = -r; j <= r; j++)
                for (int i = -r; i <= r; i++)
                {
                    int x = cx + i, z = cz + j;
                    if (x < 0 || z < 0 || x >= gn || z >= gn) continue;
                    float d = Mathf.Sqrt(i * i + j * j) * step / radius;
                    if (d >= 1f) continue;
                    float k = 1f - d * d;
                    h[z, x] += amount * k * k;
                }
        }

        // ------------------------------------------------------------------ hand layer
        /// <summary>The sculpt delta (centimetres, int32, gzip; old files: millimetres, int16) that the bake adds after the generated relief, so brush work
        /// on the tiles survives a re-bake. Missing file: no delta.</summary>
        public const string HandPath = "Assets/Locations/Forest/Data/Forest_HandSculpt.bytes";

        public static bool ApplyHand(float[,] h, int gn)
        {
            if (!File.Exists(HandPath)) return false;
            using var fs = File.OpenRead(HandPath);
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            using var br = new BinaryReader(gz);
            // a negative grid size marks centimetres in int32 (27.10: the south gorge fill raises up to 82 m, past int16 mm)
            int n = br.ReadInt32();
            bool wide = n < 0;
            if (wide) n = -n;
            if (n != gn) { Debug.LogWarning($"[Snow] Forest hand sculpt: grid {n} != {gn}, ignored."); return false; }
            for (int z = 0; z < gn; z++)
                for (int x = 0; x < gn; x++)
                    h[z, x] += wide ? br.ReadInt32() * 0.01f : br.ReadInt16() * 0.001f;
            return true;
        }

        public static int SaveHand(float[,] delta, int gn)
        {
            int nonZero = 0;
            using var fs = File.Create(HandPath);
            using var gz = new GZipStream(fs, System.IO.Compression.CompressionLevel.Optimal);
            using var bw = new BinaryWriter(gz);
            bw.Write(-gn);                                           // int32 centimetres (see ApplyHand)
            for (int z = 0; z < gn; z++)
                for (int x = 0; x < gn; x++)
                {
                    float d = delta[z, x];
                    int cm = Mathf.Abs(d) < 0.02f ? 0 : Mathf.RoundToInt(d * 100f);
                    if (cm != 0) nonZero++;
                    bw.Write(cm);
                }
            return nonZero;
        }

        // ------------------------------------------------------------------ helpers
        static float Fbm(float x, float z, int octaves)
        {
            float s = 0f, a = 1f, n = 0f;
            for (int o = 0; o < octaves; o++)
            {
                s += a * (Mathf.PerlinNoise(x * (1 << o) + 11.1f * o, z * (1 << o) + 23.3f * o) - 0.5f) * 2f;
                n += a;
                a *= 0.5f;
            }
            return s / n;
        }

        static float DistanceToPolyline(Vector2 p, Vector2[] line, out float along)
        {
            float best = float.MaxValue, total = 0f, acc = 0f;
            along = 0f;
            for (int i = 0; i + 1 < line.Length; i++) total += Vector2.Distance(line[i], line[i + 1]);
            for (int i = 0; i + 1 < line.Length; i++)
            {
                Vector2 a = line[i], b = line[i + 1], ab = b - a;
                float l2 = ab.sqrMagnitude, t = l2 > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2) : 0f;
                float d = Vector2.Distance(p, a + ab * t);
                if (d < best) { best = d; along = (acc + t * Mathf.Sqrt(l2)) / total; }
                acc += Mathf.Sqrt(l2);
            }
            return best;
        }

        static void RingPoint(int i, int gx0, int gx1, int gz0, int gz1, out int x, out int z)
        {
            int w = gx1 - gx0, d = gz1 - gz0;
            if (i < w) { x = gx0 + i; z = gz0; return; }
            i -= w;
            if (i < d) { x = gx1; z = gz0 + i; return; }
            i -= d;
            if (i < w) { x = gx1 - i; z = gz1; return; }
            i -= w;
            x = gx0; z = gz1 - i;
        }

        static int RingIndex(int x, int z, int gx0, int gx1, int gz0, int gz1)
        {
            int w = gx1 - gx0, d = gz1 - gz0;
            if (z == gz0 && x < gx1) return x - gx0;
            if (x == gx1 && z < gz1) return w + (z - gz0);
            if (z == gz1 && x > gx0) return w + d + (gx1 - x);
            return 2 * w + d + Mathf.Clamp(gz1 - z, 0, d - 1);
        }
    }
}
