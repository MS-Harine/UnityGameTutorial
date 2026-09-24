using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine.UIElements;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Blocks
{
    /// <summary>
    /// UI Toolkit field extensions. CompactInlineLabel shrinks a field's built-in label to a fixed width so
    /// the label and input sit together on one line.
    /// </summary>
    static class EditorFieldExtensions
    {
        public static void CompactInlineLabel(this VisualElement field, float labelWidth)
        {
            var label = field.Q<Label>(className: "unity-base-field__label");
            if (label == null) return;
            label.style.minWidth = labelWidth;
            label.style.width = labelWidth;
            label.style.marginRight = 2;
        }
    }

    /// <summary>
    /// Resolves the Building Blocks editor folder by locating the Blocks.Editor assembly definition, so
    /// asset paths keep working if the folder moves. Falls back to a default path and caches the result.
    /// </summary>
    static class EditorPaths
    {
        const string k_Fallback = "Assets/[BuildingBlocks]/Editor";
        static string s_EditorFolder;

        public static string EditorFolder
        {
            get
            {
                if (!string.IsNullOrEmpty(s_EditorFolder)) return s_EditorFolder;

                foreach (string guid in AssetDatabase.FindAssets("Blocks.Editor t:asmdef"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path)) continue;
                    if (Path.GetFileName(path) != "Blocks.Editor.asmdef") continue;
                    s_EditorFolder = Path.GetDirectoryName(path)?.Replace('\\', '/');
                    if (!string.IsNullOrEmpty(s_EditorFolder)) return s_EditorFolder;
                }

                s_EditorFolder = k_Fallback;
                return s_EditorFolder;
            }
        }
    }

    /// <summary>
    /// Read helpers that pull typed values (enum name, float, bool, string) out of a SerializedObject or
    /// SerializedProperty, returning a safe default when the property is missing.
    /// </summary>
    static class SerializedRead
    {
        public static string StatTypeName(SerializedProperty typeProp)
        {
            var names = typeProp.enumDisplayNames;
            int idx = typeProp.enumValueIndex;
            return idx >= 0 && idx < names.Length ? names[idx] : "?";
        }

        public static string EnumName(SerializedObject so, string path)
        {
            var p = so.FindProperty(path);
            if (p == null) return "?";
            var names = p.enumDisplayNames;
            return p.enumValueIndex >= 0 && p.enumValueIndex < names.Length ? names[p.enumValueIndex] : "?";
        }

        public static float FloatVal(SerializedObject so, string path)
        {
            var p = so.FindProperty(path);
            return p?.floatValue ?? 0f;
        }

        public static bool BoolVal(SerializedObject so, string path)
        {
            var p = so.FindProperty(path);
            return p != null && p.boolValue;
        }

        public static string StringVal(SerializedObject so, string path)
        {
            var p = so.FindProperty(path);
            return p != null ? p.stringValue : string.Empty;
        }
    }

    /// <summary>
    /// Reads the XML doc summary written above a type's class declaration straight from its .cs source, so
    /// the ability catalog can show each class's own summary. Results are cached per type.
    /// </summary>
    public static class XmlSummaryReader
    {
        static readonly Dictionary<Type, string> k_Cache = new Dictionary<Type, string>();

        public static string For(Type type)
        {
            if (type == null) return string.Empty;
            if (k_Cache.TryGetValue(type, out var cached)) return cached;

            string summary = ReadFromSource(type) ?? string.Empty;
            k_Cache[type] = summary;
            return summary;
        }

        static string ReadFromSource(Type type)
        {
            string path = FindScriptPath(type);
            if (string.IsNullOrEmpty(path)) return null;

            string[] lines;
            try { lines = File.ReadAllLines(path); }
            catch { return null; }

            int classLine = FindClassDeclarationLine(lines, type.Name);
            if (classLine < 0) return null;

            return ExtractSummaryAbove(lines, classLine);
        }

        static string FindScriptPath(Type type)
        {
            foreach (string guid in AssetDatabase.FindAssets($"{type.Name} t:MonoScript"))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(assetPath)) continue;
                if (Path.GetFileNameWithoutExtension(assetPath) != type.Name) continue;
                return assetPath;
            }
            return null;
        }

        static int FindClassDeclarationLine(string[] lines, string typeName)
        {
            var pattern = new Regex(@"\bclass\s+" + Regex.Escape(typeName) + @"\b");
            for (int i = 0; i < lines.Length; i++)
            {
                if (pattern.IsMatch(lines[i])) return i;
            }
            return -1;
        }

        static string ExtractSummaryAbove(string[] lines, int classLine)
        {
            int end = classLine - 1;
            while (end >= 0)
            {
                string trimmed = lines[end].TrimStart();
                if (trimmed.StartsWith("///")) break;
                if (trimmed.StartsWith("[") || trimmed.Length == 0) { end--; continue; }
                return null;
            }
            if (end < 0) return null;

            int start = end;
            while (start - 1 >= 0 && lines[start - 1].TrimStart().StartsWith("///"))
                start--;

            var sb = new StringBuilder();
            bool inSummary = false;
            for (int i = start; i <= end; i++)
            {
                string content = lines[i].TrimStart().Substring(3).Trim();

                if (content.StartsWith("<summary>", StringComparison.OrdinalIgnoreCase))
                {
                    inSummary = true;
                    content = content.Substring("<summary>".Length).Trim();
                }

                if (content.EndsWith("</summary>", StringComparison.OrdinalIgnoreCase))
                {
                    content = content.Substring(0, content.Length - "</summary>".Length).Trim();
                    if (content.Length > 0) AppendWithSpace(sb, content);
                    inSummary = false;
                    continue;
                }

                if (!inSummary && (content.StartsWith("<") || content.Length == 0)) continue;
                if (content.Length == 0) continue;

                AppendWithSpace(sb, content);
            }

            return sb.ToString();
        }

        static void AppendWithSpace(StringBuilder sb, string text)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(text);
        }
    }
}
