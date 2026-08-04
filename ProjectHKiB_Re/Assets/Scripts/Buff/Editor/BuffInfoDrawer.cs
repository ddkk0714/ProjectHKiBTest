using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// BuffInfo 항목을 기본과 똑같이(Buff / Buff Stack) 그리고, 그 아래에 "남은 시간" 한 줄만 더한다.
///
/// 남은 시간을 필드로 만들지 않은 이유: BuffInfo.Cooltime(Timer)은 직렬화되지 않고,
/// Timer.RemainTime은 DOTween 시퀀스의 재생 시계를 그때그때 읽는 계산값이라 직렬화 대상이
/// 될 수 없다. 그래서 SerializedProperty로는 닿지 않고, 실제 BuffInfo 인스턴스를 리플렉션으로
/// 찾아 읽는다.
/// </summary>
[CustomPropertyDrawer(typeof(BuffInfo))]
public class BuffInfoDrawer : PropertyDrawer
{
    private static float LineHeight => EditorGUIUtility.singleLineHeight;
    private static float Spacing => EditorGUIUtility.standardVerticalSpacing;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float height = LineHeight; // 폴드아웃 줄
        if (!property.isExpanded) return height;

        foreach (SerializedProperty child in VisibleChildren(property))
            height += Spacing + EditorGUI.GetPropertyHeight(child, true);

        height += Spacing + LineHeight; // 남은 시간 줄
        return height;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        Rect line = new(position.x, position.y, position.width, LineHeight);
        property.isExpanded = EditorGUI.Foldout(line, property.isExpanded, label, true);

        if (property.isExpanded)
        {
            using (new EditorGUI.IndentLevelScope())
            {
                foreach (SerializedProperty child in VisibleChildren(property))
                {
                    line.y += line.height + Spacing;
                    line.height = EditorGUI.GetPropertyHeight(child, true);
                    EditorGUI.PropertyField(line, child, true);
                }

                // ── 여기가 추가된 부분 ──
                line.y += line.height + Spacing;
                line.height = LineHeight;
                using (new EditorGUI.DisabledScope(true))
                    EditorGUI.LabelField(line, "남은 시간", DescribeRemain(property));
            }
        }

        EditorGUI.EndProperty();
    }

    // property의 직속 자식들만 순회한다(Buff, BuffStack).
    private static System.Collections.Generic.IEnumerable<SerializedProperty> VisibleChildren(SerializedProperty property)
    {
        SerializedProperty iterator = property.Copy();
        SerializedProperty end = property.GetEndProperty();
        bool enterChildren = true;

        while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, end))
        {
            enterChildren = false;
            yield return iterator.Copy();
        }
    }

    private static string DescribeRemain(SerializedProperty property)
    {
        if (!Application.isPlaying) return "(플레이 중에 표시)";

        if (ResolveValue(property) is not BuffInfo info) return "-";
        if (info.Buff != null && info.Buff.IsBuffTimeInfinite) return "무한";
        if (info.Cooltime == null) return "-";
        if (info.Cooltime.IsCooltimeEnded) return "만료";

        // 올림이라 남은 시간이 0초로 보이는 구간 없이 1초에서 바로 사라진다.
        return $"{Mathf.CeilToInt(info.Cooltime.RemainTime)}s / {Mathf.CeilToInt(info.Cooltime.Time)}s";
    }

    // ─── SerializedProperty → 실제 객체 ──────────────────────────
    // propertyPath를 따라가며 리플렉션으로 실제 인스턴스를 찾는다.
    // 예: "<CurrentBuffs>k__BackingField.Array.data[2]"

    private static object ResolveValue(SerializedProperty property)
    {
        object current = property.serializedObject.targetObject;
        string path = property.propertyPath.Replace(".Array.data[", "[");

        foreach (string part in path.Split('.'))
        {
            if (current == null) return null;

            int bracket = part.IndexOf('[');
            if (bracket < 0)
            {
                current = GetFieldValue(current, part);
                continue;
            }

            current = GetFieldValue(current, part[..bracket]);
            if (current is not IList list) return null;

            int index = int.Parse(part[(bracket + 1)..].TrimEnd(']'));
            if (index < 0 || index >= list.Count) return null;
            current = list[index];
        }

        return current;
    }

    private static object GetFieldValue(object source, string fieldName)
    {
        if (source == null) return null;

        // 비공개 필드(자동 프로퍼티 백킹 필드 포함)까지 찾아야 하고, 상속 계층도 거슬러 올라간다.
        for (System.Type type = source.GetType(); type != null; type = type.BaseType)
        {
            FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null) return field.GetValue(source);
        }

        return null;
    }
}
