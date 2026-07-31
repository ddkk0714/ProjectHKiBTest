
using System;
using System.Collections.Generic;
using UnityEngine;

public static class Extensions
{
    public static TValue GetSafe<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, TValue defaultValue = default)
    {
        if (key == null || dict == null) return defaultValue;
        if (!dict.ContainsKey(key)) return defaultValue;
        return dict[key];
    }

    public static void AddSafe<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, TValue value)
    {
        if (dict == null || key == null) return;
        dict.Add(key, value);
    }

    public static T GetSafe<T>(this IList<T> list, int index, T defaultValue = default)
    {
        if (index < 0 || list == null) return defaultValue;
        if (index >= list.Count) return defaultValue;
        return list[index];
    }

    public static string GetPath(this Component component)
    {
        try
        {
            if (component == null || component.gameObject == null) return "";
            return component.transform.GetPath() + "/" + component.GetType().ToString();
        }
        catch (Exception e)
        {
            return "[path undetermined, error " + e + "]";
        }
    }

    public static string GetPath(this Transform current)
    {
        if (current == null)
        {
            return "NULL";
        }
        if (current.parent == null)
        {
            return "/" + current.name;
        }
        return current.parent.GetPath() + "/" + current.name;
    }

    public static List<T> ShuffleList<T>(this List<T> list)
    {
        int random1, random2;
        T temp;

        for (int i = 0; i < list.Count; ++i)
        {
            random1 = UnityEngine.Random.Range(0, list.Count);
            random2 = UnityEngine.Random.Range(0, list.Count);

            temp = list[random1];
            list[random1] = list[random2];
            list[random2] = temp;
        }

        return list;
    }

    public static T CloneObject<T>(T source)
    {
        if (ReferenceEquals(source, null)) return default;

        string json = JsonUtility.ToJson(source);
        return JsonUtility.FromJson<T>(json);
    }

    public static Quaternion DirToQuaternion4(this EnumManager.AnimDir dir)
    {
        return dir switch
        {
            EnumManager.AnimDir.D => Quaternion.identity,
            EnumManager.AnimDir.L => Quaternion.Euler(0, 0, -90),
            EnumManager.AnimDir.R => Quaternion.Euler(0, 0, 90),
            EnumManager.AnimDir.U => Quaternion.Euler(0, 0, 180),
            _ => Quaternion.identity,
        };
    }

    public static int DirToAngle4(this EnumManager.AnimDir dir)
    {
        return dir switch
        {
            EnumManager.AnimDir.D => 0,
            EnumManager.AnimDir.L => -90,
            EnumManager.AnimDir.R => 90,
            EnumManager.AnimDir.U => 180,
            _ => 0,
        };
    }

    public static Vector2 DirToVector2(this EnumManager.AnimDir dir)
    {
        return dir switch
        {
            EnumManager.AnimDir.D => Vector2.down,
            EnumManager.AnimDir.L => Vector2.left,
            EnumManager.AnimDir.R => Vector2.right,
            EnumManager.AnimDir.U => Vector2.up,
            _ => Vector2.down,
        };
    }
}
