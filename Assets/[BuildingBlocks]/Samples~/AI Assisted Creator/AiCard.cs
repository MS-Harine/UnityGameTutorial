using System;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.AI.Assistant.Agents;
using Unity.AI.Assistant.Editor.Api;

namespace Blocks
{
    /// <summary>
    /// Base for an AI-assisted home card. Shows a prompt input, sends the intent to the assistant
    /// headlessly, and captures the review wizard the agent renders so the user can reopen it. Subclasses
    /// supply the copy, the prompt, the agent, and the test for which produced wizard belongs to this card.
    /// </summary>
    abstract class AiCard : HomeCard
    {
        protected abstract string Title { get; }
        protected abstract string Summary { get; }
        protected abstract string Placeholder { get; }
        protected virtual bool Wide => false;

        /// <summary>Label shown next to the working dots while this card's request runs.</summary>
        protected virtual string BusyLabel => "Working with the Assistant";

        protected abstract string BuildPrompt(string intent);
        protected abstract IAgent BuildAgent();
        protected abstract bool Owns(Wizard review);

        IAgent m_Agent;
        IAgent Agent => m_Agent ??= BuildAgent();

        string m_Draft = string.Empty;
        bool m_Running;
        bool m_SignedOut;
        bool m_NotLinked;
        string m_Error;
        Wizard m_LastReview;

        public override VisualElement Build() =>
            new PromptCard(
                Title, Summary, Placeholder, m_Draft,
                onDraftChanged: v => m_Draft = v,
                onSubmit: Submit,
                canSend: v => !m_Running && !string.IsNullOrWhiteSpace(v),
                running: m_Running,
                busyLabel: BusyLabel,
                notice: BuildNotice(),
                onResume: m_LastReview != null ? () => CreatorWindow.ShowWizard(m_LastReview) : null,
                wide: Wide);

        // The one thing standing between the user and a working prompt, or null when nothing is wrong.
        // Sign-in and organization link are preconditions; an error is the outcome of the last attempt.
        VisualElement BuildNotice()
        {
            if (m_SignedOut) return AiPrompt.SignedOutNotice();
            if (m_NotLinked) return AiPrompt.NotLinkedNotice();
            return string.IsNullOrEmpty(m_Error) ? null : AiPrompt.ErrorNotice(m_Error);
        }

        public void Start(string intent)
        {
            if (string.IsNullOrWhiteSpace(intent)) return;
            m_Draft = intent.Trim();
            Submit();
        }

        async void Submit()
        {
            if (m_Running || string.IsNullOrWhiteSpace(m_Draft)) return;
            string intent = m_Draft.Trim();

            // Both preconditions are checked before sending because a request without them doesn't fail
            // fast — it sits on the Assistant's multi-minute timeout, which reads as a hang.
            m_SignedOut = AiSignIn.IsSignedOut;
            m_NotLinked = !m_SignedOut && AiSignIn.IsProjectUnlinked;
            if (m_SignedOut || m_NotLinked)
            {
                AiHome.Show();
                CreatorWindow.RefreshHomeIfShowing();
                return;
            }

            m_Error = null;
            m_Running = true;
            AiHome.Show();

            Wizard produced = null;
            void Capture(Wizard w) { if (Owns(w)) produced = w; }
            CreatorWindow.ProposalRendered += Capture;
            try
            {
                await Agent.RunHeadless(BuildPrompt(intent));
            }
            catch (Exception ex)
            {
                m_Error = $"The request failed: {ex.Message}";
                Debug.LogError($"[BuildingBlocks] AI request failed: {ex}");
            }
            finally
            {
                CreatorWindow.ProposalRendered -= Capture;
                if (produced != null) m_LastReview = produced;
                else if (m_Error == null)
                    m_Error = "The Assistant answered without producing a proposal. Try again, or rephrase the prompt. The Console has the details.";
                m_Running = false;
                CreatorWindow.RefreshHomeIfShowing();
            }
        }

        public static void StartAbility(AbilityKind kind, string prompt) => AiHome.StartAbility(kind, prompt);
    }

    /// <summary>
    /// The AI card's visual: a header, a prompt input row (or a working indicator while running), an
    /// optional notice above the input (signed out, project not linked, or the last error), and an
    /// optional link to reopen the last review.
    /// </summary>
    sealed class PromptCard : VisualElement
    {
        public PromptCard(
            string title,
            string summary,
            string placeholder,
            string draft,
            Action<string> onDraftChanged,
            Action onSubmit,
            Func<string, bool> canSend = null,
            bool running = false,
            string busyLabel = "Working with the Assistant",
            VisualElement notice = null,
            Action onResume = null,
            bool wide = false)
        {
            AiStyles.Apply(this);

            AddToClassList("blocks-pick");
            AddToClassList("blocks-pick--home");
            AddToClassList("blocks-pick-ai");
            if (wide) AddToClassList("blocks-pick--wide");

            HomeCardParts.AddHead(this, title, summary);

            if (running)
            {
                Add(AiPrompt.WorkingIndicator(busyLabel));
                return;
            }

            if (notice != null) Add(notice);

            Add(AiPrompt.CardInputRow(
                draft, placeholder,
                onChanged: onDraftChanged,
                onSubmit: onSubmit,
                submitOnEnter: true,
                canSend: canSend));

            if (onResume != null)
            {
                var resumeRow = new VisualElement();
                resumeRow.AddToClassList("blocks-ai-input__resume-row");

                var resume = Buttons.Link("Open existing review →", onResume);
                resume.AddToClassList("blocks-ai-input__resume");
                resumeRow.Add(resume);
                Add(resumeRow);
            }
        }
    }
}
