using System;
using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;

namespace Blocks
{
    #region Wizard model

    /// <summary>
    /// Base for a multi-step flow: exposes the ordered steps, tracks the current one, and handles back and
    /// next navigation. A single-step wizard runs without the header and footer chrome.
    /// </summary>
    abstract class Wizard
    {
        public abstract string Title { get; }
        public abstract IReadOnlyList<WizardStep> Steps { get; }
        public int CurrentIndex { get; set; }
        public bool HasChrome => Steps.Count > 1;
        public bool IsFirst => CurrentIndex <= 0;
        public bool IsLast => CurrentIndex >= Steps.Count - 1;

        public WizardStep Current
        {
            get
            {
                CurrentIndex = Mathf.Clamp(CurrentIndex, 0, Steps.Count - 1);
                return Steps[CurrentIndex];
            }
        }

        public void Back() => CurrentIndex = Mathf.Max(0, CurrentIndex - 1);
        public void Next() => CurrentIndex = Mathf.Min(Steps.Count - 1, CurrentIndex + 1);
    }

    /// <summary>One step in a wizard: a tab label and the body it builds.</summary>
    abstract class WizardStep
    {
        public abstract string Label { get; }
        public abstract VisualElement BuildBody();
    }

    /// <summary>A card on the Creator home screen. Builds its own visual element.</summary>
    abstract class HomeCard
    {
        public abstract VisualElement Build();
    }

    #endregion

    #region Wizard rendering

    /// <summary>
    /// Renders a wizard into the window's top, content, and footer areas: the header, the step indicator,
    /// the current step's body, and the back/next footer when the wizard has more than one step.
    /// </summary>
    static class WizardView
    {
        public static void Render(Wizard wizard,
            VisualElement top, VisualElement content, VisualElement footer)
        {
            WizardStep step = wizard.Current;

            top.Add(new WizardHeader(wizard.Title, CreatorWindow.GoHome));
            if (wizard.HasChrome) top.Add(BuildIndicator(wizard));

            content.Add(Stage(step.BuildBody()));

            if (wizard.HasChrome) footer.Add(BuildFooter(wizard));
        }

        public static VisualElement Stage(VisualElement body)
        {
            var stage = new VisualElement();
            stage.AddToClassList("blocks-stage");
            stage.Add(body);
            return stage;
        }

        static StepIndicator BuildIndicator(Wizard wizard)
        {
            IReadOnlyList<WizardStep> steps = wizard.Steps;
            var labels = new string[steps.Count];
            for (int i = 0; i < steps.Count; i++) labels[i] = steps[i].Label;

            return new StepIndicator(labels, wizard.CurrentIndex, onJumpToDone: i =>
            {
                wizard.CurrentIndex = i;
                CreatorWindow.Refresh();
            });
        }

        static WizardFooter BuildFooter(Wizard wizard)
        {
            return new WizardFooter(
                wizard.CurrentIndex + 1, wizard.Steps.Count,
                onBack: () =>
                {
                    if (wizard.IsFirst) CreatorWindow.GoHome();
                    else { wizard.Back(); CreatorWindow.Refresh(); }
                },
                onNext: wizard.IsLast ? null : () =>
                {
                    wizard.Next();
                    CreatorWindow.Refresh();
                });
        }
    }

    /// <summary>The wizard top bar: a "Start over" link and a breadcrumb showing what is being built.</summary>
    sealed class WizardHeader : VisualElement
    {
        public WizardHeader(string title, Action onStartOver)
        {
            AddToClassList("blocks-wizard__topbar");

            var startOver = Buttons.Link("← Start over", onStartOver);
            startOver.AddToClassList("blocks-wizard__back");
            Add(startOver);

            var crumb = new Label($"Building  {title}");
            crumb.AddToClassList("blocks-wizard__crumb");
            Add(crumb);
        }
    }

    /// <summary>The wizard footer: a Back link, the step counter, and a Next button except on the last step.</summary>
    sealed class WizardFooter : VisualElement
    {
        public WizardFooter(int stepNumber, int stepCount, Action onBack, Action onNext)
        {
            AddToClassList("blocks-wizard__footer");

            var cluster = new VisualElement();
            cluster.AddToClassList("blocks-wizard__footer-cluster");
            Add(cluster);

            var back = Buttons.Link("← Back", onBack);
            back.AddToClassList("blocks-wizard__footer-back");
            cluster.Add(back);

            var counter = new Label($"Step {stepNumber} of {stepCount}");
            counter.AddToClassList("blocks-wizard__step-count");
            cluster.Add(counter);

            if (onNext != null)
            {
                var next = Buttons.Primary("Next →", onNext);
                next.AddToClassList("blocks-wizard__footer-next");
                cluster.Add(next);
            }
        }
    }

    #endregion

    #region Home cards

    /// <summary>A home card that opens a fresh wizard when clicked.</summary>
    sealed class WizardLaunchCard : HomeCard
    {
        readonly string m_Title;
        readonly HomeCardInfo m_Info;
        readonly bool m_Wide;
        readonly Func<Wizard> m_Make;

        public WizardLaunchCard(string title, in HomeCardInfo info, bool wide, Func<Wizard> make)
        {
            m_Title = title;
            m_Info = info;
            m_Wide = wide;
            m_Make = make;
        }

        public override VisualElement Build() =>
            new LaunchCard(m_Title, m_Info, m_Wide,
                onClick: () => CreatorWindow.ShowWizard(m_Make()));
    }

    #endregion

    #region Card data & parts

    /// <summary>Copy for a home card: its summary, example tags, and the "how this works" section.</summary>
    public readonly struct HomeCardInfo
    {
        public readonly string Summary;
        public readonly string[] Examples;
        public readonly LearnSection Learn;

        public HomeCardInfo(string summary, string[] examples, in LearnSection learn)
        {
            Summary = summary;
            Examples = examples;
            Learn = learn;
        }
    }

    /// <summary>Content for a card's "how this works" disclosure: a heading, body text, and optional bullet points.</summary>
    public readonly struct LearnSection
    {
        public readonly string Heading;
        public readonly string Body;
        public readonly string[] Points;

        public LearnSection(string heading, string body, string[] points = null)
        {
            Heading = heading;
            Body = body;
            Points = points;
        }
    }

    /// <summary>A collapsible section with a chevron header that shows or hides its body on click.</summary>
    sealed class CardDisclosure : VisualElement
    {
        readonly VisualElement m_Body;
        readonly Label m_Chevron;
        bool m_Open;

        public CardDisclosure(string label, VisualElement body)
        {
            AddToClassList("blocks-disclosure");

            var header = new VisualElement();
            header.AddToClassList("blocks-disclosure__header");
            header.RegisterCallback<ClickEvent>(evt => { Toggle(); evt.StopPropagation(); });
            Add(header);

            m_Chevron = new Label("▸");
            m_Chevron.AddToClassList("blocks-disclosure__chevron");
            header.Add(m_Chevron);

            var title = new Label(label);
            title.AddToClassList("blocks-disclosure__label");
            header.Add(title);

            m_Body = body;
            m_Body.AddToClassList("blocks-disclosure__body");
            m_Body.style.display = DisplayStyle.None;
            Add(m_Body);
        }

        void Toggle()
        {
            m_Open = !m_Open;
            m_Body.style.display = m_Open ? DisplayStyle.Flex : DisplayStyle.None;
            m_Chevron.text = m_Open ? "▾" : "▸";
        }
    }

    /// <summary>Builds the body of a LearnSection: the lead paragraph and any bullet points.</summary>
    static class LearnView
    {
        public static VisualElement Body(in LearnSection section)
        {
            var body = new VisualElement();

            if (!string.IsNullOrEmpty(section.Body))
            {
                var lead = new Label(section.Body);
                lead.AddToClassList("blocks-learn__body");
                body.Add(lead);
            }

            if (section.Points != null && section.Points.Length > 0)
                body.Add(Bullets.List(section.Points));

            return body;
        }
    }

    /// <summary>Shared pieces for building home cards: the title and summary header, the example pills, and the learn disclosure.</summary>
    static class HomeCardParts
    {
        public static void AddHead(VisualElement card, string title, string summary)
        {
            var header = new VisualElement();
            header.AddToClassList("blocks-pick__header");

            var titleLabel = new Label(title);
            titleLabel.AddToClassList("blocks-pick__title");
            header.Add(titleLabel);
            card.Add(header);

            if (string.IsNullOrEmpty(summary)) return;

            var desc = new Label(summary);
            desc.AddToClassList("blocks-pick__desc");
            card.Add(desc);
        }

        public static void AddExamples(VisualElement card, string[] examples)
        {
            if (examples == null || examples.Length == 0) return;

            var row = Pill.Row(examples);
            row.AddToClassList("blocks-pick__examples");
            card.Add(row);
        }

        public static void AddLearn(VisualElement card, in LearnSection learn)
        {
            if (string.IsNullOrEmpty(learn.Heading)) return;

            card.Add(new CardDisclosure(learn.Heading, LearnView.Body(learn)));
        }
    }

    /// <summary>The visual for a launch card: header, example pills, learn disclosure, and a click handler.</summary>
    sealed class LaunchCard : VisualElement
    {
        public LaunchCard(string title, in HomeCardInfo info, bool wide, Action onClick)
        {
            AddToClassList("blocks-pick");
            AddToClassList("blocks-pick--home");
            AddToClassList("blocks-pick--launch");
            if (wide) AddToClassList("blocks-pick--wide");

            HomeCardParts.AddHead(this, title, info.Summary);
            HomeCardParts.AddExamples(this, info.Examples);
            HomeCardParts.AddLearn(this, info.Learn);

            if (onClick != null) RegisterCallback<ClickEvent>(_ => onClick());
        }
    }

    #endregion
}
