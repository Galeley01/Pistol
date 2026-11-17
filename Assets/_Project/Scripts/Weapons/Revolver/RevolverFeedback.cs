using UnityEngine;
using NaughtyAttributes;
using Pistol.Audio;
using Pistol.Cameras;
using Pistol.Items;
using Pistol.Player;

namespace Pistol.Weapons.Revolver
{
    // Shot recoil, muzzle effects and sound. Goes on Revolver (next to RevolverCylinder).
    // Listens to the cylinder: a shot triggers weapon and camera recoil, flash, light, smoke and sound;
    // a dry fire triggers only the sound. Flash and smoke are a MuzzleFx prefab nested under the muzzle,
    // sounds are SoundEvent assets played through GameAudio.
    //
    // Recoil is a kick of the WEAPON (a separate stiff spring, WeaponRecoil): the barrel flips up
    // sharply around the wrist on the grip and settles back calmly; the arms follow through IK.
    // The camera (head) jolts very slightly and rises a little. Each shot disturbs the grip (Instability),
    // so a quick follow-up shot is less accurate on its own.
    [RequireComponent(typeof(ItemRig))]
    public class RevolverFeedback : MonoBehaviour, IItemContextUser
    {
        [BoxGroup("Профиль"), Required("Назначьте профиль: отдача"), SerializeField]
        [Tooltip("Ассет с настройками. Кликните по полю, чтобы открыть его со всеми группами")]
        RecoilProfile profile;
        public RecoilProfile Profile => profile;

        [BoxGroup("Эффекты выстрела"), SerializeField]
        [Tooltip("Вспышка, дым и свет: префаб MuzzleFx, вложенный под дуло. Настраивается в самом префабе")]
        MuzzleFx muzzleFx;

        [BoxGroup("Звук"), Tooltip("Звук выстрела (необязательно). Громкость, разброс и звучание в пространстве — в самом ассете")]
        public SoundEvent shotSound;
        [BoxGroup("Звук"), Tooltip("Звук сухого щелчка (необязательно)")]
        public SoundEvent dryFireSound;

        RevolverCylinder cylinder;
        WeaponSway sway;
        WeaponRecoil recoil;
        WeaponAim weaponAim;
        CameraMotion cameraFx;
        PlayerMovement movement;
        Transform muzzle;
        GameAudio gameAudio;

        public void Bind(ItemContext context)
        {
            movement = context.Movement;
            cameraFx = context.Camera != null ? context.Camera.GetComponent<CameraMotion>() : null;
            gameAudio = context.Audio;
        }

        public void Unbind()
        {
            movement = null;
            cameraFx = null;
            gameAudio = null;
        }

        void Start()
        {
            if (profile == null) { Debug.LogError("RevolverFeedback: no profile assigned.", this); enabled = false; return; }
            cylinder = GetComponent<RevolverCylinder>();
            sway = GetComponent<WeaponSway>();
            recoil = GetComponent<WeaponRecoil>();
            if (recoil == null) Debug.LogWarning("RevolverFeedback: no WeaponRecoil, the weapon won't kick.", this);
            weaponAim = GetComponent<WeaponAim>();
            var rig = ItemRig.On(this);
            muzzle = rig != null ? rig.MuzzleOrRoot : transform;

            if (cylinder != null && cylinder.Model != null)
            {
                cylinder.Model.Fired += (i, ammo) => OnFired();
                cylinder.Model.DryFired += (i, state) => OnDryFire();
            }
            else Debug.LogWarning("RevolverFeedback: no RevolverCylinder, recoil and muzzle flash won't work.");
            if (muzzleFx == null) Debug.LogWarning("RevolverFeedback: no MuzzleFx assigned, shots have no flash or smoke.", this);
        }

        // ---------- Shot ----------
        void OnFired()
        {
            float fatigue = movement != null ? movement.Fatigue : 0f;
            float m = Mathf.Lerp(1f, profile.tiredMultiplier, fatigue);

            // Weapon recoil: barrel up (-X), random side and roll; weapon back and slightly up
            if (recoil != null)
            {
                float aim = weaponAim != null ? weaponAim.AimAmount : 0f;
                float flip = profile.flipAngle * Mathf.Lerp(1f, profile.aimFlipMultiplier, aim)
                           * Random.Range(1f - profile.flipVariation, 1f + profile.flipVariation);
                var rot = new Vector3(-flip,
                                      Random.Range(-profile.sideAngle, profile.sideAngle),
                                      Random.Range(-profile.rollAngle, profile.rollAngle)) * m;
                var pos = new Vector3(0f, profile.recoilUp, -profile.recoilBack) * m;
                recoil.AddRecoil(rot, pos);
                if (sway != null) sway.AddInstability(profile.gripDisturb * m);
            }

            // Camera: short kick and shake, plus a small view climb
            if (cameraFx != null)
            {
                cameraFx.AddRecoilPunch(profile.cameraFlip * m, Random.Range(-profile.cameraRollAngle, profile.cameraRollAngle));
                if (profile.cameraJolt > 0f) cameraFx.AddShake(profile.cameraJolt);
            }
            if (movement != null && profile.viewClimb > 0f)
                movement.AddRecoilLook(new Vector2(Random.Range(-profile.viewClimbSide, profile.viewClimbSide), profile.viewClimb * m));

            if (muzzleFx != null) muzzleFx.Play();

            PlaySound(shotSound);
        }

        void OnDryFire() => PlaySound(dryFireSound);

        // The sound comes from the muzzle and follows the weapon while it plays
        void PlaySound(SoundEvent sound)
        {
            if (sound != null && gameAudio != null) gameAudio.Play(sound, muzzle);
        }
    }
}
