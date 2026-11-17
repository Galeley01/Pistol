using System;
using UnityEngine;
using NaughtyAttributes;
using Pistol.Core;

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
    public class HeldItem : MonoBehaviour, IItemContextUser
    {
        // Intents from the player (ItemInput)
        public event Action UseRequested;
        public event Action ReloadRequested;
        // R in inspect mode: the item may reload in its own way (the revolver closes with a swing).
        // An item that doesn't listen gets a plain Reload instead.
        public event Action InspectReloadRequested;
        public event Action InspectRequested;
        public event Action InspectExitRequested;

        // What the item reports about itself
        public event Action<bool> LeftHandFreeChanged;

        [ShowNativeProperty] public bool CanAim => canAim;
        [ShowNativeProperty] public bool LeftHandFree => leftHandFree;

        DebugSettings debug;
        public void Bind(ItemContext context) => debug = context.Debug;
        public void Unbind() => debug = null;

        bool canAim = true;
        bool leftHandFree;

        public void Use() { Log("use"); UseRequested?.Invoke(); }
        public void Reload() { Log("reload"); ReloadRequested?.Invoke(); }
        public void ReloadFromInspect()
        {
            Log("reload from inspect");
            if (InspectReloadRequested != null) InspectReloadRequested.Invoke();
            else ReloadRequested?.Invoke();
        }
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
            if (debug != null && debug.ItemIntents) Debug.Log($"{name}: {intent}", this);
        }
    }
}
