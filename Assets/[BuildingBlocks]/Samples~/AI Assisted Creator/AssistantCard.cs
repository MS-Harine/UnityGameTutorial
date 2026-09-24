using System;
using UnityEditor;
using UnityEngine;
using Blocks.Attack;
using UnityEngine.UIElements;
using System.Collections.Generic;
using Blocks.Character;

namespace Blocks
{
    /// <summary>
    /// The Inspector's AI assistant card. Takes a free-text request, runs it through the headless agent,
    /// and shows the result inline: a diff with Apply and Discard, a read-only answer, or a working state.
    /// Apply writes every change to the character through Undo as a single step.
    /// </summary>
    sealed class AssistantCard : ModuleCard
    {
        BuildingBlocksCharacter m_Character;
        string m_TargetId;

        VisualElement m_Container;
        string m_Draft = string.Empty;
        bool m_Running;
        bool m_SignedOut;
        bool m_NotLinked;
        CharacterEditResult m_Pending;
        CharacterAnswerResult m_Answer;
        string m_Summary = "Describe a change or ask a question.";

        #region Card setup

        public AssistantCard() : base("assistant", "Assistant") => AiStyles.Apply(this);

        protected override bool DefaultExpanded => true;

        protected override void BuildBody(VisualElement body, SerializedObject serializedObject)
        {
            m_Character = Target<BuildingBlocksCharacter>();
            m_TargetId = m_Character != null ? m_Character.GetEntityId().ToString() : null;

            m_Container = new VisualElement();
            body.Add(m_Container);

            if (m_Character == null)
            {
                m_Summary = "Multi-edit not supported";
                m_Container.Add(MutedLabel("(Multi-edit not supported)"));
                return;
            }

            CharacterEditDispatch.Register(m_TargetId, this);
            RegisterCallback<DetachFromPanelEvent>(_ => CharacterEditDispatch.Unregister(m_TargetId, this));

            Rebuild();
        }

        protected override string BuildSummary() => m_Summary;

        #endregion

        #region Result callbacks

        public void OnResult(CharacterEditResult result)
        {
            m_Running = false;
            m_Answer = null;

            if (result == null || (!result.HasAnyChange && result.Suggestions.Count == 0))
            {
                m_Pending = null;
                SetSummary("No changes proposed — try rephrasing.");
                return;
            }

            m_Pending = result;
            SetSummary("Proposed changes — review below.");
        }

        public void OnAnswer(CharacterAnswerResult result)
        {
            m_Running = false;
            m_Pending = null;

            if (result == null || string.IsNullOrWhiteSpace(result.Answer))
            {
                m_Answer = null;
                SetSummary("No answer — try rephrasing.");
                return;
            }

            m_Answer = result;
            SetSummary("Answered — see below.");
        }

        #endregion

        #region View

        void Rebuild()
        {
            if (m_Container == null) return;
            m_Container.Clear();

            if (m_Pending != null) { BuildDiff(m_Container, m_Pending); return; }
            if (m_Answer != null) { BuildAnswer(m_Container, m_Answer); return; }
            if (m_Running) { BuildRunning(m_Container); return; }
            BuildPrompt(m_Container);
        }

        void SetSummary(string summary)
        {
            m_Summary = summary;
            RefreshSummary();
            Rebuild();
        }

        void BuildAnswer(VisualElement container, CharacterAnswerResult r)
        {
            var text = new Label(r.Answer);
            text.style.whiteSpace = WhiteSpace.Normal;
            text.style.marginBottom = 8;
            container.Add(text);

            var askAgain = new Button(() =>
            {
                m_Answer = null;
                SetSummary("Describe a change or ask a question.");
            }) { text = "Ask again" };
            askAgain.style.alignSelf = Align.FlexStart;
            container.Add(askAgain);
        }

        void BuildPrompt(VisualElement container)
        {
            if (m_SignedOut) container.Add(AiPrompt.SignedOutNotice());
            if (m_NotLinked) container.Add(AiPrompt.NotLinkedNotice());

            var hint = new Label("Describe a change — \"increase its health\" — or ask a question like \"what can this character do?\".");
            hint.AddToClassList("blocks-text--dim");
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.marginBottom = 6;
            container.Add(hint);

            var row = AiPrompt.CardInputRow(
                m_Draft,
                "Describe a change or ask a question…",
                v => m_Draft = v,
                Submit,
                submitOnEnter: true,
                canSend: s => !string.IsNullOrWhiteSpace(s));

            container.Add(row);
        }

        void BuildRunning(VisualElement container)
        {
            var pill = AiPrompt.WorkingPill("Updating your character…");
            pill.style.unityFontStyleAndWeight = FontStyle.Bold;
            pill.style.marginTop = 2;
            container.Add(pill);

            var cancel = new Button(() => { m_Running = false; Rebuild(); }) { text = "Cancel" };
            cancel.style.marginTop = 6;
            cancel.style.alignSelf = Align.FlexStart;
            container.Add(cancel);
        }

        void BuildDiff(VisualElement container, CharacterEditResult r)
        {
            if (!string.IsNullOrWhiteSpace(r.Rationale))
            {
                var why = new Label(r.Rationale);
                why.AddToClassList("blocks-text--dim");
                why.style.whiteSpace = WhiteSpace.Normal;
                why.style.marginBottom = 6;
                container.Add(why);
            }

            var changes = new VisualElement();
            changes.style.marginBottom = 4;
            container.Add(changes);

            AppendStatRows(changes, r);
            AppendAbilityRows(changes, r);
            AppendScalarRows(changes, r);
            AppendComponentRows(changes, r);

            foreach (var s in r.Suggestions)
            {
                if (s == null) continue;
                string title = string.IsNullOrWhiteSpace(s.Title) ? "Idea" : s.Title;
                string body = string.IsNullOrWhiteSpace(s.Prompt) ? string.Empty : $" — {s.Prompt}";
                container.Add(MutedLabel($"💡 Also consider: {title}{body}"));
            }
            foreach (var tip in r.Tips)
            {
                if (string.IsNullOrWhiteSpace(tip)) continue;
                container.Add(MutedLabel($"• {tip}"));
            }

            var actions = new VisualElement();
            actions.style.flexDirection = FlexDirection.Row;
            actions.style.marginTop = 8;

            var apply = new Button(() => Apply(r)) { text = "Apply" };
            apply.AddToClassList("blocks-button--primary");
            actions.Add(apply);

            var discard = new Button(() =>
            {
                m_Pending = null;
                SetSummary("Discarded.");
            }) { text = "Discard" };
            discard.style.marginLeft = 6;
            actions.Add(discard);

            container.Add(actions);
        }

        void AppendStatRows(VisualElement container, CharacterEditResult r)
        {
            foreach (var c in r.StatChanges)
            {
                if (c == null) continue;
                if (c.IsNew)
                {
                    string regen = c.NewRegen > 0f ? $", regen {c.NewRegen:0.#}/s" : string.Empty;
                    container.Add(DiffRow($"+ {c.Type}", null, $"max {c.NewMax:0.#}{regen}"));
                    continue;
                }
                if (!Mathf.Approximately(c.OldMax, c.NewMax))
                    container.Add(DiffRow($"{c.Type} max", $"{c.OldMax:0.#}", $"{c.NewMax:0.#}"));
                if (!Mathf.Approximately(c.OldRegen, c.NewRegen))
                    container.Add(DiffRow($"{c.Type} regen", $"{c.OldRegen:0.#}/s", $"{c.NewRegen:0.#}/s"));
            }
        }

        void AppendAbilityRows(VisualElement container, CharacterEditResult r)
        {
            foreach (var ability in r.AddMovementAbilities)
                container.Add(DiffRow("+ Movement", null, ability.Name));
            foreach (var ability in r.RemoveMovementAbilities)
                container.Add(DiffRow("− Movement", null, ability.Name));

            if (r.SetAttackAbility != null)
                container.Add(DiffRow("Attack", CurrentAttackName(), r.SetAttackAbility.Name));
            else if (r.ClearAttack)
                container.Add(DiffRow("Attack", CurrentAttackName(), "(none)"));
        }

        void AppendScalarRows(VisualElement container, CharacterEditResult r)
        {
            var so = SerializedObject;

            if (r.NewName != null)
                container.Add(DiffRow("Name", m_Character.gameObject.name, r.NewName));
            if (r.OnEliminated.HasValue)
                container.Add(DiffRow("On eliminated", SerializedRead.EnumName(so, CharacterFields.OnEliminated), r.OnEliminated.Value.ToString()));
            if (r.EliminationDelay.HasValue)
                container.Add(DiffRow("Elim. delay", FloatStr(so, CharacterFields.Delay, "s"), $"{r.EliminationDelay.Value:0.#}s"));
            if (r.MovementEnabled.HasValue)
                container.Add(DiffRow("Movement", BoolStr(so, CharacterFields.IsMovementEnabled), r.MovementEnabled.Value ? "Enabled" : "Disabled"));
            if (r.TargetingEnabled.HasValue)
                container.Add(DiffRow("Targeting", BoolStr(so, CharacterFields.IsTargetingEnabled), r.TargetingEnabled.Value ? "Enabled" : "Disabled"));
            if (r.AttackEnabled.HasValue)
                container.Add(DiffRow("Attack", BoolStr(so, CharacterFields.IsAttackEnabled), r.AttackEnabled.Value ? "Enabled" : "Disabled"));
            if (r.Gravity.HasValue)
                container.Add(DiffRow("Gravity", FloatStr(so, CharacterFields.Gravity), $"{r.Gravity.Value:0.#}"));
            if (r.FallGravityMultiplier.HasValue)
                container.Add(DiffRow("Fall gravity ×", FloatStr(so, CharacterFields.FallGravityMultiplier), $"{r.FallGravityMultiplier.Value:0.#}"));
            if (r.MaxFallSpeed.HasValue)
                container.Add(DiffRow("Max fall speed", FloatStr(so, CharacterFields.MaxFallSpeed), $"{r.MaxFallSpeed.Value:0.#}"));
            if (r.GroundSlopeLimit.HasValue)
                container.Add(DiffRow("Ground slope", FloatStr(so, CharacterFields.GroundSlopeLimit), $"{r.GroundSlopeLimit.Value:0.#}"));
            if (r.WallSlopeLimit.HasValue)
                container.Add(DiffRow("Wall slope", FloatStr(so, CharacterFields.WallSlopeLimit), $"{r.WallSlopeLimit.Value:0.#}"));
            if (r.TargetingMode.HasValue)
                container.Add(DiffRow("Targeting mode", SerializedRead.EnumName(so, CharacterFields.TargetingMode), r.TargetingMode.Value.ToString()));
            if (r.TargetTag != null)
                container.Add(DiffRow("Target tag", StringStr(so, CharacterFields.TargetTag), r.TargetTag));
            if (r.TargetRadius.HasValue)
                container.Add(DiffRow("Target radius", FloatStr(so, CharacterFields.TargetRadius, "m"), $"{r.TargetRadius.Value:0.#}m"));
        }

        void AppendComponentRows(VisualElement container, CharacterEditResult r)
        {
            foreach (var ch in r.ComponentChanges)
            {
                if (ch == null) continue;
                container.Add(DiffRow(ch.Label, ch.OldDisplay, ch.NewDisplay));
            }
        }

        #endregion

        #region Submit & apply

        async void Submit()
        {
            string intent = m_Draft?.Trim();
            if (string.IsNullOrWhiteSpace(intent)) return;

            if (AiSignIn.IsSignedOut)
            {
                m_SignedOut = true;
                SetSummary("Sign in to your Unity account.");
                return;
            }
            m_SignedOut = false;

            // Checked before sending, because a request from an unlinked project doesn't fail fast — it
            // sits on the Assistant's multi-minute timeout, which reads as a hang.
            if (AiSignIn.IsProjectUnlinked)
            {
                m_NotLinked = true;
                SetSummary("Link this project to a Unity organization.");
                return;
            }
            m_NotLinked = false;

            if (m_Character != null) Selection.activeGameObject = m_Character.gameObject;

            m_Running = true;
            m_Pending = null;
            m_Answer = null;
            SetSummary("Updating your character…");

            try
            {
                await InspectorAgent.Run(CharacterEditPrompts.BuildTweakCharacter(intent));

                // OnResult/OnAnswer arriving through the dispatch is the success path, and clears
                // m_Running. Still running here means the assistant finished without calling either tool.
                if (m_Running)
                {
                    m_Running = false;
                    SetSummary("The Assistant didn't return an edit — try rephrasing.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BuildingBlocks] Character edit failed: {ex}");
                if (m_Running)
                {
                    m_Running = false;
                    SetSummary($"The request failed: {ex.Message}");
                }
            }
        }

        void Apply(CharacterEditResult r)
        {
            if (m_Character == null || r == null) return;

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("AI tweak character");
            int group = Undo.GetCurrentGroup();

            if (r.NewName != null)
            {
                Undo.RecordObject(m_Character.gameObject, "Rename");
                m_Character.gameObject.name = r.NewName;
            }

            var so = SerializedObject;
            so.Update();

            if (r.StatChanges.Count > 0) ApplyStats(so, r);

            SetEnum(so, CharacterFields.OnEliminated, r.OnEliminated);
            SetFloat(so, CharacterFields.Delay, r.EliminationDelay);
            SetBool(so, CharacterFields.IsMovementEnabled, r.MovementEnabled);
            SetBool(so, CharacterFields.IsTargetingEnabled, r.TargetingEnabled);
            SetBool(so, CharacterFields.IsAttackEnabled, r.AttackEnabled);
            SetFloat(so, CharacterFields.Gravity, r.Gravity);
            SetFloat(so, CharacterFields.FallGravityMultiplier, r.FallGravityMultiplier);
            SetFloat(so, CharacterFields.MaxFallSpeed, r.MaxFallSpeed);
            SetFloat(so, CharacterFields.GroundSlopeLimit, r.GroundSlopeLimit);
            SetFloat(so, CharacterFields.WallSlopeLimit, r.WallSlopeLimit);
            SetEnum(so, CharacterFields.TargetingMode, r.TargetingMode);
            SetString(so, CharacterFields.TargetTag, r.TargetTag);
            SetFloat(so, CharacterFields.TargetRadius, r.TargetRadius);

            so.ApplyModifiedProperties();

            ApplyAbilities(r);
            ApplyComponentChanges(r);

            Undo.CollapseUndoOperations(group);

            m_Pending = null;
            SetSummary("Applied. (Ctrl+Z to undo)");
        }

        void ApplyStats(SerializedObject so, CharacterEditResult r)
        {
            var statsProp = so.FindProperty(CharacterFields.Stats);
            if (statsProp == null) return;

            foreach (var c in r.StatChanges)
            {
                if (c == null) continue;

                int existing = FindStatIndex(statsProp, c.Type);
                SerializedProperty el;
                if (existing >= 0)
                {
                    el = statsProp.GetArrayElementAtIndex(existing);
                }
                else
                {
                    int idx = statsProp.arraySize;
                    statsProp.InsertArrayElementAtIndex(idx);
                    el = statsProp.GetArrayElementAtIndex(idx);
                    el.FindPropertyRelative(CharacterFields.Stat.Type).intValue = Convert.ToInt32(c.Type);
                    el.FindPropertyRelative(CharacterFields.Stat.Color).colorValue = StatTypeDefaults.ColorFor(c.Type);
                    var regenDelay = el.FindPropertyRelative(CharacterFields.Stat.RegenDelay);
                    if (regenDelay != null) regenDelay.floatValue = 0f;
                }

                el.FindPropertyRelative(CharacterFields.Stat.MaxValue).floatValue = c.NewMax;
                el.FindPropertyRelative(CharacterFields.Stat.StartValue).floatValue = c.NewMax;
                el.FindPropertyRelative(CharacterFields.Stat.RegenRate).floatValue = c.NewRegen;
            }
        }

        void ApplyAbilities(CharacterEditResult r)
        {
            var go = m_Character.gameObject;

            foreach (var ability in r.RemoveMovementAbilities)
                RemoveComponentsOfType(go, ability);

            foreach (var t in r.AddMovementAbilities)
            {
                if (go.GetComponentInChildren(t, true) == null)
                    Undo.AddComponent(go, t);
            }

            if (!r.ClearAttack && r.SetAttackAbility == null) return;

            var existing = new List<AttackAbility>();
            m_Character.GetComponentsInChildren(true, existing);

            if (r.SetAttackAbility != null)
            {
                bool already = false;
                foreach (var a in existing)
                {
                    if (a == null) continue;
                    if (a.GetType() == r.SetAttackAbility && a.gameObject == go) { already = true; continue; }
                    Undo.DestroyObjectImmediate(a);
                }
                if (!already) Undo.AddComponent(go, r.SetAttackAbility);
            }
            else
            {
                foreach (var a in existing)
                    if (a != null) Undo.DestroyObjectImmediate(a);
            }
        }

        void ApplyComponentChanges(CharacterEditResult r)
        {
            if (r.ComponentChanges.Count == 0) return;
            var go = m_Character.gameObject;

            foreach (var ch in r.ComponentChanges)
            {
                if (ch == null) continue;

                var component = ComponentPatch.Find(go, ch.ComponentTypeName, ch.ComponentIndex);
                if (component == null) continue;

                var cso = new SerializedObject(component);
                var prop = cso.FindProperty(ch.PropertyPath);
                if (prop == null) continue;

                if (ComponentPatch.TryApply(prop, ch.RawValue, out _))
                    cso.ApplyModifiedProperties();
            }
        }

        static void RemoveComponentsOfType(GameObject go, Type t)
        {
            var comps = go.GetComponentsInChildren(t, true);
            foreach (var comp in comps)
                if (comp != null) Undo.DestroyObjectImmediate(comp);
        }

        static int FindStatIndex(SerializedProperty statsProp, StatType type)
        {
            int target = Convert.ToInt32(type);
            for (int i = 0; i < statsProp.arraySize; i++)
            {
                var t = statsProp.GetArrayElementAtIndex(i).FindPropertyRelative(CharacterFields.Stat.Type);
                if (t != null && t.intValue == target) return i;
            }
            return -1;
        }

        #endregion

        #region Formatting helpers

        string CurrentAttackName()
        {
            if (m_Character == null) return "(none)";
            var a = m_Character.GetComponentInChildren<AttackAbility>(true);
            return a != null ? a.GetType().Name : "(none)";
        }

        static void SetFloat(SerializedObject so, string path, float? value)
        {
            if (!value.HasValue) return;
            var p = so.FindProperty(path);
            if (p != null) p.floatValue = value.Value;
        }

        static void SetBool(SerializedObject so, string path, bool? value)
        {
            if (!value.HasValue) return;
            var p = so.FindProperty(path);
            if (p != null) p.boolValue = value.Value;
        }

        static void SetString(SerializedObject so, string path, string value)
        {
            if (value == null) return;
            var p = so.FindProperty(path);
            if (p != null) p.stringValue = value;
        }

        static void SetEnum<T>(SerializedObject so, string path, T? value) where T : struct, Enum
        {
            if (!value.HasValue) return;
            var p = so.FindProperty(path);
            if (p != null) p.intValue = Convert.ToInt32(value.Value);
        }

        static VisualElement DiffRow(string label, string from, string to)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.FlexStart;
            row.style.marginTop = 1;
            row.style.marginBottom = 1;

            var labelEl = new Label(label);
            labelEl.style.width = 120;
            labelEl.style.flexShrink = 0;
            labelEl.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(labelEl);

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            spacer.style.flexShrink = 0;
            row.Add(spacer);

            var val = new Label(from == null ? to : $"{from}  →  {to}");
            val.style.flexShrink = 1;
            val.style.whiteSpace = WhiteSpace.Normal;
            val.style.unityTextAlign = TextAnchor.UpperRight;
            row.Add(val);

            return row;
        }

        static string FloatStr(SerializedObject so, string path, string suffix = "")
        {
            var p = so.FindProperty(path);
            return p != null ? $"{p.floatValue:0.#}{suffix}" : "?";
        }

        static string BoolStr(SerializedObject so, string path)
        {
            var p = so.FindProperty(path);
            return p == null ? "?" : (p.boolValue ? "Enabled" : "Disabled");
        }

        static string StringStr(SerializedObject so, string path)
        {
            var p = so.FindProperty(path);
            if (p == null) return "?";
            return string.IsNullOrEmpty(p.stringValue) ? "(none)" : p.stringValue;
        }

        #endregion
    }

    /// <summary>
    /// Routes an agent's edit or answer result back to the AssistantCard for the matching character, keyed
    /// by object id. Cards register while their Inspector is open and unregister when it closes.
    /// </summary>
    static class CharacterEditDispatch
    {
        static readonly Dictionary<string, AssistantCard> s_Cards = new();

        public static void Register(string id, AssistantCard card)
        {
            if (string.IsNullOrEmpty(id) || card == null) return;
            s_Cards[id] = card;
        }

        public static void Unregister(string id, AssistantCard card)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (s_Cards.TryGetValue(id, out var current) && current == card) s_Cards.Remove(id);
        }

        internal static void Route(CharacterEditResult result)
        {
            if (result == null || string.IsNullOrEmpty(result.TargetId)) return;

            if (s_Cards.TryGetValue(result.TargetId, out var card) && card != null)
            {
                card.OnResult(result);
                return;
            }

            Debug.LogWarning("[BuildingBlocks] A character edit proposal arrived but its Inspector is no longer open for that object. Re-select the character and try again.");
        }

        internal static void RouteAnswer(CharacterAnswerResult result)
        {
            if (result == null || string.IsNullOrEmpty(result.TargetId)) return;

            if (s_Cards.TryGetValue(result.TargetId, out var card) && card != null)
            {
                card.OnAnswer(result);
                return;
            }

            Debug.LogWarning("[BuildingBlocks] A character answer arrived but its Inspector is no longer open for that object. Re-select the character and try again.");
        }
    }
}
