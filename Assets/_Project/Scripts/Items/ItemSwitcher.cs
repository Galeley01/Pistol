using System;
using System.Collections.Generic;
using UnityEngine;
using NaughtyAttributes;
using Pistol.Player;

namespace Pistol.Items
{
    // Switches the item in the hands between a few prefabs with one key (H): the current item is let go
    // and hidden, the next one is taken. Each prefab is created once, on its first use, and kept, so a revolver
    // put away with the cylinder open is still open when taken back.
    // A simple step before proper slots (stage 9). Works only in the normal game mode, not in inspect.
    //
    // Goes next to ItemHolderWithPose.
    [RequireComponent(typeof(ItemHolderWithPose))]
    [DisallowMultipleComponent]
    public class ItemSwitcher : MonoBehaviour
    {
        [BoxGroup("Предметы"), SerializeField, Tooltip("Между чем переключать по H, по кругу. Стартовый предмет держателя — один из них")]
        HeldItem[] items = Array.Empty<HeldItem>();

        ItemHolderWithPose holder;
        readonly Dictionary<HeldItem, HeldItem> instances = new Dictionary<HeldItem, HeldItem>();   // prefab -> created item
        int index = -1;

        [ShowNativeProperty] public string Current => index >= 0 && index < items.Length && items[index] != null ? items[index].name : "—";

        void Awake() => holder = GetComponent<ItemHolderWithPose>();

        void Start()
        {
            // The holder has already taken its start item: remember it as the instance of that prefab
            index = Array.IndexOf(items, holder.StartItem);
            if (index >= 0 && holder.Current != null) instances[items[index]] = holder.Current;
        }

        void Update()
        {
            var player = holder.Context?.Player;
            if (player == null || player.Mode != ControlMode.Gameplay || !player.SwitchItemPressed) return;
            Next();
        }

        [Button("Тест: следующий предмет")]
        public void Next()
        {
            if (!Application.isPlaying || items.Length == 0) return;
            for (int step = 1; step <= items.Length; step++)
            {
                int i = (index + step) % items.Length;
                if (items[i] == null || i == index) continue;
                SwitchTo(i);
                return;
            }
        }

        void SwitchTo(int i)
        {
            var old = holder.Current;
            holder.Unequip();
            if (old != null) old.gameObject.SetActive(false);

            var prefab = items[i];
            if (!instances.TryGetValue(prefab, out var item) || item == null)
            {
                // Created active and equipped in the same frame, so its parts get the context before their Start
                item = Instantiate(prefab, holder.transform, false);
                item.name = prefab.name;
                instances[prefab] = item;
            }
            else item.gameObject.SetActive(true);

            holder.Equip(item);
            index = i;
        }
    }
}
