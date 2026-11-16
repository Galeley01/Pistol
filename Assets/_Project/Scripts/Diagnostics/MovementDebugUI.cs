using UnityEngine;
using NaughtyAttributes;
using UnityEngine.InputSystem;
using Pistol.Player;

namespace Pistol.Diagnostics
{
    // Sprint/fatigue debug panel. Drawn in code (OnGUI), no Canvas needed.
    // Goes on Player. Key 2 shows/hides it (hidden by default).
    public class MovementDebugUI : MonoBehaviour
    {
        // New field name (it used to be show = true): the old scene value is not picked up,
        // so the panel is hidden by default
        [Tooltip("Показывать панель сразу при запуске игры. Во время игры — клавиша 2")]
        public bool showOnStart = false;

        [Tooltip("Найдётся сам")] public PlayerMovement movement;
        public Vector2 position = new Vector2(10, 10);
        [MinValue(0.5f), MaxValue(3f)] public float scale = 1f;

        [ShowNativeProperty] bool IsShown => show;

        [Button("Показать / скрыть")]
        void Toggle() => show = !show;

        bool show;
        InputAction toggleKey;   // debug tool: its own key, independent of the player's controls
        Texture2D tex;
        GUIStyle label, title;

        void Start()
        {
            show = showOnStart;
            if (movement == null) movement = GetComponentInParent<PlayerMovement>();
            if (movement == null) movement = FindFirstObjectByType<PlayerMovement>();
            tex = Texture2D.whiteTexture;
        }

        void OnEnable()
        {
            toggleKey = new InputAction("ToggleMovementDebug", InputActionType.Button, "<Keyboard>/2");
            toggleKey.Enable();
        }

        void OnDisable()
        {
            toggleKey?.Dispose();
            toggleKey = null;
        }

        void Update()
        {
            if (toggleKey.WasPressedThisFrame()) show = !show;
        }

        void OnGUI()
        {
            if (!show || movement == null) return;

            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true };
                label.normal.textColor = Color.white;
                title = new GUIStyle(label) { fontStyle = FontStyle.Bold };
            }

            Matrix4x4 oldMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            const float w = 280f, line = 18f, barH = 10f;
            float x = position.x, y = position.y;
            var m = movement;

            // Background
            Box(new Rect(x - 6, y - 6, w + 12, 268), new Color(0f, 0f, 0f, 0.6f));

            GUI.Label(new Rect(x, y, w, line), "MOVEMENT DEBUG", title); y += line + 2;

            // ---- State ----
            GUI.Label(new Rect(x, y, w, line), "State: <b>" + StateText(m) + "</b>", label); y += line;
            GUI.Label(new Rect(x, y, w, line),
                "Crouched: " + YesNo(m.IsCrouching) + "   Lean: " + LeanText(m.LeanInput), label); y += line + 4;

            // ---- Speed ----
            float speed = m.ActualSpeed;
            GUI.Label(new Rect(x, y, w, line),
                $"Speed: <b>{speed:0.00} m/s</b>  ({speed * 3.6f:0.0} km/h)", label); y += line;
            GUI.Label(new Rect(x, y, w, line),
                $"Target: {m.TargetSpeed:0.00} m/s   Run limit: {m.CurrentRunSpeed:0.00}", label); y += line;
            Bar(new Rect(x, y, w, barH), speed / Mathf.Max(m.runSpeed, 0.01f), new Color(0.3f, 0.7f, 1f)); y += barH + 8;

            // ---- Sprint until exhaustion ----
            float sprint = m.IsExhausted ? m.sprintTimeBeforeTired : m.SprintTimer;
            float sprint01 = sprint / Mathf.Max(m.sprintTimeBeforeTired, 0.01f);
            GUI.Label(new Rect(x, y, w, line),
                $"Sprint: {sprint:0.0} / {m.sprintTimeBeforeTired:0.0} s", label); y += line;
            Bar(new Rect(x, y, w, barH), sprint01, Color.Lerp(new Color(0.3f, 0.9f, 0.3f), new Color(1f, 0.3f, 0.2f), sprint01)); y += barH + 8;

            // ---- Exhaustion / recovery ----
            GUI.Label(new Rect(x, y, w, line),
                "Exhausted: " + (m.IsExhausted ? "<color=#ff5544><b>YES</b></color>" : "<color=#66dd66>no</color>"), label); y += line;
            float rec = m.IsExhausted ? m.RecoveryTimer : 0f;
            GUI.Label(new Rect(x, y, w, line),
                $"Recovery: {rec:0.0} / {m.recoveryDuration:0.0} s" + (m.IsExhausted && m.IsRunning ? "  (reset)" : ""), label); y += line;
            Bar(new Rect(x, y, w, barH), rec / Mathf.Max(m.recoveryDuration, 0.01f), new Color(1f, 0.6f, 0.1f)); y += barH + 8;

            // ---- Breathlessness (camera shake) ----
            GUI.Label(new Rect(x, y, w, line), $"Breathless: {m.Breathless:0.00}", label); y += line;
            Bar(new Rect(x, y, w, barH), m.Breathless, new Color(0.8f, 0.4f, 1f));

            GUI.matrix = oldMatrix;
        }

        string StateText(PlayerMovement m)
        {
            if (!m.IsGrounded) return "<color=#88ccff>in air</color>";
            if (m.ActualSpeed < 0.1f) return m.IsCrouching ? "crouching" : "standing";
            if (m.IsRunning) return m.IsExhausted ? "<color=#ff5544>running (tired)</color>" : "<color=#ffdd44>running</color>";
            if (m.IsCrouching) return m.TargetSpeed > m.crouchWalkSpeed + 0.01f ? "crouch run" : "crouch walk";
            return "walking";
        }

        static string YesNo(bool v) => v ? "<color=#ffdd44>yes</color>" : "no";

        static string LeanText(float lean) =>
            lean < 0f ? "<color=#ffdd44>Q (left)</color>" :
            lean > 0f ? "<color=#ffdd44>E (right)</color>" : "no";

        void Bar(Rect r, float value01, Color color)
        {
            Box(r, new Color(1f, 1f, 1f, 0.15f));
            Box(new Rect(r.x, r.y, r.width * Mathf.Clamp01(value01), r.height), color);
        }

        void Box(Rect r, Color c)
        {
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, tex);
            GUI.color = old;
        }
    }
}
