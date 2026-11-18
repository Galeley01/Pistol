using UnityEngine;
using NaughtyAttributes;

namespace Pistol.Ballistics
{
    // A rare exception: this object (and its children) sounds its own way when hit, whatever its Physics Material.
    // Normally the sound comes from the material through the SurfaceTable of BulletSystem.
    [DisallowMultipleComponent]
    public class ImpactSurface : MonoBehaviour
    {
        [BoxGroup("Звук попадания"), SerializeField, Tooltip("Звук попадания в этот объект вместо звука его материала")]
        ImpactSoundProfile sound;

        public ImpactSoundProfile Sound => sound;
    }
}
