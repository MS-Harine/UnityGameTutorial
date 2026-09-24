using System;
using System.IO;
using System.Text;
using UnityEditor;
using System.Globalization;

namespace Blocks
{
    /// <summary>
    /// Turns an AbilityScriptSpec into C# source for a new ability. Emits the fields, lifecycle hooks, and a
    /// scaffold of commented hints for the chosen template and trigger, leaving the real logic for the user
    /// to fill in. Also sanitizes class names and namespaces.
    /// </summary>
    static class AbilityScriptGenerator
    {
        #region Generation

        public static string Generate(AbilityScriptSpec spec)
        {
            string baseClass = spec.Kind == AbilityKind.Movement ? "MovementAbility" : "AttackAbility";
            string baseUsing = spec.Kind == AbilityKind.Movement ? "Blocks.Movement" : "Blocks.Attack";
            string className = SanitizeClassName(spec.ClassName, spec.Kind);
            string ns = SanitizeNamespace(spec.Namespace, spec.Kind);
            string inputField = ToCamelCase(SanitizeIdentifier(spec.InputFieldName, "abilityAction"));

            bool isAuto = spec.Trigger == AbilityTrigger.Auto;
            bool isOnInput = !isAuto;

            bool isHold = isAuto
                ? spec.Template == AbilityTemplate.ContinuousHold
                : spec.Activation == AbilityActivation.Hold;
            bool isPress = !isHold;

            bool useTimer = spec.UseTimer;
            bool useCooldown = spec.UseCooldown;
            bool useBuffer = isOnInput && isPress && spec.UseInputBuffer;
            bool needsCooldownField = useCooldown && (useTimer || isHold || isAuto);
            bool useInputHandle = isOnInput && isHold;
            bool useActiveFlag = isHold && !useTimer;
            bool emitTryUse = isAuto && isPress && !useTimer;
            bool hasStat = spec.UseStat;
            bool statPerSecond = hasStat && spec.StatPerSecond;
            bool statOneShot = hasStat && !spec.StatPerSecond;
            bool isAttack = spec.Kind == AbilityKind.Attack;

            var sb = new StringBuilder();

            AppendFileHeader(sb, className, spec.Kind, isOnInput, inputField);

            sb.AppendLine("using UnityEngine;");
            if (isOnInput) sb.AppendLine("using UnityEngine.InputSystem;");
            sb.AppendLine("using Blocks.Character;");
            sb.AppendLine($"using {baseUsing};");
            sb.AppendLine();
            sb.AppendLine($"namespace {ns}");
            sb.AppendLine("{");
            sb.AppendLine($"    public sealed class {className} : {baseClass}");
            sb.AppendLine("    {");

            if (isOnInput)
            {
                sb.AppendLine("        [Header(\"Input\")]");
                sb.AppendLine($"        [SerializeField] InputActionReference {inputField};");
                sb.AppendLine();
            }

            bool hasSettingsHeader = useTimer || useCooldown || useBuffer || isAttack;
            if (hasSettingsHeader)
            {
                sb.AppendLine("        [Header(\"Settings\")]");
                if (useTimer) sb.AppendLine($"        [SerializeField] float duration = {Float(spec.Duration)};");
                if (useCooldown) sb.AppendLine($"        [SerializeField] float cooldown = {Float(spec.Cooldown)};");
                if (useBuffer) sb.AppendLine($"        [SerializeField] float bufferTime = {Float(spec.InputBuffer)};");
                if (isAttack)
                {
                    sb.AppendLine($"        [SerializeField] float damage = {Float(spec.Damage)};");
                    sb.AppendLine($"        [SerializeField] float range = {Float(spec.Range)};");
                }
                sb.AppendLine();
            }

            if (hasStat)
            {
                sb.AppendLine("        [Header(\"Stat Cost\")]");
                sb.AppendLine($"        [SerializeField] StatType statType = StatType.{spec.StatType};");
                sb.AppendLine($"        [SerializeField] float statCost = {Float(spec.StatCost)};");
                sb.AppendLine();
            }

            if (useTimer) sb.AppendLine("        float m_ActiveRemaining;");
            if (needsCooldownField) sb.AppendLine("        float m_CooldownRemaining;");
            if (useInputHandle) sb.AppendLine("        InputHandle m_InputHandle;");
            if (useActiveFlag) sb.AppendLine("        bool m_IsActive;");
            if (useTimer || needsCooldownField || useInputHandle || useActiveFlag) sb.AppendLine();

            if (isOnInput)
            {
                sb.AppendLine("        protected override void OnInitialize()");
                sb.AppendLine("        {");
                sb.AppendLine(useInputHandle
                    ? $"            m_InputHandle = BindInput({inputField}, ConfigureInput);"
                    : $"            BindInput({inputField}, ConfigureInput);");
                sb.AppendLine("        }");
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("        protected override void OnInitialize()");
                sb.AppendLine("        {");
                sb.AppendLine("            // YOUR LOGIC — cache references or set up state here.");
                sb.AppendLine("        }");
                sb.AppendLine();
            }

            bool needsCleanup = useTimer || isHold || needsCooldownField;
            if (needsCleanup)
            {
                sb.AppendLine("        protected override void OnCleanup()");
                sb.AppendLine("        {");
                if (useTimer || isHold) sb.AppendLine("            StopAbility();");
                if (useTimer) sb.AppendLine("            m_ActiveRemaining = 0f;");
                if (needsCooldownField) sb.AppendLine("            m_CooldownRemaining = 0f;");
                sb.AppendLine("        }");
                sb.AppendLine();
            }

            if (isOnInput)
            {
                sb.AppendLine("        void ConfigureInput(InputHandle input)");
                sb.AppendLine("        {");
                if (isPress)
                {
                    sb.AppendLine("            input.Performed = StartAbility;");
                    sb.AppendLine("            input.CanExecute = CanStartAbility;");
                    if (useBuffer) sb.AppendLine("            input.BufferTime = bufferTime;");
                    if (!useTimer && useCooldown) sb.AppendLine("            input.CooldownTime = cooldown;");
                }
                else
                {
                    sb.AppendLine("            input.Started = StartAbility;");
                    sb.AppendLine("            input.Canceled = StopAbility;");
                }
                sb.AppendLine("        }");
                sb.AppendLine();
            }

            sb.AppendLine("        bool CanStartAbility()");
            sb.AppendLine("        {");
            if (useTimer)
            {
                string runningCheck = needsCooldownField
                    ? "m_ActiveRemaining > 0f || m_CooldownRemaining > 0f"
                    : "m_ActiveRemaining > 0f";
                sb.AppendLine($"            if ({runningCheck}) return false;");
            }
            else if (needsCooldownField)
            {
                sb.AppendLine("            if (m_CooldownRemaining > 0f) return false;");
            }

            if (useActiveFlag && isAuto) sb.AppendLine("            if (m_IsActive) return false;");

            if (statOneShot)
            {
                sb.AppendLine("            if (!Character.HasAtLeast(statType, statCost)) return false;");
            }
            else if (statPerSecond)
            {
                sb.AppendLine("            if (Character.GetValue(statType) <= 0f) return false;");
            }

            sb.AppendLine("            // YOUR LOGIC — add gating conditions below");
            sb.AppendLine("            return true;");
            sb.AppendLine("        }");
            sb.AppendLine();

            if (emitTryUse)
            {
                sb.AppendLine("        void TryUseAbility()");
                sb.AppendLine("        {");
                sb.AppendLine("            if (!CanStartAbility()) return;");
                if (statOneShot) sb.AppendLine("            Character.Consume(statType, statCost);");
                if (needsCooldownField) sb.AppendLine("            m_CooldownRemaining = cooldown;");
                sb.AppendLine("            ExecuteAbility();");
                sb.AppendLine("        }");
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("        void StartAbility()");
                sb.AppendLine("        {");
                if (isHold || isAuto) sb.AppendLine("            if (!CanStartAbility()) return;");
                if (statOneShot) sb.AppendLine("            Character.Consume(statType, statCost);");

                if (useTimer)
                {
                    sb.AppendLine("            m_ActiveRemaining = duration;");
                    sb.AppendLine("            OnAbilityStarted();");
                }
                else if (isPress)
                {
                    sb.AppendLine("            ExecuteAbility();");
                }
                else
                {
                    sb.AppendLine("            m_IsActive = true;");
                    sb.AppendLine("            OnAbilityStarted();");
                }
                sb.AppendLine("        }");
                sb.AppendLine();
            }

            if (useTimer || isHold)
            {
                sb.AppendLine("        void StopAbility()");
                sb.AppendLine("        {");
                if (useTimer)
                {
                    sb.AppendLine("            if (m_ActiveRemaining <= 0f) return;");
                    sb.AppendLine("            m_ActiveRemaining = 0f;");
                    if (needsCooldownField) sb.AppendLine("            m_CooldownRemaining = cooldown;");
                    sb.AppendLine("            OnAbilityStopped();");
                }
                else
                {
                    sb.AppendLine("            if (!m_IsActive) return;");
                    sb.AppendLine("            m_IsActive = false;");
                    if (needsCooldownField) sb.AppendLine("            m_CooldownRemaining = cooldown;");
                    sb.AppendLine("            OnAbilityStopped();");
                }
                sb.AppendLine("        }");
                sb.AppendLine();
            }

            sb.AppendLine("        protected override void OnUpdate()");
            sb.AppendLine("        {");

            if (isAuto) AppendAutoTriggerHint(sb, useTimer, isHold, emitTryUse, isAttack);

            if (needsCooldownField)
            {
                sb.AppendLine("            if (m_CooldownRemaining > 0f) m_CooldownRemaining -= Time.deltaTime;");
            }

            if (useTimer)
            {
                sb.AppendLine();
                sb.AppendLine("            if (m_ActiveRemaining <= 0f) return;");
                sb.AppendLine();
                if (statPerSecond)
                {
                    sb.AppendLine("            if (!Character.Consume(statType, statCost * Time.deltaTime))");
                    sb.AppendLine("            {");
                    sb.AppendLine("                StopAbility();");
                    sb.AppendLine("                return;");
                    sb.AppendLine("            }");
                    sb.AppendLine();
                }
                sb.AppendLine("            UpdateAbility();");
                sb.AppendLine();
                sb.AppendLine("            m_ActiveRemaining -= Time.deltaTime;");
                sb.AppendLine("            if (m_ActiveRemaining <= 0f)");
                sb.AppendLine("            {");
                sb.AppendLine("                m_ActiveRemaining = 0f;");
                if (needsCooldownField) sb.AppendLine("                m_CooldownRemaining = cooldown;");
                sb.AppendLine("                OnAbilityStopped();");
                sb.AppendLine("            }");
            }
            else if (isHold)
            {
                sb.AppendLine();
                sb.AppendLine(useInputHandle
                    ? "            if (m_InputHandle == null || !m_InputHandle.IsPressed) return;"
                    : "            if (!m_IsActive) return;");
                sb.AppendLine();
                if (statPerSecond)
                {
                    sb.AppendLine("            if (!Character.Consume(statType, statCost * Time.deltaTime))");
                    sb.AppendLine("            {");
                    sb.AppendLine("                StopAbility();");
                    sb.AppendLine("                return;");
                    sb.AppendLine("            }");
                    sb.AppendLine();
                }
                sb.AppendLine("            UpdateAbility();");
            }
            sb.AppendLine("        }");
            sb.AppendLine();

            sb.AppendLine("        // ─── YOUR LOGIC — modify below ───────────────────────────────");
            sb.AppendLine();
            if (isPress && !useTimer)
            {
                sb.AppendLine("        void ExecuteAbility()");
                sb.AppendLine("        {");
                AppendBodyHints(sb, isAttack, oneShot: true);
                sb.AppendLine("        }");
            }
            else
            {
                sb.AppendLine("        void OnAbilityStarted()");
                sb.AppendLine("        {");
                sb.AppendLine("            // Setup when the ability begins.");
                if (isAttack) sb.AppendLine("            // Character.FaceTarget();");
                sb.AppendLine("        }");
                sb.AppendLine();
                sb.AppendLine("        void UpdateAbility()");
                sb.AppendLine("        {");
                AppendBodyHints(sb, isAttack, oneShot: false);
                sb.AppendLine("        }");
                sb.AppendLine();
                sb.AppendLine("        void OnAbilityStopped()");
                sb.AppendLine("        {");
                sb.AppendLine("            // Cleanup when the ability ends.");
                sb.AppendLine("        }");
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        // The header answers, in the file itself, the three questions testers hit: is this file mine
        // (yes), how do I use it (attach it from the character's card), and where does my code go.
        static void AppendFileHeader(StringBuilder sb, string className, AbilityKind kind, bool isOnInput, string inputField)
        {
            string card = kind == AbilityKind.Movement ? "Movement" : "Attack";
            string attach = kind == AbilityKind.Movement
                ? $"select a character and add {className} from the {card} card"
                : $"select a character and pick {className} on the {card} card";

            sb.AppendLine($"// {className} — created with the Building Blocks Creator.");
            sb.AppendLine("//");
            sb.AppendLine("// This file is yours: edit, rename, move, or delete it freely. Nothing regenerates or");
            sb.AppendLine("// overwrites it.");
            sb.AppendLine("//");
            sb.AppendLine($"// To use it, {attach} of its BuildingBlocksCharacter");
            sb.AppendLine("// component. The character picks it up automatically — there is nothing else to wire up.");
            if (isOnInput)
            {
                sb.AppendLine("//");
                sb.AppendLine($"// Input: after attaching, assign the \"{inputField}\" field in the Inspector with an action");
                sb.AppendLine("// from the project's Input Actions asset (see \"Adding a key\" in the Readme).");
            }
            sb.AppendLine("//");
            sb.AppendLine("// Your gameplay goes in the methods under the \"YOUR LOGIC\" markers below.");
            sb.AppendLine();
        }

        static void AppendAutoTriggerHint(StringBuilder sb, bool useTimer, bool isHold, bool emitTryUse, bool isAttack)
        {
            string targetCheck = isAttack ? "Character.HasTargetInRange(range)" : "ShouldFire()";

            sb.AppendLine("            // YOUR LOGIC — decide when this AI-driven ability fires.");
            if (emitTryUse)
            {
                sb.AppendLine($"            // if ({targetCheck}) TryUseAbility();");
            }
            else if (isHold && !useTimer)
            {
                sb.AppendLine($"            // if ({targetCheck} && !m_IsActive) StartAbility();");
                sb.AppendLine($"            // if (m_IsActive && !{targetCheck}) StopAbility();");
            }
            else
            {
                sb.AppendLine($"            // if ({targetCheck}) StartAbility();");
            }
            sb.AppendLine();
        }

        static void AppendBodyHints(StringBuilder sb, bool isAttack, bool oneShot)
        {
            if (isAttack)
            {
                sb.AppendLine("            // Character.FaceTarget();");
                if (oneShot)
                {
                    sb.AppendLine("            // Character.NotifyAttackPerformed();");
                    sb.AppendLine("            // Character.HitBox(new Vector2(0.75f, 0f), new Vector2(1.5f, 1f), damage, HitReaction.PushBack(15f));");
                }
                else
                {
                    sb.AppendLine("            // Character.HitBox(new Vector2(0.75f, 0f), new Vector2(1.5f, 1f), damage * Time.deltaTime);");
                }
            }
            else
            {
                sb.AppendLine("            // Character.Jump(2f);");
                sb.AppendLine(oneShot
                    ? "            // Character.AddMovement(new Vector2(10f, 0f));"
                    : "            // Character.AddMovement(new Vector2(8f * Time.deltaTime, 0f));");
                sb.AppendLine("            // Character.SetVerticalVelocity(0f);");
            }
        }

        #endregion

        #region Sanitizing & formatting

        public static string SanitizeClassName(string raw, AbilityKind kind)
        {
            string sanitized = SanitizeIdentifier(raw, "NewAbility");
            const string suffix = "Ability";
            if (!sanitized.EndsWith(suffix, StringComparison.Ordinal)) sanitized += suffix;
            return sanitized;
        }

        public static string SanitizeNamespace(string raw, AbilityKind kind)
        {
            string fallback = kind == AbilityKind.Movement
                ? "Blocks.Movement.Examples"
                : "Blocks.Attack";
            if (string.IsNullOrWhiteSpace(raw)) return fallback;

            string[] segments = raw.Split('.');
            var sb = new StringBuilder();
            foreach (string part in segments)
            {
                string segment = SanitizeIdentifier(part, "Ns");
                if (sb.Length > 0) sb.Append('.');
                sb.Append(segment);
            }
            return sb.Length == 0 ? fallback : sb.ToString();
        }

        static string SanitizeIdentifier(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            var sb = new StringBuilder();
            foreach (char c in value)
            {
                if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
            }
            string result = sb.ToString();
            if (string.IsNullOrWhiteSpace(result)) return fallback;
            if (!char.IsLetter(result[0]) && result[0] != '_') result = "_" + result;
            return result;
        }

        static string ToCamelCase(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            if (value.Length == 1) return value.ToLowerInvariant();
            return char.ToLowerInvariant(value[0]) + value.Substring(1);
        }

        static string Float(float value) => value.ToString("0.###", CultureInfo.InvariantCulture) + "f";

        #endregion
    }

    /// <summary>
    /// Writes a generated ability script to disk. Validates the output folder, confirms before overwriting,
    /// then writes the file, refreshes the AssetDatabase, and selects the new script.
    /// </summary>
    static class AbilityScriptWriter
    {
        /// <summary>
        /// True when the spec's output folder is a real folder in this project, explaining why not if it
        /// isn't. The Folder field is free text, so every route that puts a script on disk — writing one
        /// here, or handing the path to the Assistant — checks through this and refuses the same way.
        /// </summary>
        public static bool ValidateOutputFolder(AbilityScriptSpec spec)
        {
            string folderPath = spec.OutputFolderPath;
            if (!string.IsNullOrWhiteSpace(folderPath) && AssetDatabase.IsValidFolder(folderPath))
                return true;

            string shown = string.IsNullOrWhiteSpace(folderPath) ? "(empty)" : folderPath;
            EditorUtility.DisplayDialog("Folder not found",
                $"This isn't a folder in your project:\n\n{shown}\n\nPick a folder inside Assets, or use Browse.",
                "OK");
            return false;
        }

        public static void Create(AbilityScriptSpec spec)
        {
            if (!ValidateOutputFolder(spec)) return;

            string folderPath = spec.OutputFolderPath;
            string className = AbilityScriptGenerator.SanitizeClassName(spec.ClassName, spec.Kind);
            string assetPath = $"{folderPath}/{className}.cs";
            string fullPath = Path.GetFullPath(assetPath);

            if (File.Exists(fullPath))
            {
                bool overwrite = EditorUtility.DisplayDialog("Overwrite Script?",
                    $"A script already exists at:\n{assetPath}\n\nOverwrite it?",
                    "Overwrite", "Cancel");
                if (!overwrite) return;
            }

            string source = AbilityScriptGenerator.Generate(spec);
            File.WriteAllText(fullPath, source, new UTF8Encoding(false));
            AssetDatabase.Refresh();

            spec.CreatedScriptPath = assetPath;

            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(assetPath);
            if (script != null)
            {
                Selection.activeObject = script;
                EditorGUIUtility.PingObject(script);
            }
        }
    }
}
