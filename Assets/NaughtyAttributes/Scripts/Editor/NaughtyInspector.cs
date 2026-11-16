using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace NaughtyAttributes.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(UnityEngine.Object), true)]
    public class NaughtyInspector : UnityEditor.Editor
    {
        private List<SerializedProperty> _serializedProperties = new List<SerializedProperty>();
        private IEnumerable<FieldInfo> _nonSerializedFields;
        private IEnumerable<PropertyInfo> _nativeProperties;
        private IEnumerable<MethodInfo> _methods;
        private Dictionary<string, SavedBool> _foldouts = new Dictionary<string, SavedBool>();

        // Local patch (Pistol project). The original re-ran reflection and LINQ grouping on every
        // repaint (each frame in Play Mode), which made big inspectors lag and dropped game FPS.
        // Members are now collected once per type and the property layout once per editor.
        private sealed class TypeMembers
        {
            public List<FieldInfo> NonSerializedFields;
            public List<PropertyInfo> NativeProperties;
            public List<MethodInfo> Methods;
        }

        private sealed class PropertyGroup
        {
            public string Name;
            public List<SerializedProperty> Properties = new List<SerializedProperty>();
        }

        private static readonly Dictionary<System.Type, TypeMembers> _membersByType = new Dictionary<System.Type, TypeMembers>();

        private bool _layoutReady;
        private bool _anyNaughtyAttribute;
        private readonly List<SerializedProperty> _nonGroupedProperties = new List<SerializedProperty>();
        private readonly List<PropertyGroup> _boxGroups = new List<PropertyGroup>();
        private readonly List<PropertyGroup> _foldoutGroups = new List<PropertyGroup>();
        private readonly List<SerializedProperty> _visibleProperties = new List<SerializedProperty>();

        protected virtual void OnEnable()
        {
            if (ReferenceEquals(target, null))
            {
                // Same as the original: ReflectionUtility logs an error and there is nothing to draw
                _nonSerializedFields = ReflectionUtility.GetAllFields(target, f => false).ToList();
                _nativeProperties = new List<PropertyInfo>();
                _methods = new List<MethodInfo>();
                _layoutReady = false;
                return;
            }

            System.Type type = target.GetType();
            if (!_membersByType.TryGetValue(type, out TypeMembers members))
            {
                members = new TypeMembers
                {
                    NonSerializedFields = ReflectionUtility.GetAllFields(
                        target, f => f.GetCustomAttributes(typeof(ShowNonSerializedFieldAttribute), true).Length > 0).ToList(),
                    NativeProperties = ReflectionUtility.GetAllProperties(
                        target, p => p.GetCustomAttributes(typeof(ShowNativePropertyAttribute), true).Length > 0).ToList(),
                    Methods = ReflectionUtility.GetAllMethods(
                        target, m => m.GetCustomAttributes(typeof(ButtonAttribute), true).Length > 0).ToList(),
                };
                _membersByType[type] = members;
            }

            _nonSerializedFields = members.NonSerializedFields;
            _nativeProperties = members.NativeProperties;
            _methods = members.Methods;
            _layoutReady = false;
        }

        private void EnsureLayout()
        {
            if (_layoutReady)
            {
                return;
            }

            GetSerializedProperties(ref _serializedProperties);
            _anyNaughtyAttribute = _serializedProperties.Any(p => PropertyUtility.GetAttribute<INaughtyAttribute>(p) != null);

            _nonGroupedProperties.Clear();
            _nonGroupedProperties.AddRange(GetNonGroupedProperties(_serializedProperties));
            FillGroups(_boxGroups, GetGroupedProperties(_serializedProperties));
            FillGroups(_foldoutGroups, GetFoldoutProperties(_serializedProperties));

            _layoutReady = true;
        }

        private static void FillGroups(List<PropertyGroup> groups, IEnumerable<IGrouping<string, SerializedProperty>> source)
        {
            groups.Clear();
            foreach (var grouping in source)
            {
                var group = new PropertyGroup { Name = grouping.Key };
                group.Properties.AddRange(grouping);
                groups.Add(group);
            }
        }

        private List<SerializedProperty> CollectVisible(List<SerializedProperty> properties)
        {
            _visibleProperties.Clear();
            foreach (var property in properties)
            {
                if (PropertyUtility.IsVisible(property))
                {
                    _visibleProperties.Add(property);
                }
            }

            return _visibleProperties;
        }

        protected virtual void OnDisable()
        {
            ReorderableListPropertyDrawer.Instance.ClearCache();
        }

        public override void OnInspectorGUI()
        {
            EnsureLayout();

            if (!_anyNaughtyAttribute)
            {
                DrawDefaultInspector();
            }
            else
            {
                DrawSerializedProperties();
            }

            DrawNonSerializedFields();
            DrawNativeProperties();
            DrawButtons();
        }

        protected void GetSerializedProperties(ref List<SerializedProperty> outSerializedProperties)
        {
            outSerializedProperties.Clear();
            using (var iterator = serializedObject.GetIterator())
            {
                if (iterator.NextVisible(true))
                {
                    do
                    {
                        outSerializedProperties.Add(serializedObject.FindProperty(iterator.name));
                    }
                    while (iterator.NextVisible(false));
                }
            }
        }

        protected void DrawSerializedProperties()
        {
            EnsureLayout();
            serializedObject.Update();

            // Draw non-grouped serialized properties
            foreach (var property in _nonGroupedProperties)
            {
                if (property.name.Equals("m_Script", System.StringComparison.Ordinal))
                {
                    using (new EditorGUI.DisabledScope(disabled: true))
                    {
                        EditorGUILayout.PropertyField(property);
                    }
                }
                else
                {
                    NaughtyEditorGUI.PropertyField_Layout(property, includeChildren: true);
                }
            }

            // Draw grouped serialized properties
            foreach (var group in _boxGroups)
            {
                List<SerializedProperty> visibleProperties = CollectVisible(group.Properties);
                if (visibleProperties.Count == 0)
                {
                    continue;
                }

                NaughtyEditorGUI.BeginBoxGroup_Layout(group.Name);
                foreach (var property in visibleProperties)
                {
                    NaughtyEditorGUI.PropertyField_Layout(property, includeChildren: true);
                }

                NaughtyEditorGUI.EndBoxGroup_Layout();
            }

            // Draw foldout serialized properties
            foreach (var group in _foldoutGroups)
            {
                List<SerializedProperty> visibleProperties = CollectVisible(group.Properties);
                if (visibleProperties.Count == 0)
                {
                    continue;
                }

                if (!_foldouts.ContainsKey(group.Name))
                {
#if UNITY_6000_4_OR_NEWER
                    _foldouts[group.Name] = new SavedBool($"{EntityId.ToULong(target.GetEntityId())}.{group.Name}", false);
#else
                    _foldouts[group.Name] = new SavedBool($"{target.GetInstanceID()}.{group.Name}", false);
#endif
                }

                _foldouts[group.Name].Value = EditorGUILayout.Foldout(_foldouts[group.Name].Value, group.Name, true);
                if (_foldouts[group.Name].Value)
                {
                    foreach (var property in visibleProperties)
                    {
                        NaughtyEditorGUI.PropertyField_Layout(property, true);
                    }
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        protected void DrawNonSerializedFields(bool drawHeader = false)
        {
            if (_nonSerializedFields.Any())
            {
                if (drawHeader)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Non-Serialized Fields", GetHeaderGUIStyle());
                    NaughtyEditorGUI.HorizontalLine(
                        EditorGUILayout.GetControlRect(false), HorizontalLineAttribute.DefaultHeight, HorizontalLineAttribute.DefaultColor.GetColor());
                }

                foreach (var field in _nonSerializedFields)
                {
                    NaughtyEditorGUI.NonSerializedField_Layout(serializedObject.targetObject, field);
                }
            }
        }

        protected void DrawNativeProperties(bool drawHeader = false)
        {
            if (_nativeProperties.Any())
            {
                if (drawHeader)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Native Properties", GetHeaderGUIStyle());
                    NaughtyEditorGUI.HorizontalLine(
                        EditorGUILayout.GetControlRect(false), HorizontalLineAttribute.DefaultHeight, HorizontalLineAttribute.DefaultColor.GetColor());
                }

                foreach (var property in _nativeProperties)
                {
                    NaughtyEditorGUI.NativeProperty_Layout(serializedObject.targetObject, property);
                }
            }
        }

        protected void DrawButtons(bool drawHeader = false)
        {
            if (_methods.Any())
            {
                if (drawHeader)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Buttons", GetHeaderGUIStyle());
                    NaughtyEditorGUI.HorizontalLine(
                        EditorGUILayout.GetControlRect(false), HorizontalLineAttribute.DefaultHeight, HorizontalLineAttribute.DefaultColor.GetColor());
                }

                foreach (var method in _methods)
                {
                    NaughtyEditorGUI.Button(serializedObject.targetObject, method);
                }
            }
        }

        private static IEnumerable<SerializedProperty> GetNonGroupedProperties(IEnumerable<SerializedProperty> properties)
        {
            return properties.Where(p => PropertyUtility.GetAttribute<IGroupAttribute>(p) == null);
        }

        private static IEnumerable<IGrouping<string, SerializedProperty>> GetGroupedProperties(IEnumerable<SerializedProperty> properties)
        {
            return properties
                .Where(p => PropertyUtility.GetAttribute<BoxGroupAttribute>(p) != null)
                .GroupBy(p => PropertyUtility.GetAttribute<BoxGroupAttribute>(p).Name);
        }

        private static IEnumerable<IGrouping<string, SerializedProperty>> GetFoldoutProperties(IEnumerable<SerializedProperty> properties)
        {
            return properties
                .Where(p => PropertyUtility.GetAttribute<FoldoutAttribute>(p) != null)
                .GroupBy(p => PropertyUtility.GetAttribute<FoldoutAttribute>(p).Name);
        }

        private static GUIStyle GetHeaderGUIStyle()
        {
            GUIStyle style = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
            style.fontStyle = FontStyle.Bold;
            style.alignment = TextAnchor.UpperCenter;

            return style;
        }
    }
}
