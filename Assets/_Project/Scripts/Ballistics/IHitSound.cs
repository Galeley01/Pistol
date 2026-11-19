using UnityEngine;
using Pistol.Audio;

namespace Pistol.Ballistics
{
    // A target with a sound of its own on top of the surface's impact sound (glass falling apart): BulletSystem
    // asks it right after the hit and plays the sound through the game's audio, where and when the target says.
    public interface IHitSound
    {
        bool HitSound(in BulletHit hit, out SoundEvent sound, out Vector3 position, out float delay);
    }
}
