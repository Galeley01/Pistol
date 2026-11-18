using System;
using System.Collections.Generic;
using UnityEngine;
using NaughtyAttributes;

namespace Pistol.Ballistics
{
    // Which impact sound each surface makes, by the Physics Material of the hit collider
    // (Steel -> ring, Wood -> knock...). Anything without a listed material uses the default sound,
    // so a bullet hit is never silent. A single object can still override it with ImpactSurface.
    // Given to BulletSystem. Changes made in Play Mode are kept (it is an asset).
    [CreateAssetMenu(menuName = "Pistol/Audio/Surface Sound Table", fileName = "SurfaceSounds")]
    public class SurfaceSoundTable : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            [Tooltip("Physics Material коллайдера")]
            public PhysicsMaterial material;
            [Tooltip("Как звучит попадание в такую поверхность")]
            public ImpactSoundProfile sound;
        }

        [BoxGroup("Звуки"), Tooltip("Звук попадания во всё, у чего нет материала из списка")]
        public ImpactSoundProfile defaultSound;
        [BoxGroup("Звуки"), Tooltip("Материал коллайдера → звук попадания")]
        public List<Entry> entries = new List<Entry>();

        public ImpactSoundProfile For(PhysicsMaterial material)
        {
            if (material != null)
                foreach (var e in entries)
                    if (e.material == material && e.sound != null) return e.sound;
            return defaultSound;
        }

        public void Set(PhysicsMaterial material, ImpactSoundProfile sound)
        {
            foreach (var e in entries)
                if (e.material == material) { e.sound = sound; return; }
            entries.Add(new Entry { material = material, sound = sound });
        }
    }
}
