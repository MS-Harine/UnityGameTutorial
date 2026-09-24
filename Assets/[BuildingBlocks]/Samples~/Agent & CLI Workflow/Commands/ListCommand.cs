using System;
using System.Text;
using UnityEditor;
using Blocks.Attack;
using Blocks.Movement;
using System.Reflection;
using Unity.Pipeline.Commands;
using System.Collections.Generic;

namespace Blocks.Cli
{
    /// <summary>
    /// bb_list — the front door. Prints every bb_* command in the project, discovered by reflection rather
    /// than from a hand-kept list, so a command someone adds to this sample shows up here the moment it
    /// compiles.
    /// </summary>
    static class ListCommand
    {
        /// <summary>The prefix that marks a command as this template's own, rather than a Pipeline built-in.</summary>
        public const string Prefix = "bb_";

        // The wizard's "print the listing to the Console" button lives in the assembly that compiles with or
        // without the Pipeline package, so it cannot name this class. Handing the listing over on load lets
        // it show that button only when there is something to print — see CliHome.ProvideListing.
        [InitializeOnLoadMethod]
        static void ProvideListing() => CliHome.ProvideListing(() => List(detail: false));

        [CliCommand("bb_list", "List the Building Blocks commands available in this project.")]
        public static string List(
            [CliArg("detail", "Also print each command's arguments, and the ability classes in this project.")]
            bool detail = false)
        {
            List<MethodInfo> commands = Collect();

            var sb = new StringBuilder();
            sb.Append(Layout.Heading($"{commands.Count} commands over the Unity CLI"));
            sb.AppendLine();

            if (commands.Count == 0)
            {
                sb.Append(Layout.Text("No bb_ commands found. Import the Agent & CLI Workflow sample from the Package Manager."));
                return sb.ToString();
            }

            var names = new List<string>(commands.Count);
            var descriptions = new List<string>(commands.Count);
            for (int i = 0; i < commands.Count; i++)
            {
                CliCommandAttribute attribute = Attribute(commands[i]);
                names.Add(attribute.Name);
                descriptions.Add(attribute.Description);
            }

            sb.Append(Layout.Rows(names, descriptions, spaced: true));
            sb.AppendLine();
            sb.Append(Layout.Rule());
            sb.AppendLine();

            if (!detail)
            {
                sb.Append(Layout.Text("Type unity command bb_list --detail true to see arguments."));
                return sb.ToString();
            }

            for (int i = 0; i < commands.Count; i++)
            {
                sb.Append(Arguments(commands[i]));
                sb.AppendLine();
            }

            sb.Append(Abilities());
            return sb.ToString();
        }

        #region Discovery

        /// <summary>
        /// Every bb_* command in the loaded Editor assemblies, sorted by name. Uses the same TypeCache scan
        /// the Pipeline package registers commands through, so this listing cannot drift from what is
        /// actually callable.
        /// </summary>
        static List<MethodInfo> Collect()
        {
            var found = new List<MethodInfo>();
            foreach (MethodInfo method in TypeCache.GetMethodsWithAttribute<CliCommandAttribute>())
            {
                CliCommandAttribute attribute = Attribute(method);
                if (attribute == null || attribute.Name == null) continue;
                if (!attribute.Name.StartsWith(Prefix, StringComparison.Ordinal)) continue;
                found.Add(method);
            }

            found.Sort((a, b) => string.Compare(Attribute(a).Name, Attribute(b).Name, StringComparison.Ordinal));
            return found;
        }

        static CliCommandAttribute Attribute(MethodInfo method) =>
            method.GetCustomAttribute<CliCommandAttribute>();

        #endregion

        #region Rendering

        /// <summary>One command's arguments: the flag, its type, whether it is required, and what it does.</summary>
        static string Arguments(MethodInfo method)
        {
            CliCommandAttribute command = Attribute(method);
            ParameterInfo[] parameters = method.GetParameters();

            var sb = new StringBuilder();
            sb.Append(Layout.Section($"unity command {command.Name}"));
            sb.AppendLine();

            if (parameters.Length == 0)
            {
                sb.Append(Layout.Rows(new[] { "(no arguments)" }, new[] { string.Empty }));
                return sb.ToString();
            }

            var flags = new List<string>(parameters.Length);
            var descriptions = new List<string>(parameters.Length);

            for (int i = 0; i < parameters.Length; i++)
            {
                ParameterInfo parameter = parameters[i];
                var arg = parameter.GetCustomAttribute<CliArgAttribute>();

                string name = arg?.Name ?? parameter.Name;
                bool required = arg != null ? arg.Required || !parameter.HasDefaultValue : !parameter.HasDefaultValue;

                flags.Add("--" + name);
                descriptions.Add($"{TypeName(parameter.ParameterType)}{(required ? " (required)" : string.Empty)} · {arg?.Description}");
            }

            sb.Append(Layout.Rows(flags, descriptions));
            return sb.ToString();
        }

        /// <summary>
        /// The ability classes in this project, listed because <c>bb_character_draft</c> takes them by class
        /// name and there is no other way to learn what those names are. They are project-specific: an
        /// ability the Creator generated last week is as valid here as one that shipped with the template.
        /// </summary>
        static string Abilities()
        {
            var sb = new StringBuilder();
            sb.Append(Layout.Section("abilities in this project"));
            sb.AppendLine();
            sb.Append(Layout.Rows(
                new[] { "movement", "attack" },
                new[]
                {
                    Layout.Join(AbilityLookup.Names<MovementAbility>()),
                    Layout.Join(AbilityLookup.Names<AttackAbility>()),
                }));
            return sb.ToString();
        }

        /// <summary>The short type name a CLI user would recognise, rather than the CLR name.</summary>
        static string TypeName(Type type)
        {
            if (type == typeof(bool)) return "bool";
            if (type == typeof(int)) return "int";
            if (type == typeof(float)) return "float";
            if (type == typeof(string)) return "string";
            return type.Name;
        }

        #endregion
    }
}
