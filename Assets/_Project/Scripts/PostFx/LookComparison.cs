using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.PostProcessing;
using NaughtyAttributes;
using Pistol.Core;

namespace Pistol.PostFx
{
    // Before / after: one key (4) switches this Post Processing volume on and off, so the new look can be compared
    // with the old one right in Play. A small label in the corner says which one is on.
    // Debug only (DebugSettings.Allowed): in a normal build the volume simply stays on.
    //
    // Goes on the object with the look volume (Pistol > Create Look Volume makes it).
    [RequireComponent(typeof(PostProcessVolume))]
    public class LookComparison : MonoBehaviour
    {
        [SerializeField, Tooltip("Клавиша сравнения: вкл/выкл этот набор эффектов")]
        string key = "<Keyboard>/4";
        [SerializeField, Tooltip("Показывать в углу экрана, что сейчас включено")]
        bool showLabel = true;

        PostProcessVolume volume;
        InputAction toggle;
        GUIStyle style;

        [ShowNativeProperty] public bool On => volume != null && volume.weight > 0f;

        void Awake() => volume = GetComponent<PostProcessVolume>();

        void OnEnable()
        {
            if (!DebugSettings.Allowed) return;
            toggle = new InputAction("CompareLook", InputActionType.Button, key);
            toggle.Enable();
        }

        void OnDisable()
        {
            toggle?.Disable();
            toggle?.Dispose();
            toggle = null;
        }

        void Update()
        {
            if (toggle != null && toggle.WasPressedThisFrame()) Toggle();
        }

        [Button("Вкл / выкл (сравнить)")]
        public void Toggle()
        {
            if (volume == null) return;
            volume.weight = volume.weight > 0f ? 0f : 1f;
        }

        void OnGUI()
        {
            if (!showLabel || toggle == null) return;
            style ??= new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
            string text = On ? "Look: NEW  (4 = compare)" : "Look: OLD  (4 = compare)";
            var rect = new Rect(Screen.width - 330, 12, 320, 28);
            GUI.color = Color.black;
            GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), text, style);
            GUI.color = On ? new Color(0.6f, 1f, 0.6f) : new Color(1f, 0.75f, 0.5f);
            GUI.Label(rect, text, style);
            GUI.color = Color.white;
        }
    }
}
