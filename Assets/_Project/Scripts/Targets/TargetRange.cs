using UnityEngine;
using NaughtyAttributes;
using Pistol.Audio;
using Pistol.Ballistics;
using Pistol.Player;

namespace Pistol.Targets
{
    // The shooting range: resets every target with a key (T) and plays the impact sounds of the targets
    // at the hit point (ImpactSoundProfile: hit angle, bullet speed, sound travelling over the distance).
    //
    // Goes on its own object in the scene. Without it targets still react to hits, but are silent and never reset.
    [DisallowMultipleComponent]
    public class TargetRange : MonoBehaviour
    {
        [BoxGroup("Ссылки"), Required("Назначьте PlayerContext игрока"), SerializeField, Tooltip("Ввод игрока: клавиша сброса мишеней")]
        PlayerContext player;
        [BoxGroup("Ссылки"), SerializeField, Tooltip("Звук игры. Пусто — мишени молчат")]
        GameAudio gameAudio;
        [BoxGroup("Ссылки"), SerializeField, Tooltip("Где уши игрока (камера): от расстояния до неё зависит задержка звука")]
        Transform listener;

        [ShowNativeProperty] public int Targets => ShootingTarget.Active.Count;

        void OnEnable() => ShootingTarget.AnyHit += OnTargetHit;
        void OnDisable() => ShootingTarget.AnyHit -= OnTargetHit;

        void Update()
        {
            if (player != null && player.ResetTargetsPressed) ResetAll();
        }

        [Button("Тест: сбросить мишени")]
        public void ResetAll()
        {
            var list = ShootingTarget.Active;
            for (int i = list.Count - 1; i >= 0; i--) list[i].ResetTarget();
        }

        void OnTargetHit(ShootingTarget target, BulletHit hit)
        {
            var profile = target.ImpactSound;
            if (profile == null || gameAudio == null) return;

            float incidence = Vector3.Angle(-hit.direction, hit.normal);
            profile.Evaluate(incidence, hit.speed, out float directWeight, out float glancingWeight, out float pitch, out float volume);
            float delay = listener != null ? profile.Delay(Vector3.Distance(listener.position, hit.point)) : 0f;

            const float Audible = 0.05f;
            if (directWeight > Audible) gameAudio.Play(profile.direct, hit.point, volume * directWeight, pitch, delay);
            if (glancingWeight > Audible) gameAudio.Play(profile.glancing, hit.point, volume * glancingWeight, pitch, delay);
        }
    }
}
