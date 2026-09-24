using System.IO;
using UnityEditor;
using UnityEngine;
using Blocks.Character;
using UnityEngine.UIElements;

namespace Blocks
{
    /// <summary>
    /// An editable summary of an ability spec, shown as a card of labeled rows. Which rows appear depends on
    /// the spec's kind and trigger, and edits write straight back to the spec. Rebuilds itself when a toggle
    /// changes which rows apply.
    /// </summary>
    sealed class AbilitySpecView : VisualElement
    {
        readonly AbilityScriptSpec m_Spec;

        public AbilitySpecView(AbilityScriptSpec spec)
        {
            m_Spec = spec;
            AddToClassList("blocks-card");
            Rebuild();
        }

        #region Layout

        void Rebuild()
        {
            Clear();

            bool isAttack = m_Spec.Kind == AbilityKind.Attack;
            bool isOnInput = m_Spec.Trigger == AbilityTrigger.OnInput;

            Add(ClassRow());
            Add(FolderRow());
            Add(TriggerRow());
            if (isOnInput) Add(InputFieldRow());
            if (isAttack)
            {
                Add(DamageRow());
                Add(RangeRow());
            }
            Add(TimingRow());
            Add(StatCostRow());
        }

        #endregion

        #region Row builders

        VisualElement ClassRow()
        {
            var col = new VisualElement();
            col.style.flexGrow = 1;

            var field = new TextField { value = m_Spec.ClassName };
            col.Add(field);

            var hint = new Label(ClassHint());
            hint.AddToClassList("blocks-muted");
            hint.style.marginTop = 2;
            hint.style.marginLeft = 2;
            col.Add(hint);

            field.RegisterValueChangedCallback(evt => { m_Spec.ClassName = evt.newValue; hint.text = ClassHint(); });
            return new LabeledRow("Class", col);
        }

        string ClassHint()
        {
            string className = AbilityScriptGenerator.SanitizeClassName(m_Spec.ClassName, m_Spec.Kind);
            string ns = AbilityScriptGenerator.SanitizeNamespace(m_Spec.Namespace, m_Spec.Kind);
            return $"{ns} · {className}.cs";
        }

        VisualElement FolderRow()
        {
            var col = new VisualElement();
            col.style.flexGrow = 1;
            col.style.flexDirection = FlexDirection.Row;
            col.style.alignItems = Align.Center;

            var field = new TextField { value = m_Spec.OutputFolderPath };
            field.style.flexGrow = 1;
            field.RegisterValueChangedCallback(evt => { m_Spec.OutputFolderPath = evt.newValue; });
            col.Add(field);

            var browse = new Button(() =>
            {
                string absolute = EditorUtility.OpenFolderPanel("Output folder", m_Spec.OutputFolderPath, "");
                if (string.IsNullOrEmpty(absolute)) return;

                string dataPath = Application.dataPath;
                string relative;
                if (absolute == dataPath) relative = "Assets";
                else if (absolute.StartsWith(dataPath + "/")) relative = "Assets" + absolute.Substring(dataPath.Length);
                else
                {
                    EditorUtility.DisplayDialog("Outside the project",
                        "Pick a folder inside this project's Assets folder.", "OK");
                    return;
                }

                m_Spec.OutputFolderPath = relative;
                field.SetValueWithoutNotify(relative);
            }) { text = "Browse…", tooltip = "Choose where the script file is written" };
            browse.style.marginLeft = 4;
            col.Add(browse);

            return new LabeledRow("Folder", col);
        }

        VisualElement TriggerRow()
        {
            string text = m_Spec.Trigger == AbilityTrigger.OnInput
                ? "Player input"
                : "Auto — driven by game logic (no input)";
            var lbl = new Label(text);
            lbl.AddToClassList("blocks-muted");
            return new LabeledRow("Trigger", lbl);
        }

        VisualElement InputFieldRow()
        {
            var col = new VisualElement();
            col.style.flexGrow = 1;

            var field = new TextField { value = m_Spec.InputFieldName };
            field.RegisterValueChangedCallback(evt => { m_Spec.InputFieldName = evt.newValue; });
            col.Add(field);

            var hint = new Label(
                "Name it after an action (jumpAction → Jump) and the Creator assigns that action when it " +
                "attaches the script. Anything else, assign the field in the Inspector.");
            hint.AddToClassList("blocks-muted");
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.marginTop = 2;
            hint.style.marginLeft = 2;
            col.Add(hint);

            return new LabeledRow("Input field", col);
        }

        VisualElement DamageRow()
        {
            var field = NumberField.Float(m_Spec.Damage, v => m_Spec.Damage = Mathf.Max(0f, v), 140);
            return new LabeledRow("Damage", field);
        }

        VisualElement RangeRow()
        {
            var field = NumberField.Float(m_Spec.Range, v => m_Spec.Range = Mathf.Max(0f, v), 140);
            return new LabeledRow("Range (m)", field);
        }

        VisualElement TimingRow()
        {
            var col = new VisualElement();
            col.style.flexGrow = 1;
            col.style.flexDirection = FlexDirection.Row;
            col.style.flexWrap = Wrap.Wrap;
            col.style.alignItems = Align.Center;

            bool isOnInput = m_Spec.Trigger == AbilityTrigger.OnInput;
            bool isPress = m_Spec.Activation == AbilityActivation.Press;
            int fields = 0;

            if (m_Spec.UseTimer)
            {
                col.Add(NumberField.Inline("Duration", m_Spec.Duration, 130, 56, v => m_Spec.Duration = Mathf.Max(0f, v)));
                fields++;
            }
            if (m_Spec.UseCooldown)
            {
                col.Add(NumberField.Inline("Cooldown", m_Spec.Cooldown, 130, 64, v => m_Spec.Cooldown = Mathf.Max(0f, v)));
                fields++;
            }
            if (isOnInput && isPress && m_Spec.UseInputBuffer)
            {
                col.Add(NumberField.Inline("Buffer", m_Spec.InputBuffer, 120, 48, v => m_Spec.InputBuffer = Mathf.Max(0f, v)));
                fields++;
            }
            if (fields == 0)
            {
                string prefix = isOnInput ? m_Spec.Activation.ToString() : "Auto";
                var none = new Label($"{prefix} — no timer, cooldown, or buffer for this template.");
                none.AddToClassList("blocks-muted");
                col.Add(none);
            }

            return new LabeledRow("Timing", col);
        }

        VisualElement StatCostRow()
        {
            var col = new VisualElement();
            col.style.flexGrow = 1;

            var toggle = new Toggle("Costs a stat") { value = m_Spec.UseStat };
            toggle.RegisterValueChangedCallback(evt => { m_Spec.UseStat = evt.newValue; Rebuild(); });
            col.Add(toggle);

            if (!m_Spec.UseStat) return new LabeledRow("Stat cost", col);

            var sub = new VisualElement();
            sub.style.flexDirection = FlexDirection.Row;
            sub.style.flexWrap = Wrap.Wrap;
            sub.style.alignItems = Align.Center;
            sub.style.marginTop = 4;

            var statType = new EnumField(m_Spec.StatType);
            statType.style.width = 110;
            statType.RegisterValueChangedCallback(evt => { m_Spec.StatType = (StatType)evt.newValue; });
            sub.Add(statType);

            sub.Add(NumberField.Inline("Cost", m_Spec.StatCost, 120, 36, v => m_Spec.StatCost = Mathf.Max(0f, v), marginLeft: 6));

            var perSec = new Toggle("/sec") { value = m_Spec.StatPerSecond };
            perSec.style.marginLeft = 8;
            perSec.RegisterValueChangedCallback(evt => { m_Spec.StatPerSecond = evt.newValue; });
            sub.Add(perSec);

            col.Add(sub);

            return new LabeledRow("Stat cost", col);
        }

        #endregion
    }
}
