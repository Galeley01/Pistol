using System;
using UnityEngine;
using NaughtyAttributes;

namespace Pistol.Items
{
    // The face of an item that can be held: the player's intents come in here (use, reload, inspect),
    // and the item tells the world what it allows right now (aiming) and what its hands do (left hand free).
    // The item's own components subscribe to the intents and decide what they mean for this item:
    // for the revolver Use = fire or spin the open cylinder, Reload = open / close the cylinder.
    // Nothing on the item reads the keyboard.
    //
    // Goes on the item root, next to ItemRig.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ItemRig))]
    public class HeldItem : MonoBehaviour
    {
        [BoxGroup("Отладка"), SerializeField, Tooltip("Писать в Console каждое намерение (выстрел, перезарядка...)")]
        bool logIntents = false;

        // Intents from the player (ItemInput)
        public event Action UseRequested;
        public event Action ReloadRequested;
        public event Action InspectRequested;
        public event Action InspectExitRequested;

        // What the item reports about itself
        public event Action<bool> LeftHandFreeChanged;

        [ShowNativeProperty] public bool CanAim => canAim;
        [ShowNativeProperty] public bool LeftHandFree => leftHandFree;

        bool canAim = true;
        bool leftHandFree;

        public void Use() { Log("use"); UseRequested?.Invoke(); }
        public void Reload() { Log("reload"); ReloadRequested?.Invoke(); }
        public void Inspect() { Log("inspect"); InspectRequested?.Invoke(); }
        public void ExitInspect() { Log("exit inspect"); InspectExitRequested?.Invoke(); }

        // Called by the item's own components
        public void SetCanAim(bool value) => canAim = value;

        public void SetLeftHandFree(bool free)
        {
            if (leftHandFree == free) return;
            leftHandFree = free;
            LeftHandFreeChanged?.Invoke(free);
        }

        void Log(string intent)
        {
            if (logIntents) Debug.Log($"{name}: {intent}", this);
        }
    }
}
