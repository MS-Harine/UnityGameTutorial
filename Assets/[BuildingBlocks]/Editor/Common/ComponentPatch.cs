using System;
using UnityEditor;
using UnityEngine;
using System.Globalization;
using System.Collections.Generic;

namespace Blocks
{
    /// <summary>
    /// Reads and writes a component's serialized fields as plain text, and locates components by type name.
    /// Backs the Inspector edit flow that applies the assistant's ComponentEdits: a value arrives as a
    /// string, gets coerced to the property's real type, and the current value is read back for display.
    /// </summary>
    static class ComponentPatch
    {
        #region Property values

        public static bool IsCoercible(SerializedPropertyType type)
        {
            switch (type)
            {
                case SerializedPropertyType.Float:
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.Boolean:
                case SerializedPropertyType.String:
                case SerializedPropertyType.Enum:
                case SerializedPropertyType.Color:
                case SerializedPropertyType.Vector2:
                case SerializedPropertyType.Vector3:
                    return true;
                default:
                    return false;
            }
        }

        public static string Read(SerializedProperty p)
        {
            if (p == null) return "?";
            switch (p.propertyType)
            {
                case SerializedPropertyType.Float: return p.floatValue.ToString("0.###", CultureInfo.InvariantCulture);
                case SerializedPropertyType.Integer: return p.intValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Boolean: return p.boolValue ? "true" : "false";
                case SerializedPropertyType.String: return string.IsNullOrEmpty(p.stringValue) ? "(empty)" : p.stringValue;
                case SerializedPropertyType.Enum:
                    var names = p.enumDisplayNames;
                    return p.enumValueIndex >= 0 && p.enumValueIndex < names.Length
                        ? names[p.enumValueIndex]
                        : p.enumValueIndex.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Color: return "#" + ColorUtility.ToHtmlStringRGBA(p.colorValue);
                case SerializedPropertyType.Vector2: return FormatVector(p.vector2Value.x, p.vector2Value.y, null);
                case SerializedPropertyType.Vector3: return FormatVector(p.vector3Value.x, p.vector3Value.y, p.vector3Value.z);
                default: return "(unsupported)";
            }
        }

        public static bool TryApply(SerializedProperty p, string raw, out string display)
        {
            display = null;
            if (p == null) return false;
            raw = raw?.Trim() ?? string.Empty;

            switch (p.propertyType)
            {
                case SerializedPropertyType.Float:
                    if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) return false;
                    p.floatValue = f; display = f.ToString("0.###", CultureInfo.InvariantCulture); return true;

                case SerializedPropertyType.Integer:
                    if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int iv))
                    {
                        if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float fi)) return false;
                        iv = Mathf.RoundToInt(fi);
                    }
                    p.intValue = iv; display = iv.ToString(CultureInfo.InvariantCulture); return true;

                case SerializedPropertyType.Boolean:
                    if (!TryParseBool(raw, out bool b)) return false;
                    p.boolValue = b; display = b ? "true" : "false"; return true;

                case SerializedPropertyType.String:
                    p.stringValue = raw; display = string.IsNullOrEmpty(raw) ? "(empty)" : raw; return true;

                case SerializedPropertyType.Enum:
                    int idx = FindEnumIndex(p, raw);
                    if (idx < 0) return false;
                    p.enumValueIndex = idx;
                    var names = p.enumDisplayNames;
                    display = idx < names.Length ? names[idx] : idx.ToString(CultureInfo.InvariantCulture);
                    return true;

                case SerializedPropertyType.Color:
                    if (!TryParseColor(raw, out Color col)) return false;
                    p.colorValue = col; display = "#" + ColorUtility.ToHtmlStringRGBA(col); return true;

                case SerializedPropertyType.Vector2:
                    if (!TryParseVector(raw, 2, out Vector3 v2)) return false;
                    p.vector2Value = new Vector2(v2.x, v2.y); display = FormatVector(v2.x, v2.y, null); return true;

                case SerializedPropertyType.Vector3:
                    if (!TryParseVector(raw, 3, out Vector3 v3)) return false;
                    p.vector3Value = v3; display = FormatVector(v3.x, v3.y, v3.z); return true;

                default:
                    return false;
            }
        }

        #endregion

        #region Component lookup

        public static List<Component> FindAll(GameObject root, string typeName)
        {
            var result = new List<Component>();
            if (root == null || string.IsNullOrWhiteSpace(typeName)) return result;

            string wanted = typeName.Trim();
            foreach (var c in root.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                var t = c.GetType();
                if (string.Equals(t.Name, wanted, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(t.FullName, wanted, StringComparison.OrdinalIgnoreCase))
                    result.Add(c);
            }
            return result;
        }

        public static Component Find(GameObject root, string typeName, int index)
        {
            var list = FindAll(root, typeName);
            return index >= 0 && index < list.Count ? list[index] : null;
        }

        #endregion

        #region Parsing helpers

        static string FormatVector(float x, float y, float? z)
        {
            string sx = x.ToString("0.###", CultureInfo.InvariantCulture);
            string sy = y.ToString("0.###", CultureInfo.InvariantCulture);
            if (!z.HasValue) return $"({sx}, {sy})";
            return $"({sx}, {sy}, {z.Value.ToString("0.###", CultureInfo.InvariantCulture)})";
        }

        static bool TryParseBool(string raw, out bool value)
        {
            switch (raw.ToLowerInvariant())
            {
                case "true": case "1": case "yes": case "on": case "enabled": value = true; return true;
                case "false": case "0": case "no": case "off": case "disabled": value = false; return true;
                default: value = false; return false;
            }
        }

        static int FindEnumIndex(SerializedProperty p, string raw)
        {
            var names = p.enumNames;
            var display = p.enumDisplayNames;
            for (int i = 0; i < names.Length; i++)
                if (string.Equals(names[i], raw, StringComparison.OrdinalIgnoreCase)) return i;
            for (int i = 0; i < display.Length; i++)
                if (string.Equals(display[i], raw, StringComparison.OrdinalIgnoreCase)) return i;
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n >= 0 && n < names.Length)
                return n;
            return -1;
        }

        static bool TryParseColor(string raw, out Color color)
        {
            color = default;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            string hexCandidate = raw.StartsWith("#") ? raw : "#" + raw;
            if (ColorUtility.TryParseHtmlString(raw, out color)) return true;
            if (ColorUtility.TryParseHtmlString(hexCandidate, out color)) return true;

            var parts = raw.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 3 || parts.Length == 4)
            {
                var f = new float[4] { 0, 0, 0, 1 };
                for (int i = 0; i < parts.Length; i++)
                    if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out f[i])) return false;
                color = new Color(f[0], f[1], f[2], f[3]);
                return true;
            }
            return false;
        }

        static bool TryParseVector(string raw, int count, out Vector3 value)
        {
            value = Vector3.zero;
            var parts = raw
                .Replace("(", "").Replace(")", "").Replace("[", "").Replace("]", "")
                .Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < count) return false;

            var comps = new float[3];
            for (int i = 0; i < count; i++)
                if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out comps[i])) return false;

            value = new Vector3(comps[0], comps[1], comps[2]);
            return true;
        }

        #endregion
    }
}
