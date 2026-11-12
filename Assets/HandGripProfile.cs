using UnityEngine;
using NaughtyAttributes;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Как руки держат конкретное оружие. Всё задаётся в координатах оружия:
// X — вправо, Y — вверх, Z — вперёд по стволу, в метрах и градусах.
// Ассет можно править прямо в Play Mode — изменения сохранятся после выхода из Play.
[CreateAssetMenu(menuName = "Weapons/Hand Grip Profile", fileName = "HandGripProfile")]
public class HandGripProfile : ScriptableObject
{
    [System.Serializable]
    public class HandPose
    {
        [Vector3Slider(Vector3SliderAttribute.Kind.Position, 0.25f), Tooltip("Где запястье относительно оружия, метры")]
        public Vector3 wristPosition;
        [Vector3Slider(Vector3SliderAttribute.Kind.Rotation, 180f), Tooltip("Поворот кисти относительно оружия, градусы")]
        public Vector3 wristRotation;
        [Vector3Slider(Vector3SliderAttribute.Kind.Position, 1f), Tooltip("Куда направлен локоть (подсказка для IK), метры")]
        public Vector3 elbowHint;

        [Header("Сгиб пальцев, градусы на каждый сустав")]
        [Range(-40f, 110f)] public float thumb;
        [Range(-40f, 110f)] public float index;
        [Range(-40f, 110f)] public float middle;
        [Range(-40f, 110f)] public float ring;
        [Range(-40f, 110f)] public float pinky;
    }

    [BoxGroup("Правая рука — держит рукоять")] public HandPose right = new HandPose
    {
        wristPosition = new Vector3(0.015f, -0.03f, -0.07f),
        elbowHint = new Vector3(0.3f, -0.4f, -0.25f),
        thumb = 25f, index = 25f, middle = 65f, ring = 70f, pinky = 75f
    };

    [BoxGroup("Левая рука — поддерживает")] public HandPose left = new HandPose
    {
        wristPosition = new Vector3(-0.055f, -0.065f, -0.035f),
        elbowHint = new Vector3(-0.35f, -0.4f, -0.2f),
        thumb = 10f, index = 55f, middle = 60f, ring = 65f, pinky = 70f
    };

    // ---------- Левая рука убирается, пока барабан открыт ----------
    [BoxGroup("Левая рука — убрана (барабан открыт)")]
    [Vector3Slider(Vector3SliderAttribute.Kind.Position, 0.5f), Tooltip("Куда уходит запястье, метры относительно оружия")]
    public Vector3 leftAwayPosition = new Vector3(-0.2f, -0.3f, -0.05f);

    [BoxGroup("Левая рука — убрана (барабан открыт)")]
    [Vector3Slider(Vector3SliderAttribute.Kind.Rotation, 180f), Tooltip("Доворот кисти относительно её поворота на рукояти")]
    public Vector3 leftAwayRotationOffset = new Vector3(0f, 0f, 0f);

    [BoxGroup("Левая рука — убрана (барабан открыт)")]
    [Vector3Slider(Vector3SliderAttribute.Kind.Position, 1f), Tooltip("Куда смотрит локоть, когда рука убрана")]
    public Vector3 leftAwayElbowHint = new Vector3(-0.4f, -0.5f, -0.2f);

    [BoxGroup("Левая рука — убрана (барабан открыт)"), Range(-40f, 110f), Tooltip("Сгиб пальцев расслабленной руки")]
    public float leftAwayFingerCurl = 15f;

    [BoxGroup("Левая рука — убрана (барабан открыт)"), Range(0.1f, 2f), Tooltip("Сколько секунд рука уходит / возвращается")]
    public float leftHandMoveTime = 0.45f;

    [BoxGroup("Левая рука — убрана (барабан открыт)"), Range(0f, 0.2f), Tooltip("Насколько рука уходит дугой наружу, а не по прямой")]
    public float leftHandArc = 0.06f;
}

// Рисует Vector3 тремя ползунками вместо трёх полей с цифрами
public class Vector3SliderAttribute : PropertyAttribute
{
    public enum Kind { Position, Rotation }
    public readonly Kind kind;
    public readonly float limit;
    public Vector3SliderAttribute(Kind kind, float limit) { this.kind = kind; this.limit = limit; }
}

#if UNITY_EDITOR
[CustomPropertyDrawer(typeof(Vector3SliderAttribute))]
public class Vector3SliderDrawer : PropertyDrawer
{
    static readonly string[] PosLabels = { "X  влево ↔ вправо", "Y  вниз ↔ вверх", "Z  назад ↔ вперёд" };
    static readonly string[] RotLabels = { "X  наклон вверх/вниз", "Y  поворот влево/вправо", "Z  вращение (крен)" };

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        => (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing) * 4;

    public override void OnGUI(Rect rect, SerializedProperty property, GUIContent label)
    {
        if (property.propertyType != SerializedPropertyType.Vector3)
        {
            EditorGUI.PropertyField(rect, property, label);
            return;
        }
        var a = (Vector3SliderAttribute)attribute;
        float line = EditorGUIUtility.singleLineHeight, step = line + EditorGUIUtility.standardVerticalSpacing;

        EditorGUI.BeginProperty(rect, label, property);
        var r = new Rect(rect.x, rect.y, rect.width, line);
        EditorGUI.LabelField(r, label, EditorStyles.boldLabel);

        Vector3 v = property.vector3Value;
        var labels = a.kind == Vector3SliderAttribute.Kind.Rotation ? RotLabels : PosLabels;
        EditorGUI.BeginChangeCheck();
        EditorGUI.indentLevel++;
        for (int i = 0; i < 3; i++)
        {
            r.y += step;
            float val = a.kind == Vector3SliderAttribute.Kind.Rotation ? Mathf.DeltaAngle(0f, v[i]) : v[i];
            v[i] = EditorGUI.Slider(r, labels[i], val, -a.limit, a.limit);
        }
        EditorGUI.indentLevel--;
        if (EditorGUI.EndChangeCheck()) property.vector3Value = v;
        EditorGUI.EndProperty();
    }
}
#endif