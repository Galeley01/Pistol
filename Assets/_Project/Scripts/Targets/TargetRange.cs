using UnityEngine;
using NaughtyAttributes;
using Pistol.Player;

namespace Pistol.Targets
{
    // A shooting range: the targets are its children in the scene. A key (T) puts every one of them back.
    // The targets are collected once at start; a target added later at runtime is picked up by "Collect".
    //
    // Goes on the parent object of the targets.
    [DisallowMultipleComponent]
    public class TargetRange : MonoBehaviour
    {
        [BoxGroup("Ссылки"), Required("Назначьте PlayerContext игрока"), SerializeField, Tooltip("Ввод игрока: клавиша сброса мишеней")]
        PlayerContext player;

        ShootingTarget[] targets = System.Array.Empty<ShootingTarget>();

        [ShowNativeProperty] public int Targets => targets.Length;

        void Awake() => Collect();

        // The targets among the children (inactive ones too)
        public void Collect() => targets = GetComponentsInChildren<ShootingTarget>(true);

        void Update()
        {
            if (player != null && player.ResetTargetsPressed) ResetAll();
        }

        [Button("Тест: сбросить мишени")]
        public void ResetAll()
        {
            foreach (var t in targets)
                if (t != null) t.ResetTarget();
        }
    }
}
