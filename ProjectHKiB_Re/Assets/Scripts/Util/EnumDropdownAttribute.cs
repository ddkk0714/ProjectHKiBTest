using System;
using UnityEngine;

[AttributeUsage(AttributeTargets.Field)]
public class EnumDropdownAttribute : PropertyAttribute
{
    public Type EnumType { get; }

    public EnumDropdownAttribute(Type enumType)
    {
        EnumType = enumType;
    }
}