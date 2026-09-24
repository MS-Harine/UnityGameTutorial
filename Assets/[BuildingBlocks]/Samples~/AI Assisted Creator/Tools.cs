using System;
using System.IO;
using UnityEditor;
using System.Text;
using Blocks.Attack;
using Blocks.Movement;
using Blocks.Character;
using System.Collections.Generic;
using Unity.AI.Assistant.FunctionCalling;

namespace Blocks
{
    /// <summary>
    /// Agent tool that hands the assistant the existing ability code for one kind (movement or attack), so
    /// a proposal can reuse or extend what is already in the project instead of inventing a new class.
    /// </summary>
    public class ReferenceScriptsAgentTools
    {
        #region Agent tool

        /// <summary>
        /// Builds a text dump of one ability kind: every concrete subclass with its source file, the
        /// abstract parent, and the shared CharacterAbility lifecycle base. Surfaced to the assistant as an
        /// agent tool.
        /// </summary>
        /// <param name="kind">"movement" or "attack". Anything else returns a short error string.</param>
        /// <returns>The catalog text, or an error message when the kind is not recognized.</returns>
        [AgentTool(
            AgentPrompts.ToolReadAbilityCatalog,
            "BuildingBlocks.ReadAbilityCatalog")]
        public static string ReadAbilityCatalog(
            [ToolParameter(AgentPrompts.ParamReadAbilityCatalogKind)]
            string kind)
        {
            string normalized = (kind ?? string.Empty).Trim().ToLowerInvariant();
            bool isAttack = normalized == "attack";
            bool isMovement = normalized == "movement";
            if (!isAttack && !isMovement)
                return "kind must be \"movement\" or \"attack\".";

            var sb = new StringBuilder();
            sb.Append("Ability catalog — ").Append(isAttack ? "attack" : "movement").AppendLine(":");
            sb.AppendLine();
            sb.AppendLine(AgentPrompts.ReadAbilityCatalogGuidance);
            sb.AppendLine();

            List<Type> concrete = isAttack ? CollectConcrete<AttackAbility>() : CollectConcrete<MovementAbility>();

            sb.Append("Concrete ").Append(isAttack ? "AttackAbility" : "MovementAbility").Append(" subclasses (").Append(concrete.Count).AppendLine("):");
            sb.AppendLine();
            if (concrete.Count == 0)
                sb.AppendLine("  (none — nothing exists in the project for this kind yet)").AppendLine();
            else
            {
                foreach (Type type in concrete)
                    AppendTypeBlock(sb, type);
            }

            sb.AppendLine("---");
            sb.AppendLine();
            sb.Append("Abstract parent (").Append(isAttack ? "AttackAbility" : "MovementAbility").AppendLine("):");
            sb.AppendLine();
            Type parentType = isAttack ? typeof(AttackAbility) : typeof(MovementAbility);
            AppendTypeBlock(sb, parentType);

            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine(AgentPrompts.ReadAbilityCatalogLifecycleCaption);
            sb.AppendLine();
            AppendTypeBlock(sb, typeof(CharacterAbility));

            return sb.ToString();
        }

        #endregion

        #region Reflection helpers

        /// <summary>
        /// Gathers the non-abstract subclasses of <typeparamref name="T"/> in the project, sorted by name.
        /// </summary>
        /// <returns>The concrete ability types, ordered by type name.</returns>
        static List<Type> CollectConcrete<T>() where T : CharacterAbility
        {
            var list = new List<Type>();
            foreach (Type t in TypeCache.GetTypesDerivedFrom<T>())
            {
                if (t.IsAbstract) continue;
                list.Add(t);
            }
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
            return list;
        }

        /// <summary>
        /// Appends one type's block to <paramref name="sb"/>: its name, XML summary, source path, and the
        /// full file body when the .cs file can be found.
        /// </summary>
        /// <param name="sb">Builder receiving the formatted block.</param>
        /// <param name="type">The ability type to describe.</param>
        static void AppendTypeBlock(StringBuilder sb, Type type)
        {
            string summary = XmlSummaryReader.For(type);
            string path = FindScriptPath(type.Name);

            sb.Append("// ").Append(type.Name);
            if (!string.IsNullOrWhiteSpace(summary))
                sb.Append(" — ").Append(summary);
            sb.AppendLine();
            if (!string.IsNullOrEmpty(path)) sb.Append("// Source: ").AppendLine(path);

            if (string.IsNullOrEmpty(path))
            {
                sb.AppendLine("// (no .cs file found in project)").AppendLine();
                return;
            }

            string body;
            try { body = File.ReadAllText(path); }
            catch (Exception ex) { body = $"// failed to read: {ex.Message}"; }
            sb.AppendLine(body);
            sb.AppendLine();
        }

        /// <summary>
        /// Resolves the asset path of the MonoScript whose file name matches <paramref name="className"/>.
        /// </summary>
        /// <param name="className">The class (and expected file) name to locate.</param>
        /// <returns>The asset path, or null when no matching script exists.</returns>
        static string FindScriptPath(string className)
        {
            foreach (string guid in AssetDatabase.FindAssets($"{className} t:MonoScript"))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(assetPath)) continue;
                if (Path.GetFileNameWithoutExtension(assetPath) != className) continue;
                return assetPath;
            }
            return null;
        }

        #endregion
    }

    /// <summary>
    /// Small parsing and lookup helpers shared by the render tools: lenient enum parsing, resolving an
    /// ability class by name, and turning a raw suggested-ability payload into a clean value.
    /// </summary>
    static class AgentToolHelpers
    {
        #region Enum parsing

        public static T ParseEnum<T>(string raw, T fallback) where T : struct, Enum
            => Enum.TryParse(raw?.Trim(), true, out T value) ? value : fallback;

        public static T? ParseEnumOrNull<T>(string raw) where T : struct, Enum
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            return Enum.TryParse(raw.Trim(), true, out T value) ? value : null;
        }

        public static bool TryParseEnum<T>(string raw, out T value) where T : struct, Enum
        {
            value = default;
            return !string.IsNullOrWhiteSpace(raw) && Enum.TryParse(raw.Trim(), true, out value);
        }

        #endregion

        #region Ability helpers

        /// <summary>
        /// Finds the concrete <typeparamref name="T"/> subclass named <paramref name="typeName"/>,
        /// preferring an exact match and falling back to a case-insensitive one.
        /// </summary>
        /// <param name="typeName">The ability class name to resolve.</param>
        /// <returns>The matching ability type, or null when none matches.</returns>
        public static Type FindAbility<T>(string typeName) where T : CharacterAbility
        {
            if (string.IsNullOrWhiteSpace(typeName)) return null;
            string wanted = typeName.Trim();

            Type caseInsensitive = null;
            foreach (Type t in TypeCache.GetTypesDerivedFrom<T>())
            {
                if (t.IsAbstract) continue;
                if (string.Equals(t.Name, wanted, StringComparison.Ordinal)) return t;
                if (caseInsensitive == null && string.Equals(t.Name, wanted, StringComparison.OrdinalIgnoreCase))
                    caseInsensitive = t;
            }
            return caseInsensitive;
        }

        /// <summary>
        /// Trims a raw <see cref="SuggestedAbilityProposal"/> into a <see cref="SuggestedAbility"/>,
        /// dropping it when both title and prompt are blank.
        /// </summary>
        /// <param name="s">The raw proposal from the assistant.</param>
        /// <returns>The cleaned suggestion, or null when there is nothing to suggest.</returns>
        public static SuggestedAbility Sanitize(SuggestedAbilityProposal s)
        {
            if (s == null) return null;
            if (string.IsNullOrWhiteSpace(s.Title) && string.IsNullOrWhiteSpace(s.Prompt)) return null;
            return new SuggestedAbility
            {
                Title = s.Title?.Trim() ?? string.Empty,
                Prompt = s.Prompt?.Trim() ?? string.Empty,
            };
        }

        #endregion
    }

    #region Proposal data types

    /// <summary>
    /// An existing project class the assistant flags as already covering the requested ability, with tips
    /// for using it as-is or tweaking it.
    /// </summary>
    public sealed class ExistingAbilityMatch
    {
        public string ClassName { get; set; }
        public string Summary { get; set; }
        public List<string> ConfigurationTips { get; set; } = new();
        public List<string> ModificationTips { get; set; } = new();
    }

    /// <summary>A cleaned title and prompt pair the Creator offers as a buildable suggestion.</summary>
    sealed class SuggestedAbility
    {
        public string Title;
        public string Prompt;
    }

    /// <summary>Raw title and prompt pair the assistant proposes for a new ability, before sanitizing.</summary>
    public sealed class SuggestedAbilityProposal
    {
        public string Title { get; set; } = string.Empty;
        public string Prompt { get; set; } = string.Empty;
    }

    #endregion
}
