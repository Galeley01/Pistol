using System;
using UnityEngine;
using UnityEngine.InputSystem;
using NaughtyAttributes;

namespace Pistol.Player
{
    // What the player is doing with the controls right now.
    public enum ControlMode
    {
        Gameplay,    // cursor captured, moving, looking, shooting
        Inspect,     // cursor free, inspecting a held item; movement and look are off
        CursorFree   // Esc pressed: cursor free, a click returns to Gameplay (later: pause menu)
    }

    // Single entry point for the player: the only place that reads the keyboard and mouse
    // and the only owner of the cursor. Other components read input here and request mode changes,
    // they never touch Keyboard, Mouse or Cursor themselves.
    //
    // Each mode enables only its own action map, so one key press can never be handled twice:
    // the click that recaptures the cursor can't fire, Esc in inspect mode can't also free the cursor.
    // A Button action enabled while its key is already held does not report a press that frame.
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public class PlayerContext : MonoBehaviour
    {
        [BoxGroup("Ссылки"), Required("Назначьте ассет PlayerControls (Assets/_Project/Input)")]
        [Tooltip("Схема управления (Input Actions)")]
        [SerializeField] InputActionAsset controls;

        [BoxGroup("Ссылки"), Required, Tooltip("Ходилка игрока. Находится сама на этом объекте")]
        [SerializeField] PlayerMovement movement;

        [BoxGroup("Ссылки"), Required, Tooltip("Камера игрока. Находится сама среди дочерних объектов")]
        [SerializeField] Camera viewCamera;

        [ShowNativeProperty] public ControlMode Mode => mode;
        public event Action<ControlMode> ModeChanged;

        public PlayerMovement Movement => movement;
        public Camera ViewCamera => viewCamera;

        // ---------- Gameplay input (zero / false outside Gameplay mode) ----------
        public Vector2 Move => move.ReadValue<Vector2>();
        public Vector2 LookDelta => look.ReadValue<Vector2>();     // degrees-ish per frame, already scaled in the asset
        public bool SprintHeld => sprint.IsPressed();
        public bool JumpPressed => jump.WasPressedThisFrame();
        public bool CrouchPressed => crouch.WasPressedThisFrame();
        public float Lean => (leanRight.IsPressed() ? 1f : 0f) - (leanLeft.IsPressed() ? 1f : 0f);   // -1 left, 1 right
        public bool FirePressed => fire.WasPressedThisFrame();
        public bool AimHeld => aim.IsPressed();
        public bool ReloadPressed => reload.WasPressedThisFrame();
        public bool InspectPressed => inspect.WasPressedThisFrame();
        public bool ResetTargetsPressed => resetTargets.WasPressedThisFrame();

        // ---------- Inspect input (zero / false outside Inspect mode) ----------
        public Vector2 PointerPosition => point.ReadValue<Vector2>();
        public bool SelectPressed => select.WasPressedThisFrame();
        public bool InspectExitPressed => inspectExit.WasPressedThisFrame() || inspectCancel.WasPressedThisFrame();
        public bool InspectReloadPressed => inspectReload.WasPressedThisFrame();

        ControlMode mode;
        bool initialized;
        InputActionMap gameplayMap, inspectMap, cursorFreeMap;
        InputAction move, look, sprint, jump, crouch, leanLeft, leanRight, fire, aim, reload, inspect, pause, resetTargets;
        InputAction point, select, inspectExit, inspectCancel, inspectReload;
        InputAction resume;

        void Reset()
        {
            movement = GetComponent<PlayerMovement>();
            viewCamera = GetComponentInChildren<Camera>(true);
#if UNITY_EDITOR
            var guids = UnityEditor.AssetDatabase.FindAssets("PlayerControls t:InputActionAsset");
            if (guids.Length > 0)
                controls = UnityEditor.AssetDatabase.LoadAssetAtPath<InputActionAsset>(UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]));
#endif
        }

        void Awake()
        {
            if (controls == null)
            {
                Debug.LogError("PlayerContext: no Input Actions asset assigned, the player can't be controlled.", this);
                enabled = false;
                return;
            }
            if (movement == null) movement = GetComponent<PlayerMovement>();

            gameplayMap = controls.FindActionMap("Gameplay", true);
            inspectMap = controls.FindActionMap("Inspect", true);
            cursorFreeMap = controls.FindActionMap("CursorFree", true);

            move = gameplayMap.FindAction("Move", true);
            look = gameplayMap.FindAction("Look", true);
            sprint = gameplayMap.FindAction("Sprint", true);
            jump = gameplayMap.FindAction("Jump", true);
            crouch = gameplayMap.FindAction("Crouch", true);
            leanLeft = gameplayMap.FindAction("LeanLeft", true);
            leanRight = gameplayMap.FindAction("LeanRight", true);
            fire = gameplayMap.FindAction("Fire", true);
            aim = gameplayMap.FindAction("Aim", true);
            reload = gameplayMap.FindAction("Reload", true);
            resetTargets = gameplayMap.FindAction("ResetTargets", true);
            inspect = gameplayMap.FindAction("Inspect", true);
            pause = gameplayMap.FindAction("Pause", true);

            point = inspectMap.FindAction("Point", true);
            select = inspectMap.FindAction("Select", true);
            inspectExit = inspectMap.FindAction("Exit", true);
            inspectCancel = inspectMap.FindAction("Cancel", true);
            inspectReload = inspectMap.FindAction("Reload", true);

            resume = cursorFreeMap.FindAction("Resume", true);
            initialized = true;
        }

        void OnEnable()
        {
            if (initialized) ApplyMode(ControlMode.Gameplay, notify: false);
        }

        void OnDisable()
        {
            if (!initialized) return;
            gameplayMap.Disable();
            inspectMap.Disable();
            cursorFreeMap.Disable();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void Update()
        {
            switch (mode)
            {
                case ControlMode.Gameplay:
                    // Esc, or something else released the cursor (focus loss, the editor's own Esc)
                    if (pause.WasPressedThisFrame() || Cursor.lockState != CursorLockMode.Locked)
                        SetMode(ControlMode.CursorFree);
                    break;
                case ControlMode.CursorFree:
                    if (resume.WasPressedThisFrame()) SetMode(ControlMode.Gameplay);
                    break;
            }
        }

        // Request a control mode. Inspect is entered and left by the held item (e.g. RevolverInspect).
        public void SetMode(ControlMode newMode)
        {
            if (!initialized || newMode == mode) return;
            ApplyMode(newMode, notify: true);
        }

        void ApplyMode(ControlMode newMode, bool notify)
        {
            mode = newMode;
            SetMap(gameplayMap, newMode == ControlMode.Gameplay);
            SetMap(inspectMap, newMode == ControlMode.Inspect);
            SetMap(cursorFreeMap, newMode == ControlMode.CursorFree);

            bool locked = newMode == ControlMode.Gameplay;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;

            if (notify) ModeChanged?.Invoke(newMode);
        }

        static void SetMap(InputActionMap map, bool on)
        {
            if (on) map.Enable();
            else map.Disable();
        }
    }
}
