using System;
using System.Collections.Generic;
using UnityEngine;
using NaughtyAttributes;

namespace Pistol.Ballistics
{
    // What a bullet leaves on one kind of surface: a flat decal cut from an atlas of variants (a picked cell on a
    // quad), or a small 3D mesh of the crater. Each hit picks a random variant, size and turn around the surface normal.
    // Used by BulletSystem through the SurfaceTable. Changes made in Play Mode are kept (it is an asset).
    [CreateAssetMenu(menuName = "Pistol/Ballistics/Impact Decal", fileName = "ImpactDecal")]
    public class ImpactDecal : ScriptableObject
    {
        public enum Kind { Flat, Mesh }

        [BoxGroup("След"), Tooltip("Flat — плоская наклейка из атласа; Mesh — объёмная модель воронки")]
        public Kind kind = Kind.Flat;
        [BoxGroup("След"), Tooltip("Материал следа (для наклейки — шейдер Pistol/BulletDecal)")]
        public Material material;

        [BoxGroup("Атлас"), ShowIf("kind", Kind.Flat), Range(1, 8), Tooltip("Ячеек в атласе по горизонтали")]
        public int columns = 4;
        [BoxGroup("Атлас"), ShowIf("kind", Kind.Flat), Range(1, 8), Tooltip("Ячеек в атласе по вертикали")]
        public int rows = 2;
        [BoxGroup("Атлас"), ShowIf("kind", Kind.Flat), Range(1, 64), Tooltip("Сколько ячеек заполнено (по строкам, начиная сверху слева)")]
        public int variants = 5;

        [BoxGroup("Модель"), ShowIf("kind", Kind.Mesh), Tooltip("Варианты модели воронки: ось Z — наружу из поверхности, начало координат — на уровне поверхности")]
        public Mesh[] meshes = new Mesh[0];
        [BoxGroup("Модель"), ShowIf("kind", Kind.Mesh), Tooltip("Материалы по частям модели (маска отверстия, воронка, пыль вокруг). Пусто — один «Материал следа»")]
        public Material[] meshMaterials = new Material[0];
        [BoxGroup("Модель"), ShowIf("kind", Kind.Mesh), Tooltip("Поправка поворота модели, если после импорта она смотрит не наружу, градусы")]
        public Vector3 meshRotation;

        [BoxGroup("Размер"), Tooltip("Размер в метрах, а не от калибра: для наклейки — сторона квадрата, для модели — её масштаб (1 = как в модели)")]
        public bool sizeInMeters;
        [BoxGroup("Размер"), ShowIf("sizeInMeters"), Range(0.01f, 2f), Tooltip("Размер в метрах (или масштаб модели)")]
        public float meters = 1f;
        [BoxGroup("Размер"), HideIf("sizeInMeters"), Range(0.5f, 20f), Tooltip("Размер следа относительно калибра пули (воронка в бетоне больше пули)")]
        public float sizeScale = 3.5f;
        [BoxGroup("Размер"), Range(0f, 0.6f), Tooltip("Случайный разброс размера (0.3 = ±30%)")]
        public float sizeJitter = 0.3f;
        [BoxGroup("Размер"), Tooltip("Случайный поворот вокруг нормали поверхности")]
        public bool randomRotation = true;

        [Serializable]
        public class AngleVariant
        {
            [Range(0f, 89f), Tooltip("С какого угла попадания (от перпендикуляра к стене, градусы) брать этот след")]
            public float fromAngle = 45f;
            [Tooltip("След для такого угла: вытянутый вдоль полёта пули (+X текстуры — по ходу пули)")]
            public ImpactDecal decal;
        }

        [BoxGroup("Под углом"), Tooltip("Следы для попаданий под углом: берётся вариант с наибольшим «с какого угла», не больше угла попадания. " +
                                         "Пусто — этот след при любом угле")]
        public AngleVariant[] angleVariants = new AngleVariant[0];
        [BoxGroup("Под углом"), Tooltip("Повернуть след вдоль полёта пули (+X текстуры — по ходу пули) вместо случайного поворота. Для вытянутых следов")]
        public bool alongTravel;
        [BoxGroup("Под углом"), ShowIf("alongTravel"), Range(0f, 20f), Tooltip("Случайное отклонение от направления полёта, градусы")]
        public float travelJitter = 4f;

        // The mark for a hit at this angle (0 = straight on, 90 = along the surface)
        public ImpactDecal ForAngle(float incidence)
        {
            ImpactDecal best = this;
            float bestFrom = -1f;
            if (angleVariants != null)
                foreach (var v in angleVariants)
                    if (v != null && v.decal != null && v.decal != this && incidence >= v.fromAngle && v.fromAngle > bestFrom)
                    {
                        best = v.decal;
                        bestFrom = v.fromAngle;
                    }
            return best;
        }

        [System.NonSerialized] readonly List<Mesh> quads = new List<Mesh>();

        // The size of the mark for a bullet of this calibre, before the random spread
        public float Size(float caliber) => sizeInMeters ? meters : caliber * sizeScale;
        // With the random spread: what one hit gets
        public float RandomSize(float caliber) => Size(caliber) * (1f + UnityEngine.Random.Range(-sizeJitter, sizeJitter));

        [System.NonSerialized] Material[] single;   // the flat decal's one material, as an array (no new array per hit)

        public Material[] MaterialsFor()
        {
            if (kind == Kind.Mesh && meshMaterials != null && meshMaterials.Length > 0) return meshMaterials;
            if (single == null || single[0] != material) single = new[] { material };
            return single;
        }

        public int Count => kind == Kind.Flat ? Mathf.Clamp(variants, 1, columns * rows) : (meshes != null ? meshes.Length : 0);

        public Mesh MeshFor(int variant)
        {
            if (kind == Kind.Mesh) return meshes != null && meshes.Length > 0 ? meshes[variant % meshes.Length] : null;
            while (quads.Count < Count) quads.Add(BuildQuad(quads.Count));
            return quads[variant % quads.Count];
        }

        // A unit quad facing +Z (out of the surface) showing one cell of the atlas
        Mesh BuildQuad(int cell)
        {
            float w = 1f / columns, h = 1f / rows;
            float u0 = (cell % columns) * w, v0 = 1f - (cell / columns + 1) * h;   // cells go from the top left
            var m = new Mesh
            {
                name = $"{name} {cell}",
                vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) },
                uv = new[] { new Vector2(u0, v0), new Vector2(u0 + w, v0), new Vector2(u0 + w, v0 + h), new Vector2(u0, v0 + h) },
                normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward },
                tangents = new[] { new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1) },
                triangles = new[] { 0, 2, 1, 0, 3, 2 }
            };
            m.RecalculateBounds();
            return m;
        }
    }
}
