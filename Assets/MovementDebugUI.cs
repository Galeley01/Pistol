using UnityEngine;
using NaughtyAttributes;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Отладочная панель бега/усталости. Рисуется кодом (OnGUI), Canvas не нужен.
// Повесьте на Player. Клавиша 2 — показать/скрыть (по умолчанию скрыта).
public class MovementDebugUI : MonoBehaviour
{
    // Новое имя поля (раньше было show = true): старое значение из сцены не подхватится,
    // и панель по умолчанию будет скрыта
    [Tooltip("Показывать панель сразу при запуске игры. Во время игры — клавиша 2")]
    public bool showOnStart = false;

    [Tooltip("Найдётся сам")] public PlayerMovement movement;
    public Vector2 position = new Vector2(10, 10);
    [MinValue(0.5f), MaxValue(3f)] public float scale = 1f;

    [ShowNativeProperty] bool IsShown => show;

    [Button("Показать / скрыть")]
    void Toggle() => show = !show;

    bool show;
    Texture2D tex;
    GUIStyle label, title;

    void Start()
    {
        show = showOnStart;
        if (movement == null) movement = GetComponentInParent<PlayerMovement>();
        if (movement == null) movement = FindFirstObjectByType<PlayerMovement>();
        tex = Texture2D.whiteTexture;
    }

    void Update()
    {
        if (ReadToggle()) show = !show;
    }

#if ENABLE_INPUT_SYSTEM
    static bool ReadToggle() => Keyboard.current != null && Keyboard.current.digit2Key.wasPressedThisFrame;
#else
    static bool ReadToggle() => Input.GetKeyDown(KeyCode.Alpha2);
#endif

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

        // Фон
        Box(new Rect(x - 6, y - 6, w + 12, 268), new Color(0f, 0f, 0f, 0.6f));

        GUI.Label(new Rect(x, y, w, line), "MOVEMENT DEBUG", title); y += line + 2;

        // ---- Состояние ----
        GUI.Label(new Rect(x, y, w, line), "Состояние: <b>" + StateText(m) + "</b>", label); y += line;
        GUI.Label(new Rect(x, y, w, line),
            "Сидя: " + YesNo(m.IsCrouching) + "   Выглядывание: " + LeanText(m.LeanInput), label); y += line + 4;

        // ---- Скорость ----
        float speed = m.ActualSpeed;
        GUI.Label(new Rect(x, y, w, line),
            $"Скорость: <b>{speed:0.00} м/с</b>  ({speed * 3.6f:0.0} км/ч)", label); y += line;
        GUI.Label(new Rect(x, y, w, line),
            $"Цель: {m.TargetSpeed:0.00} м/с   Предел бега: {m.CurrentRunSpeed:0.00}", label); y += line;
        Bar(new Rect(x, y, w, barH), speed / Mathf.Max(m.runSpeed, 0.01f), new Color(0.3f, 0.7f, 1f)); y += barH + 8;

        // ---- Бег до усталости ----
        float sprint = m.IsExhausted ? m.sprintTimeBeforeTired : m.SprintTimer;
        float sprint01 = sprint / Mathf.Max(m.sprintTimeBeforeTired, 0.01f);
        GUI.Label(new Rect(x, y, w, line),
            $"Бег: {sprint:0.0} / {m.sprintTimeBeforeTired:0.0} с", label); y += line;
        Bar(new Rect(x, y, w, barH), sprint01, Color.Lerp(new Color(0.3f, 0.9f, 0.3f), new Color(1f, 0.3f, 0.2f), sprint01)); y += barH + 8;

        // ---- Усталость / отдышка ----
        GUI.Label(new Rect(x, y, w, line),
            "Усталость: " + (m.IsExhausted ? "<color=#ff5544><b>ДА</b></color>" : "<color=#66dd66>нет</color>"), label); y += line;
        float rec = m.IsExhausted ? m.RecoveryTimer : 0f;
        GUI.Label(new Rect(x, y, w, line),
            $"Отдышка: {rec:0.0} / {m.recoveryDuration:0.0} с" + (m.IsExhausted && m.IsRunning ? "  (сброс)" : ""), label); y += line;
        Bar(new Rect(x, y, w, barH), rec / Mathf.Max(m.recoveryDuration, 0.01f), new Color(1f, 0.6f, 0.1f)); y += barH + 8;

        // ---- Сила отдышки (тряска камеры) ----
        GUI.Label(new Rect(x, y, w, line), $"Сбитое дыхание: {m.Breathless:0.00}", label); y += line;
        Bar(new Rect(x, y, w, barH), m.Breathless, new Color(0.8f, 0.4f, 1f));

        GUI.matrix = oldMatrix;
    }

    string StateText(PlayerMovement m)
    {
        if (!m.IsGrounded) return "<color=#88ccff>в воздухе</color>";
        if (m.ActualSpeed < 0.1f) return m.IsCrouching ? "сидит" : "стоит";
        if (m.IsRunning) return m.IsExhausted ? "<color=#ff5544>бег (устал)</color>" : "<color=#ffdd44>бег</color>";
        if (m.IsCrouching) return m.TargetSpeed > m.crouchWalkSpeed + 0.01f ? "бег сидя" : "ходьба сидя";
        return "ходьба";
    }

    static string YesNo(bool v) => v ? "<color=#ffdd44>да</color>" : "нет";

    static string LeanText(float lean) =>
        lean < 0f ? "<color=#ffdd44>Q (влево)</color>" :
        lean > 0f ? "<color=#ffdd44>E (вправо)</color>" : "нет";

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