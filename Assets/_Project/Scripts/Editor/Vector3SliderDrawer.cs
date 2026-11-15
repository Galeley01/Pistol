using UnityEditor;
using UnityEngine;
using Pistol.Core;

namespace Pistol.EditorTools
{
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
}
