using UnityEngine;
using NaughtyAttributes;
using Pistol.Player;

namespace Pistol.Targets
{
    // A shooting range: a key (T) puts every target of the scene back (ShootingTarget.Active: the plates, the cards,
    // the glass of the car, wherever they are in the hierarchy).
    //
    // Goes on the parent object of the targets.
    [DisallowMultipleComponent]
    public class TargetRange : MonoBehaviour
    {
        [BoxGroup("Ссылки"), Required("Назначьте PlayerContext игрока"), SerializeField, Tooltip("Ввод игрока: клавиша сброса мишеней")]
        PlayerContext player;

        [ShowNativeProperty] public int Targets => ShootingTarget.Active.Count;

        void Update()
        {
            if (player != null && player.ResetTargetsPressed) ResetAll();
        }

        [Button("Тест: сбросить мишени")]
        public void ResetAll()
        {
            var targets = ShootingTarget.Active;
            for (int i = targets.Count - 1; i >= 0; i--) targets[i].ResetTarget();
        }
    }
}
