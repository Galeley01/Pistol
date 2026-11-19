using UnityEngine;
using NaughtyAttributes;
using Pistol.Player;

namespace Pistol.Targets
{
    // A shooting range: the targets are its children in the scene. A key (T) puts every one of them back.
    // The targets are collected once at start; a target added later at runtime is picked up by "Collect".
    // Targets that live elsewhere in the scene (the glass of the car) are reset too when their root is in "also reset".
    //
    // Goes on the parent object of the targets.
    [DisallowMultipleComponent]
    public class TargetRange : MonoBehaviour
    {
        [BoxGroup("Ссылки"), Required("Назначьте PlayerContext игрока"), SerializeField, Tooltip("Ввод игрока: клавиша сброса мишеней")]
        PlayerContext player;

        [BoxGroup("Ссылки"), SerializeField, Tooltip("Что ещё сбрасывать клавишей, кроме детей (машина со стёклами)")]
        GameObject[] alsoReset = System.Array.Empty<GameObject>();

        ShootingTarget[] targets = System.Array.Empty<ShootingTarget>();

        [ShowNativeProperty] public int Targets => targets.Length;

        void Awake() => Collect();

        // The targets among the children and the extra objects (inactive ones too)
        public void Collect()
        {
            var all = new System.Collections.Generic.List<ShootingTarget>(GetComponentsInChildren<ShootingTarget>(true));
            foreach (var extra in alsoReset)
                if (extra != null) all.AddRange(extra.GetComponentsInChildren<ShootingTarget>(true));
            targets = all.ToArray();
        }

        // Pistol > Setup Car Damage
        public void AddReset(GameObject root)
        {
            if (root == null || System.Array.IndexOf(alsoReset, root) >= 0) return;
            var list = new System.Collections.Generic.List<GameObject>(alsoReset) { root };
            list.RemoveAll(g => g == null);
            alsoReset = list.ToArray();
        }

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
