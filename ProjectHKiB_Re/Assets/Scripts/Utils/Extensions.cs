
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
}
