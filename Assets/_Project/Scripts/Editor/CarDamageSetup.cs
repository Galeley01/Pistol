using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Pistol.Audio;
using Pistol.Ballistics;
using Pistol.Targets;

namespace Pistol.EditorTools
{
    // Makes the car take hits:
    //   - surfaces: PM_CarMetal (body, wheels, metal parts) and PM_Glass, listed in the SurfaceTable with their sounds
    //     (metal hit; a glass crack = the start of the breaking sound) and the metal bullet hole decal (8 holes with
    //     bent petals, atlas built from the "imperfection_0003" sheet);
    //   - glass: the one glass mesh of the model is split into its panes (10: windshield, rear, 6 side windows,
    //     2 headlights), each with its own collider and a GlassPane: cracks on every hit, tempered panes fall apart
    //     at the first hit, the windshield (the largest pane, laminated) after several;
    //   - generated here: the crack web atlas, the shard meshes and the shard material;
    //   - the car in the scene is added to the TargetRange reset (T makes the glass whole again).
    // Existing assets are kept and updated. Also run by Pistol > Add Range Props after the car prefab is rebuilt.
    // Menu: Pistol > Setup Car Damage. Save the scene afterwards.
    public static class CarDamageSetup
    {
        const string CarDir = "Assets/_Project/Environment/Props/Car/";
        const string GlassDir = CarDir + "Glass/";
        const string AudioDir = CarDir + "Audio/";
        const string DecalDir = "Assets/_Project/Weapons/Shared/FX/Decals/";
        const string MetalDir = DecalDir + "Metal/";
        const string TablePath = "Assets/_Project/Settings/SurfaceSounds.asset";
        const string SteelSoundPath = "Assets/_Project/Targets/Audio/Steel_ImpactDirect.asset";
        const string SteelProfilePath = "Assets/_Project/Targets/Steel_Impact.asset";
        public const string CarMetalPath = CarDir + "PM_CarMetal.physicMaterial";
        const string GlassPath = CarDir + "PM_Glass.physicMaterial";
        const string PrefabPath = CarDir + "Car.prefab";

        const string GlassPart = "Object19";
        static readonly string[] MetalParts = { "Plane23", "Object27", "Cylinder53" };

        const int WindshieldHits = 6;
        const int ShardMeshes = 6;

        [MenuItem("Pistol/Setup Car Damage")]
        static void Setup()
        {
            if (Application.isPlaying) { Debug.LogWarning("Set up outside Play Mode."); return; }
            SetUpCarPrefab();
            AddToRangeReset();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("Car damage set up: metal holes and sound, glass panes that crack and shatter. Save the scene.");
        }

        // The car prefab and everything it needs; true if the prefab was changed
        public static bool SetUpCarPrefab()
        {
            var metal = PhysicsMaterialAt(CarMetalPath, 0.4f, 0.4f, 0.1f);
            var glass = PhysicsMaterialAt(GlassPath, 0.3f, 0.3f, 0.05f);
            var metalDecal = MetalDecal();
            var crack = CrackDecal();
            var shards = ShardMeshAssets();
            var shardMaterial = ShardMaterial();

            var metalProfile = Profile("CarMetal_Impact", Sound("CarMetal_ImpactDirect", "Car Metal Hit.mp3", 0.85f, 0.265f, 0f, 0f));
            var crackSound = Sound("Glass_Crack", "Glass Break.mp3", 0.55f, 0.055f, 0.55f, 0.15f);
            var shatterSound = Sound("Glass_Shatter", "Glass Break.mp3", 0.9f, 0.055f, 0f, 0f);
            var glassProfile = Profile("Glass_Impact", crackSound);
            Register(metal, metalProfile, metalDecal);
            Register(glass, glassProfile, null);
            AssetDatabase.SaveAssets();

            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null) { Debug.LogWarning("No car prefab yet: run Pistol > Add Range Props."); return false; }
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                    if (System.Array.IndexOf(MetalParts, PartName(r.gameObject.name)) >= 0 && r.TryGetComponent<MeshCollider>(out var c))
                        c.sharedMaterial = metal;

                var panes = new List<MeshRenderer>();
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                    if (r.gameObject.name.StartsWith("Glass_")) panes.Add(r);
                if (panes.Count == 0)
                {
                    MeshRenderer source = null;
                    foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                        if (PartName(r.gameObject.name) == GlassPart) source = r;
                    if (source == null) { Debug.LogWarning("The car has no glass part " + GlassPart); return false; }
                    panes = SplitGlass(source);
                }
                ConfigurePanes(panes, glass, crack, shards, shardMaterial, shatterSound);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return true;
        }

        static string PartName(string name)
        {
            int split = name.IndexOf("_MeshPart", System.StringComparison.Ordinal);
            return split > 0 ? name.Substring(0, split) : name;
        }

        // ---------- Glass panes ----------
        // The glass mesh holds all panes as separate pieces: each connected piece (vertices welded by position)
        // becomes its own mesh asset and object next to the original, which is removed
        static List<MeshRenderer> SplitGlass(MeshRenderer source)
        {
            var mesh = source.GetComponent<MeshFilter>().sharedMesh;
            var material = source.sharedMaterial;
            var verts = mesh.vertices;
            var tris = mesh.triangles;

            // Weld by position, then union the vertices of every triangle
            var parent = new int[verts.Length];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
            void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[a] = b; }
            float q = Mathf.Max(mesh.bounds.size.magnitude * 1e-5f, 1e-6f);
            var byPosition = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < verts.Length; i++)
            {
                var key = new Vector3Int(Mathf.RoundToInt(verts[i].x / q), Mathf.RoundToInt(verts[i].y / q), Mathf.RoundToInt(verts[i].z / q));
                if (byPosition.TryGetValue(key, out int other)) Union(i, other); else byPosition[key] = i;
            }
            for (int t = 0; t < tris.Length; t += 3) { Union(tris[t], tris[t + 1]); Union(tris[t], tris[t + 2]); }

            var islands = new Dictionary<int, List<int>>();
            for (int t = 0; t < tris.Length; t += 3)
            {
                int k = Find(tris[t]);
                if (!islands.TryGetValue(k, out var list)) islands[k] = list = new List<int>();
                list.Add(t);
            }

            var normals = mesh.normals; var tangents = mesh.tangents; var uv = mesh.uv; var uv2 = mesh.uv2;
            var result = new List<MeshRenderer>();
            int index = 0;
            foreach (var island in islands.Values)
            {
                var map = new Dictionary<int, int>();
                var v = new List<Vector3>(); var n = new List<Vector3>(); var tg = new List<Vector4>(); var u = new List<Vector2>(); var u2 = new List<Vector2>();
                var t2 = new List<int>();
                foreach (int t in island)
                    for (int k = 0; k < 3; k++)
                    {
                        int old = tris[t + k];
                        if (!map.TryGetValue(old, out int idx))
                        {
                            idx = map[old] = v.Count;
                            v.Add(verts[old]);
                            if (normals.Length == verts.Length) n.Add(normals[old]);
                            if (tangents.Length == verts.Length) tg.Add(tangents[old]);
                            if (uv.Length == verts.Length) u.Add(uv[old]);
                            if (uv2.Length == verts.Length) u2.Add(uv2[old]);
                        }
                        t2.Add(idx);
                    }
                index++;
                string path = $"{GlassDir}GlassPane_{index:00}.asset";
                var pane = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                bool fresh = pane == null;
                if (fresh) pane = new Mesh();
                pane.Clear();
                pane.name = $"GlassPane_{index:00}";
                pane.SetVertices(v);
                if (n.Count == v.Count) pane.SetNormals(n);
                if (tg.Count == v.Count) pane.SetTangents(tg);
                if (u.Count == v.Count) pane.SetUVs(0, u);
                if (u2.Count == v.Count) pane.SetUVs(1, u2);
                pane.SetTriangles(t2, 0);
                pane.RecalculateBounds();
                if (fresh) AssetDatabase.CreateAsset(pane, path); else EditorUtility.SetDirty(pane);

                var go = new GameObject($"Glass_{index:00}", typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(source.transform.parent, false);
                go.transform.localPosition = source.transform.localPosition;
                go.transform.localRotation = source.transform.localRotation;
                go.transform.localScale = source.transform.localScale;
                go.GetComponent<MeshFilter>().sharedMesh = pane;
                var r = go.GetComponent<MeshRenderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                result.Add(r);
            }
            Object.DestroyImmediate(source.gameObject);
            Debug.Log($"Car glass split into {result.Count} panes ({GlassDir}).");
            return result;
        }

        // The largest pane is the laminated windshield; the rest is tempered. Shards by the pane's area.
        static void ConfigurePanes(List<MeshRenderer> panes, PhysicsMaterial glass, ImpactDecal crack, Mesh[] shards,
                                   Material shardMaterial, SoundEvent shatterSound)
        {
            float Area(MeshRenderer r)
            {
                var s = r.bounds.size;
                float min = Mathf.Min(s.x, Mathf.Min(s.y, s.z));
                return s.x * s.y * s.z / Mathf.Max(min, 1e-4f);   // the two largest sides (the pane's thickness left out)
            }
            MeshRenderer windshield = null;
            foreach (var r in panes) if (windshield == null || Area(r) > Area(windshield)) windshield = r;

            foreach (var r in panes)
            {
                var collider = r.GetComponent<MeshCollider>();
                if (collider == null) collider = r.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = r.GetComponent<MeshFilter>().sharedMesh;
                collider.sharedMaterial = glass;
                var pane = r.GetComponent<GlassPane>();
                if (pane == null) pane = r.gameObject.AddComponent<GlassPane>();
                bool laminated = r == windshield;
                int count = Mathf.Clamp(Mathf.RoundToInt(Area(r) * 900f), 30, 180);
                pane.Configure(laminated ? GlassPane.Kind.Laminated : GlassPane.Kind.Tempered, laminated ? WindshieldHits : 1,
                               count, crack, shards, shardMaterial, shatterSound);
                if (laminated) r.gameObject.name = "Glass_Windshield";
                EditorUtility.SetDirty(pane);
            }
        }

        static void AddToRangeReset()
        {
            var range = Object.FindFirstObjectByType<TargetRange>();   // editor tool, once
            GameObject car = null;
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                if (root.name == "Car") car = root;
            if (range == null || car == null) { Debug.LogWarning("No TargetRange or no Car in the scene: T won't reset the glass."); return; }
            Undo.RecordObject(range, "Car damage");
            range.AddReset(car);
            EditorUtility.SetDirty(range);
        }

        // ---------- Surfaces and sounds ----------
        static PhysicsMaterial PhysicsMaterialAt(string path, float dynamicFriction, float staticFriction, float bounce)
        {
            var m = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (m != null) return m;
            m = new PhysicsMaterial(Path.GetFileNameWithoutExtension(path))
                { dynamicFriction = dynamicFriction, staticFriction = staticFriction, bounciness = bounce };
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // A copy of the steel hit sound (mixer group, 3D settings) with another clip. startTime skips the silence
        // before the hit in the file (metal 0.27 s, glass 0.06 s), endTime cuts it (the crack is the start of the break)
        static SoundEvent Sound(string name, string clipFile, float volume, float startTime, float endTime, float endFade)
        {
            string path = AudioDir + name + ".asset";
            var sound = AssetDatabase.LoadAssetAtPath<SoundEvent>(path);
            if (sound != null) return sound;
            var steel = AssetDatabase.LoadAssetAtPath<SoundEvent>(SteelSoundPath);
            sound = steel != null ? Object.Instantiate(steel) : ScriptableObject.CreateInstance<SoundEvent>();
            sound.name = name;
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + clipFile);
            if (clip == null) Debug.LogWarning("Sound not found: " + AudioDir + clipFile);
            sound.clips = clip != null ? new[] { clip } : new AudioClip[0];
            sound.volume = volume;
            sound.volumeVariation = 0.1f;
            sound.pitchVariation = 0.07f;
            sound.spatialBlend = 1f;
            sound.startTime = startTime;
            sound.endTime = endTime;
            sound.endFade = endFade;
            AssetDatabase.CreateAsset(sound, path);
            return sound;
        }

        // Like the steel profile, without a separate glancing sound: at an angle the same one, quieter and higher
        static ImpactSoundProfile Profile(string name, SoundEvent direct)
        {
            string path = AudioDir + name + ".asset";
            var profile = AssetDatabase.LoadAssetAtPath<ImpactSoundProfile>(path);
            if (profile != null) return profile;
            var steel = AssetDatabase.LoadAssetAtPath<ImpactSoundProfile>(SteelProfilePath);
            profile = steel != null ? Object.Instantiate(steel) : ScriptableObject.CreateInstance<ImpactSoundProfile>();
            profile.name = name;
            profile.direct = direct;
            profile.glancing = null;
            AssetDatabase.CreateAsset(profile, path);
            return profile;
        }

        static void Register(PhysicsMaterial material, ImpactSoundProfile sound, ImpactDecal decal)
        {
            var table = AssetDatabase.LoadAssetAtPath<SurfaceTable>(TablePath);
            if (table == null) { Debug.LogWarning("Surface table not found: " + TablePath); return; }
            var entry = table.entries.Find(e => e.material == material);
            if (entry == null) table.entries.Add(entry = new SurfaceTable.Entry { material = material });
            if (entry.sound == null) entry.sound = sound;
            if (entry.decal == null) entry.decal = decal;
            EditorUtility.SetDirty(table);
        }

        // ---------- Decals ----------
        // 8 holes in sheet metal (black through-hole, bent petals); the hole is about 1/2.2 of the cell
        static ImpactDecal MetalDecal()
        {
            Tex(MetalDir + "T_BulletHole_Metal_albedo.png", TextureImporterType.Default, true);
            Tex(MetalDir + "T_BulletHole_Metal_normal.png", TextureImporterType.NormalMap, false);
            var material = DecalMaterial(MetalDir + "M_BulletHole_Metal.mat", MetalDir + "T_BulletHole_Metal_albedo.png",
                                         MetalDir + "T_BulletHole_Metal_normal.png", 0.3f, -1);
            return DecalAsset(MetalDir + "Decal_Metal.asset", material, 4, 2, 8, d =>
            {
                d.sizeInMeters = false;
                d.sizeScale = 2.2f;
                d.sizeJitter = 0.15f;
            });
        }

        static ImpactDecal CrackDecal()
        {
            string tex = GlassDir + "T_GlassCrack.png";
            if (!File.Exists(tex))
            {
                File.WriteAllBytes(tex, CrackAtlas(512));
                AssetDatabase.ImportAsset(tex, ImportAssetOptions.ForceSynchronousImport);
            }
            Tex(tex, TextureImporterType.Default, true);
            // Drawn after the transparent glass, so the cracks aren't dimmed by it
            var material = DecalMaterial(GlassDir + "M_GlassCrack.mat", tex, null, 0.9f, 3010);
            return DecalAsset(GlassDir + "Decal_GlassCrack.asset", material, 2, 2, 4, d =>
            {
                d.sizeInMeters = true;
                d.meters = 0.22f;
                d.sizeJitter = 0.3f;
            });
        }

        static Material DecalMaterial(string path, string albedo, string normal, float smoothness, int queue)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Pistol/BulletDecal")) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(albedo));
            if (normal != null) m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normal));
            m.SetFloat("_Glossiness", smoothness);
            m.renderQueue = queue;
            EditorUtility.SetDirty(m);
            return m;
        }

        static ImpactDecal DecalAsset(string path, Material material, int columns, int rows, int variants, System.Action<ImpactDecal> fresh)
        {
            var d = AssetDatabase.LoadAssetAtPath<ImpactDecal>(path);
            if (d == null)
            {
                d = ScriptableObject.CreateInstance<ImpactDecal>();
                fresh(d);
                AssetDatabase.CreateAsset(d, path);
            }
            d.kind = ImpactDecal.Kind.Flat;
            d.material = material;
            d.columns = columns;
            d.rows = rows;
            d.variants = variants;
            EditorUtility.SetDirty(d);
            return d;
        }

        // A web of cracks per cell: a frosted hole in the middle, uneven rays out from it with branches, and broken
        // rings between neighbouring rays. White lines, alpha fading towards the ends.
        static byte[] CrackAtlas(int cell)
        {
            int size = cell * 2;
            var alpha = new float[size * size];
            var random = new System.Random(4242);
            float R() => (float)random.NextDouble();

            for (int c = 0; c < 4; c++)
            {
                var centre = new Vector2((c % 2 + 0.5f) * cell, (c / 2 + 0.5f) * cell);
                float maxR = cell * 0.47f;
                int rays = 9 + random.Next(6);
                var ends = new List<Vector2>[rays];
                float[] angles = new float[rays];
                for (int i = 0; i < rays; i++) angles[i] = (i + (R() - 0.5f) * 0.6f) / rays * Mathf.PI * 2f;
                for (int i = 0; i < rays; i++)
                {
                    float a = angles[i], len = maxR * (0.55f + 0.45f * R());
                    var p = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * cell * 0.03f;
                    var points = new List<Vector2> { p };
                    int steps = 7;
                    for (int s = 1; s <= steps; s++)
                    {
                        a += (R() - 0.5f) * 0.35f;
                        var q = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * len / steps;
                        float fade = 1f - s / (float)(steps + 1);
                        Line(alpha, size, p, q, 1.4f, 0.95f * fade + 0.1f);
                        if (R() < 0.18f)   // a short branch
                        {
                            float b = a + (R() < 0.5f ? -1f : 1f) * (0.4f + R() * 0.5f);
                            Line(alpha, size, q, q + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * len * (0.1f + R() * 0.15f), 1f, 0.6f * fade);
                        }
                        p = q;
                        points.Add(p);
                    }
                    ends[i] = points;
                }
                // Rings: segments between neighbouring rays at a few distances, some missing
                for (int ring = 1; ring <= 3; ring++)
                    for (int i = 0; i < rays; i++)
                    {
                        if (R() < 0.3f) continue;
                        var a = ends[i]; var b = ends[(i + 1) % rays];
                        int k = Mathf.Clamp(ring * 2 + random.Next(2), 1, Mathf.Min(a.Count, b.Count) - 1);
                        var mid = (a[k] + b[k]) * 0.5f + (a[k] + b[k] - 2f * centre).normalized * cell * 0.01f * (R() - 0.3f);
                        float strength = 0.8f - ring * 0.15f;
                        Line(alpha, size, a[k], mid, 1.2f, strength);
                        Line(alpha, size, mid, b[k], 1.2f, strength);
                    }
                // Frosted, crushed glass around the hole, the hole itself see-through dark
                Disc(alpha, size, centre, cell * 0.05f, 0.85f);
            }

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++)
            {
                byte a = (byte)(Mathf.Clamp01(alpha[i]) * 255f);
                pixels[i] = new Color32(235, 240, 242, a);
            }
            // The hole: dark in the middle of each cell
            for (int c = 0; c < 4; c++)
            {
                var centre = new Vector2((c % 2 + 0.5f) * cell, (c / 2 + 0.5f) * cell);
                int r = Mathf.CeilToInt(cell * 0.022f);
                for (int y = -r; y <= r; y++)
                    for (int x = -r; x <= r; x++)
                    {
                        float d = Mathf.Sqrt(x * x + y * y) / r;
                        if (d > 1f) continue;
                        int i = ((int)centre.y + y) * size + (int)centre.x + x;
                        pixels[i] = new Color32(20, 22, 24, (byte)(255 * Mathf.Clamp01((1f - d) * 3f)));
                    }
            }
            tex.SetPixels32(pixels);
            var png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
            return png;
        }

        static void Line(float[] alpha, int size, Vector2 a, Vector2 b, float width, float strength)
        {
            int x0 = Mathf.FloorToInt(Mathf.Min(a.x, b.x) - width - 1), x1 = Mathf.CeilToInt(Mathf.Max(a.x, b.x) + width + 1);
            int y0 = Mathf.FloorToInt(Mathf.Min(a.y, b.y) - width - 1), y1 = Mathf.CeilToInt(Mathf.Max(a.y, b.y) + width + 1);
            Vector2 ab = b - a;
            float len2 = Mathf.Max(ab.sqrMagnitude, 1e-4f);
            for (int y = Mathf.Max(0, y0); y <= Mathf.Min(size - 1, y1); y++)
                for (int x = Mathf.Max(0, x0); x <= Mathf.Min(size - 1, x1); x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
                    float d = Vector2.Distance(p, a + ab * t);
                    float v = Mathf.Clamp01(width - d + 0.5f) * strength;
                    int i = y * size + x;
                    if (v > alpha[i]) alpha[i] = v;
                }
        }

        static void Disc(float[] alpha, int size, Vector2 centre, float radius, float strength)
        {
            int r = Mathf.CeilToInt(radius);
            for (int y = -r; y <= r; y++)
                for (int x = -r; x <= r; x++)
                {
                    float d = Mathf.Sqrt(x * x + y * y) / radius;
                    if (d > 1f) continue;
                    int i = ((int)centre.y + y) * size + (int)centre.x + x;
                    alpha[i] = Mathf.Max(alpha[i], strength * (1f - d * d) * (0.6f + 0.4f * Mathf.PerlinNoise(x * 0.4f, y * 0.4f)));
                }
        }

        // ---------- Shards ----------
        // Thin uneven glass pieces, about 1 unit across (the particle size scales them)
        static Mesh[] ShardMeshAssets()
        {
            var meshes = new Mesh[ShardMeshes];
            var random = new System.Random(77);
            for (int s = 0; s < ShardMeshes; s++)
            {
                string path = $"{GlassDir}GlassShard_{s + 1}.asset";
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (mesh == null)
                {
                    mesh = BuildShard(random, 3 + s % 3);
                    mesh.name = $"GlassShard_{s + 1}";
                    AssetDatabase.CreateAsset(mesh, path);
                }
                meshes[s] = mesh;
            }
            return meshes;
        }

        static Mesh BuildShard(System.Random random, int corners)
        {
            const float half = 0.04f;   // thickness 0.08 of the size
            var outline = new Vector2[corners];
            float start = (float)random.NextDouble() * Mathf.PI * 2f;
            for (int i = 0; i < corners; i++)
            {
                float a = start + (i + ((float)random.NextDouble() - 0.5f) * 0.6f) / corners * Mathf.PI * 2f;
                float r = 0.3f + (float)random.NextDouble() * 0.25f;
                outline[i] = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r * (0.5f + (float)random.NextDouble() * 0.5f));
            }
            var v = new List<Vector3>(); var t = new List<int>();
            // Top and bottom faces (fans), then the sides; flat shaded
            for (int side = 0; side < 2; side++)
            {
                int b = v.Count; float z = side == 0 ? half : -half;
                foreach (var p in outline) v.Add(new Vector3(p.x, p.y, z));
                for (int i = 1; i < corners - 1; i++)
                    if (side == 0) t.AddRange(new[] { b, b + i + 1, b + i }); else t.AddRange(new[] { b, b + i, b + i + 1 });
            }
            for (int i = 0; i < corners; i++)
            {
                var p = outline[i]; var q = outline[(i + 1) % corners];
                int b = v.Count;
                v.Add(new Vector3(p.x, p.y, half)); v.Add(new Vector3(q.x, q.y, half));
                v.Add(new Vector3(q.x, q.y, -half)); v.Add(new Vector3(p.x, p.y, -half));
                t.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
            }
            var mesh = new Mesh();
            mesh.SetVertices(v);
            mesh.SetTriangles(t, 0);
            var uv = new Vector2[v.Count];
            for (int i = 0; i < uv.Length; i++) uv[i] = new Vector2(v[i].x + 0.5f, v[i].y + 0.5f);
            mesh.uv = uv;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // Clear, very smooth, fading by the particle colour's alpha
        static Material ShardMaterial()
        {
            string path = GlassDir + "M_GlassShard.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            var shader = Shader.Find("Particles/Standard Surface") ?? Shader.Find("Standard");
            m = new Material(shader) { name = "M_GlassShard" };
            m.SetFloat("_Mode", 2f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_ALPHABLEND_ON");
            m.SetColor("_Color", new Color(0.82f, 0.9f, 0.92f, 0.55f));
            m.SetFloat("_Glossiness", 0.95f);
            m.SetFloat("_Metallic", 0.1f);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // ---------- Import ----------
        static void Tex(string path, TextureImporterType type, bool srgb)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) { Debug.LogWarning("Texture not found: " + path); return; }
            if (ti.textureType == type && ti.sRGBTexture == srgb && ti.textureShape == TextureImporterShape.Texture2D
                && ti.alphaIsTransparency == srgb && ti.wrapMode == TextureWrapMode.Clamp) return;
            ti.textureType = type;
            ti.textureShape = TextureImporterShape.Texture2D;   // a bare .meta can import as a cubemap
            ti.sRGBTexture = srgb;
            ti.alphaIsTransparency = srgb;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.mipmapEnabled = true;
            ti.SaveAndReimport();
        }
    }
}
