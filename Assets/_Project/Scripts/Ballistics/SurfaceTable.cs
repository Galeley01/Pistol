using System;
using System.Collections.Generic;
using UnityEngine;
using NaughtyAttributes;

namespace Pistol.Ballistics
{
    // What each surface does when a bullet hits it, by the Physics Material of the hit collider:
    // the impact sound (Steel -> ring, Wood -> knock...) and the mark left on it (concrete -> crater...).
    // Anything without a listed material uses the defaults, so a hit is never silent and always leaves a mark.
    // A single object can still override the sound with ImpactSurface.
    // Given to BulletSystem. Changes made in Play Mode are kept (it is an asset).
    [CreateAssetMenu(menuName = "Pistol/Ballistics/Surface Table", fileName = "SurfaceSounds")]
    public class SurfaceTable : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            [Tooltip("Physics Material коллайдера")]
            public PhysicsMaterial material;
            [Tooltip("Как звучит попадание в такую поверхность")]
            public ImpactSoundProfile sound;
            [Tooltip("Какой след остаётся. Пусто — простая тёмная точка")]
            public ImpactDecal decal;
        }

        [BoxGroup("По умолчанию"), Tooltip("Звук попадания во всё, у чего нет материала из списка")]
        public ImpactSoundProfile defaultSound;
        [BoxGroup("По умолчанию"), Tooltip("След на всём, у чего нет материала из списка (бетон, стены). Пусто — простая тёмная точка")]
        public ImpactDecal defaultDecal;
        [BoxGroup("Поверхности"), Tooltip("Материал коллайдера → звук попадания и след")]
        public List<Entry> entries = new List<Entry>();

        Entry Find(PhysicsMaterial material)
        {
            if (material != null)
                foreach (var e in entries)
                    if (e.material == material) return e;
            return null;
        }

        public ImpactSoundProfile For(PhysicsMaterial material)
        {
            var e = Find(material);
            return e != null && e.sound != null ? e.sound : defaultSound;
        }

        // A listed material without a decal keeps the plain dark dot (steel for now), it doesn't take the concrete crater
        public ImpactDecal DecalFor(PhysicsMaterial material)
        {
            var e = Find(material);
            return e != null ? e.decal : defaultDecal;
        }

        public void Set(PhysicsMaterial material, ImpactSoundProfile sound)
        {
            var e = Find(material);
            if (e != null) { e.sound = sound; return; }
            entries.Add(new Entry { material = material, sound = sound });
        }

        // Lists the surface if it isn't listed yet; fills only its empty slots (sounds and marks tuned by hand stay)
        public void Register(PhysicsMaterial material, ImpactSoundProfile sound, ImpactDecal decal)
        {
            var e = Find(material);
            if (e == null) entries.Add(e = new Entry { material = material });
            if (e.sound == null) e.sound = sound;
            if (e.decal == null) e.decal = decal;
        }
    }
}
