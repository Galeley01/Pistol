using UnityEngine;

namespace Pistol.Ballistics
{
    // Information about a bullet impact, passed to IDamageable.TakeHit
    public struct BulletHit
    {
        public Vector3 point, normal, direction;
        public float speed;          // bullet speed at impact, m/s
        public float distance;       // distance travelled, m
        public AmmoType ammo;
        public Collider collider;
        public Pistol.Audio.GameAudio audio;   // for a target that plays a sound of its own (breaking glass); may be null
    }
}
