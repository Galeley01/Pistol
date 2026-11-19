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
    //   - the car is lit by the light probes, not by a lightmap of its own (its lightmap UVs are many small pieces);
    //   - generated here: the crack web atlas, the shard meshes and the shard material.
    // T resets the glass like every target (GlassPane is a ShootingTarget). Existing assets are kept and updated.
    // Pistol > Add Range Props builds the car prefab with this (Prepare, then Decorate); this menu redoes it on the
    // existing prefab. Menu: Pistol > Setup Car Damage.
    public static class CarDamageSetup
    {
        const string CarDir = "Assets/_Project/Environment/Props/Car/";
        const string GlassDir = CarDir + "Glass/";
        const string AudioDir = CarDir + "Audio/";
        const string MetalDir = EditorPaths.Decals + "Metal/";
        const string CarMetalPath = CarDir + "PM_CarMetal.physicMaterial";
        const string GlassPath = CarDir + "PM_Glass.physicMaterial";
        const string PrefabPath = CarDir + "Car.prefab";

        // The car model's parts (from the .blend): texture set, and whether the part is metal
        public const string GlassSet = "glass";
        public static readonly Dictionary<string, (string set, bool metal)> Parts = new Dictionary<string, (string, bool)>
        {
            { "Plane23", ("willys", true) }, { "Object27", ("disks", true) }, { "Cylinder53", ("metal", true) },
            { "Cylinder54", ("tieres", false) }, { "Cylinder58", ("inside", false) }, { "Object19", (GlassSet, false) },
        };

        const int WindshieldHits = 6;
        const int ShardMeshes = 6;

        // What the car's parts need, made once per run
        public sealed class Kit
        {
            public PhysicsMaterial metal, glass;
            public ImpactDecal crack;
            public Mesh[] shards;
            public Material shardMaterial;
            public SoundEvent shatter;
        }

        [MenuItem("Pistol/Setup Car Damage")]
        static void Setup()
        {
            if (Application.isPlaying) { Debug.LogWarning("Set up outside Play Mode."); return; }
            var kit = Prepare();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null) { Debug.LogWarning("No car prefab yet: run Pistol > Add Range Props."); return; }
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                Decorate(root, kit);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            Debug.Log("Car damage set up: metal holes and sound, glass panes that crack and shatter.");
        }

        // Surfaces, sounds, decals and shard assets; the surfaces are listed in the SurfaceTable
        public static Kit Prepare()
        {
            var kit = new Kit
            {
                metal = EditorAssets.PhysicsMaterialAt(CarMetalPath, 0.4f, 0.4f, 0.1f),
                glass = EditorAssets.PhysicsMaterialAt(GlassPath, 0.3f, 0.3f, 0.05f),
                crack = CrackDecal(),
                shards = ShardMeshAssets(),
                shardMaterial = ShardMaterial(),
                shatter = Sound("Glass_Shatter", "Glass Break.mp3", 0.9f, 0.055f, 0f, 0f),
            };
            var table = AssetDatabase.LoadAssetAtPath<SurfaceTable>(EditorPaths.SurfaceTable);
            if (table == null) Debug.LogWarning("Surface table not found: " + EditorPaths.SurfaceTable);
            else
            {
                table.Register(kit.metal, Profile("CarMetal_Impact", Sound("CarMetal_ImpactDirect", "Car Metal Hit.mp3", 0.85f, 0.265f, 0f, 0f)), MetalDecal());
                table.Register(kit.glass, Profile("Glass_Impact", Sound("Glass_Crack", "Glass Break.mp3", 0.55f, 0.055f, 0.55f, 0.15f)), null);
                EditorUtility.SetDirty(table);
            }
            AssetDatabase.SaveAssets();
            return kit;
        }

        // On the car (a prefab being built or opened): metal surfaces, glass panes, light from the probes. One pass
        // over the parts; the glass is split only once (later runs find the panes and set them up again).
        public static void Decorate(GameObject root, Kit kit)
        {
            var panes = new List<MeshRenderer>();
            MeshRenderer glassSource = null;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                r.receiveGI = ReceiveGI.LightProbes;
                if (r.gameObject.name.StartsWith("Glass_")) { panes.Add(r); continue; }
                if (!Parts.TryGetValue(EditorAssets.PartName(r.gameObject.name), out var part)) continue;
                if (part.set == GlassSet) glassSource = r;
                else if (part.metal && r.TryGetComponent<MeshCollider>(out var c)) c.sharedMaterial = kit.metal;
            }
            if (panes.Count == 0)
            {
                if (glassSource == null) { Debug.LogWarning("The car has no glass part."); return; }
                panes = SplitGlass(glassSource);
            }
            foreach (var r in panes) r.receiveGI = ReceiveGI.LightProbes;
            ConfigurePanes(panes, kit);
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
        static void ConfigurePanes(List<MeshRenderer> panes, Kit kit)
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
                collider.sharedMaterial = kit.glass;
                var pane = r.GetComponent<GlassPane>();
                if (pane == null) pane = r.gameObject.AddComponent<GlassPane>();
                bool laminated = r == windshield;
                int count = Mathf.Clamp(Mathf.RoundToInt(Area(r) * 900f), 30, 180);
                pane.Configure(laminated ? GlassPane.Kind.Laminated : GlassPane.Kind.Tempered, laminated ? WindshieldHits : 1,
                               count, kit.crack, kit.shards, kit.shardMaterial, kit.shatter);
                if (laminated) r.gameObject.name = "Glass_Windshield";
                EditorUtility.SetDirty(pane);
            }
        }

        // ---------- Sounds ----------
        // A copy of the steel hit sound (mixer group, 3D settings) with another clip. startTime skips the silence
        // before the hit in the file (metal 0.27 s, glass 0.06 s), endTime cuts it (the crack is the start of the break)
        static SoundEvent Sound(string name, string clipFile, float volume, float startTime, float endTime, float endFade)
        {
            string path = AudioDir + name + ".asset";
            var sound = AssetDatabase.LoadAssetAtPath<SoundEvent>(path);
            if (sound != null) return sound;
            var steel = AssetDatabase.LoadAssetAtPath<SoundEvent>(EditorPaths.SteelImpactDirect);
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
            var steel = AssetDatabase.LoadAssetAtPath<ImpactSoundProfile>(EditorPaths.SteelImpact);
            profile = steel != null ? Object.Instantiate(steel) : ScriptableObject.CreateInstance<ImpactSoundProfile>();
            profile.name = name;
            profile.direct = direct;
            profile.glancing = null;
            AssetDatabase.CreateAsset(profile, path);
            return profile;
        }

        // ---------- Decals ----------
        // 8 holes in sheet metal (black through-hole, bent petals); the hole is about 1/2.2 of the cell
        static ImpactDecal MetalDecal()
        {
            Tex(MetalDir + "T_BulletHole_Metal_albedo.png", true);
            Tex(MetalDir + "T_BulletHole_Metal_normal.png", false);
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
            Tex(tex, true);
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
            var m = EditorAssets.LoadOrCreateMaterial(path, "Pistol/BulletDecal");
            if (m == null) return null;
            m.SetTexture("_MainTex", EditorAssets.Texture(albedo));
            if (normal != null) m.SetTexture("_BumpMap", EditorAssets.Texture(normal));
            m.SetFloat("_Glossiness", smoothness);
            m.renderQueue = queue;
            EditorUtility.SetDirty(m);
            return m;
        }

        static ImpactDecal DecalAsset(string path, Material material, int columns, int rows, int variants, System.Action<ImpactDecal> fresh)
        {
            var d = EditorAssets.LoadOrCreate(path, fresh);
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
            string shader = Shader.Find("Particles/Standard Surface") != null ? "Particles/Standard Surface" : "Standard";
            return EditorAssets.LoadOrCreateMaterial(GlassDir + "M_GlassShard.mat", shader, m =>
            {
                EditorAssets.MakeFade(m, new Color(0.82f, 0.9f, 0.92f, 0.55f), 0.95f);
                m.SetFloat("_Metallic", 0.1f);
            });
        }

        // ---------- Import ----------
        // Decal textures: clamped at the edges, alpha as transparency for the colour map
        static void Tex(string path, bool colour) => EditorAssets.ImportTexture(path,
            colour ? TextureImporterType.Default : TextureImporterType.NormalMap, colour, wrap: TextureWrapMode.Clamp, alphaIsTransparency: colour);
    }
}
