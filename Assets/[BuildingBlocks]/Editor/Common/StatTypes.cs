using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using Blocks.Character;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using System.Text.RegularExpressions;

namespace Blocks
{
    /// <summary>
    /// Adds new entries to the StatType enum from the editor. Validates a proposed name, then writes the
    /// enum value into StatType.cs and a matching color case into StatTypeDefaults.cs and reimports both.
    /// The new type becomes available after scripts reload.
    /// </summary>
    static class StatTypeRegistry
    {
        const string k_StatTypePath = "Assets/[BuildingBlocks]/Runtime/Character/StatType.cs";
        const string k_DefaultsPath = "Assets/[BuildingBlocks]/Runtime/Character/StatTypeDefaults.cs";

        static readonly Regex k_Identifier = new Regex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

        public static bool IsValidNewName(string name, out string error)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                error = "Name is required.";
                return false;
            }
            if (!k_Identifier.IsMatch(name))
            {
                error = "Use letters, digits, and underscores. Must start with a letter or underscore.";
                return false;
            }
            foreach (var existing in Enum.GetNames(typeof(StatType)))
            {
                if (string.Equals(existing, name, StringComparison.OrdinalIgnoreCase))
                {
                    error = $"\"{existing}\" already exists.";
                    return false;
                }
            }
            error = string.Empty;
            return true;
        }

        public static void WriteNew(string name, Color defaultColor)
        {
            if (!IsValidNewName(name, out string err)) throw new InvalidOperationException(err);

            string enumPath = Path.GetFullPath(k_StatTypePath);
            string defaultsPath = Path.GetFullPath(k_DefaultsPath);

            string enumSrc = File.ReadAllText(enumPath);
            int nextValue = ComputeNextEnumValue(enumSrc);
            string newEnumSrc = AppendEnumEntry(enumSrc, name, nextValue);
            File.WriteAllText(enumPath, newEnumSrc);

            string defaultsSrc = File.ReadAllText(defaultsPath);
            string newDefaultsSrc = InsertColorCase(defaultsSrc, name, defaultColor);
            File.WriteAllText(defaultsPath, newDefaultsSrc);

            AssetDatabase.ImportAsset(k_StatTypePath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(k_DefaultsPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();
        }

        static int ComputeNextEnumValue(string src)
        {
            int max = -1;
            var matches = Regex.Matches(src, @"\b[A-Za-z_][A-Za-z0-9_]*\s*=\s*(\d+)");
            foreach (Match m in matches)
            {
                if (int.TryParse(m.Groups[1].Value, out int v) && v > max) max = v;
            }
            return max + 1;
        }

        static string AppendEnumEntry(string src, string name, int value)
        {
            var bodyMatch = Regex.Match(src, @"enum\s+StatType\s*\{(?<body>[^}]*)\}", RegexOptions.Singleline);
            if (!bodyMatch.Success) throw new InvalidOperationException("Could not locate StatType enum body.");

            string body = bodyMatch.Groups["body"].Value;
            string trimmed = body.TrimEnd(' ', '\t', '\r', '\n');
            if (!trimmed.EndsWith(",")) trimmed += ",";

            const string entryIndent = "        ";
            const string closeIndent = "    ";
            string newBody = trimmed + "\n" + entryIndent + name + " = " + value + "\n" + closeIndent;

            int start = bodyMatch.Groups["body"].Index;
            int len = bodyMatch.Groups["body"].Length;
            return src.Substring(0, start) + newBody + src.Substring(start + len);
        }

        static string InsertColorCase(string src, string name, Color color)
        {
            int idx = src.IndexOf("default:", StringComparison.Ordinal);
            if (idx < 0) throw new InvalidOperationException("Could not locate default: in StatTypeDefaults.cs.");

            int lineStart = src.LastIndexOf('\n', idx) + 1;
            string line = $"                case StatType.{name}: return new Color({color.r:F3}f, {color.g:F3}f, {color.b:F3}f, 1f);\n";
            return src.Substring(0, lineStart) + line + src.Substring(lineStart);
        }
    }

    /// <summary>
    /// Editor popup for creating a new stat type. Collects a name and default color, validates the name
    /// live against StatTypeRegistry, and writes the type when the user clicks Create.
    /// </summary>
    sealed class NewStatTypePopup : EditorWindow
    {
        TextField m_NameField;
        ColorField m_ColorField;
        Label m_Error;
        Button m_CreateBtn;

        public new static void ShowPopup()
        {
            var win = CreateInstance<NewStatTypePopup>();
            win.titleContent = new GUIContent("New Stat Type");
            win.minSize = new Vector2(360, 200);
            win.maxSize = new Vector2(360, 200);
            win.ShowUtility();
        }

        void CreateGUI()
        {
            var root = rootVisualElement;
            BBStyles.Apply(root);
            root.style.paddingLeft = 12;
            root.style.paddingRight = 12;
            root.style.paddingTop = 12;
            root.style.paddingBottom = 12;

            root.Add(Heading.Title("Create a new stat type"));

            var label = new Label("Adds a new entry to the StatType enum. Scripts will reload after creation.");
            label.AddToClassList("blocks-text--dim");
            label.style.whiteSpace = WhiteSpace.Normal;
            root.Add(label);

            m_NameField = new TextField("Name");
            m_NameField.RegisterValueChangedCallback(_ => Validate());
            root.Add(m_NameField);

            m_ColorField = new ColorField("Default color") { value = new Color(0.6f, 0.6f, 0.6f), showAlpha = false };
            root.Add(m_ColorField);

            m_Error = new Label();
            m_Error.AddToClassList("blocks-text--warn");
            m_Error.style.minHeight = 16;
            m_Error.style.marginTop = 4;
            m_Error.style.whiteSpace = WhiteSpace.Normal;
            root.Add(m_Error);

            var buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.justifyContent = Justify.FlexEnd;
            buttons.style.marginTop = 8;

            var cancel = Buttons.Secondary("Cancel", Close);
            cancel.style.marginRight = 6;
            buttons.Add(cancel);

            m_CreateBtn = Buttons.Primary("Create", OnCreate);
            buttons.Add(m_CreateBtn);

            root.Add(buttons);
            Validate();
        }

        void Validate()
        {
            string typeName = m_NameField?.value?.Trim() ?? string.Empty;
            bool ok = StatTypeRegistry.IsValidNewName(typeName, out string err);
            m_Error.text = ok ? string.Empty : err;
            m_CreateBtn?.SetEnabled(ok);
        }

        void OnCreate()
        {
            string typeName = m_NameField.value.Trim();
            if (!StatTypeRegistry.IsValidNewName(typeName, out string err))
            {
                m_Error.text = err;
                return;
            }
            try
            {
                StatTypeRegistry.WriteNew(typeName, m_ColorField.value);
                Close();
            }
            catch (Exception e)
            {
                m_Error.text = e.Message;
            }
        }
    }
}
