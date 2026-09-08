using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;


namespace Deucarian.GameContentAuthoring.Editor
{
    internal static class GameContentLibraryMemberAccess
    {
        internal static string ReadStringMember(object target, string memberName, string fallback)
        {
            object value = ReadMemberValue(target, memberName);
            if (value is string text)
                return string.IsNullOrWhiteSpace(text) ? fallback : text;
            return fallback;
        }

        internal static object ReadMemberValue(object target, string memberName)
        {
            if (target == null || string.IsNullOrWhiteSpace(memberName)) return null;
            Type type = target.GetType();
            while (type != null)
            {
                PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
                if (property != null && property.GetIndexParameters().Length == 0)
                    return property.GetValue(target, null);
                FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
                if (field != null)
                    return field.GetValue(target);
                type = type.BaseType;
            }

            return null;
        }
    }
}
