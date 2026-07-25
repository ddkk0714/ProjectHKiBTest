#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(EnumDropdownAttribute))]
public class EnumDropdownDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var attr = (EnumDropdownAttribute)attribute;

        EditorGUI.BeginProperty(position, label, property);
        EditorGUI.BeginChangeCheck();

        // SerializeReference 내부 intValue를 Enum 객체로 변환
        Enum currentValue = (Enum)Enum.ToObject(attr.EnumType, property.intValue);

        // Enum 드롭다운 그려주기
        Enum newValue = EditorGUI.EnumPopup(position, label, currentValue);

        if (EditorGUI.EndChangeCheck())
        {
            property.intValue = Convert.ToInt32(newValue);
        }

        EditorGUI.EndProperty();
    }
}
#endif