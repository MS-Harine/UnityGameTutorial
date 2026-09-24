using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blocks
{
    /// <summary>
    /// Assistant-facing UI Toolkit pieces: an animated spinner label, a row of pulsing working dots, and an
    /// inline prompt input paired with a send button.
    /// </summary>
    static class AiPrompt
    {
        static readonly string[] k_SpinnerFrames = { "◐", "◓", "◑", "◒" };

        /// <summary>
        /// A single-line label with a spinner glyph that cycles while on screen and pauses when the element
        /// detaches from the panel.
        /// </summary>
        public static Label WorkingPill(string label = "Working with the Assistant…")
        {
            var pill = new Label($"{k_SpinnerFrames[0]} {label}");
            int frame = 0;
            var spin = pill.schedule.Execute(() =>
            {
                frame = (frame + 1) % k_SpinnerFrames.Length;
                pill.text = $"{k_SpinnerFrames[frame]} {label}";
            }).Every(150);
            pill.RegisterCallback<DetachFromPanelEvent>(_ => spin.Pause());
            return pill;
        }

        /// <summary>
        /// A row of three dots that pulse in a wave next to a label. The animation pauses when the element
        /// detaches from the panel.
        /// </summary>
        public static VisualElement WorkingIndicator(string label = "Working…")
        {
            var row = new VisualElement();
            row.AddToClassList("blocks-ai-working");

            var dotsWrap = new VisualElement();
            dotsWrap.AddToClassList("blocks-ai-working__dots");
            row.Add(dotsWrap);

            var dots = new VisualElement[3];
            for (int i = 0; i < dots.Length; i++)
            {
                dots[i] = new VisualElement();
                dots[i].AddToClassList("blocks-ai-working__dot");
                dotsWrap.Add(dots[i]);
            }

            var text = new Label(label);
            text.AddToClassList("blocks-ai-working__label");
            row.Add(text);

            float cycle = 0f;
            const float step = 1f / 70f;
            var tick = row.schedule.Execute(() =>
            {
                cycle = (cycle + step) % 1f;
                for (int i = 0; i < dots.Length; i++)
                {
                    float p = Mathf.Repeat(cycle - i * 0.16f, 1f);
                    float wave = 0.5f - 0.5f * Mathf.Cos(p * 2f * Mathf.PI);
                    dots[i].style.opacity = 0.3f + 0.7f * wave;
                    float s = 0.7f + 0.45f * wave;
                    dots[i].style.scale = new Scale(new Vector3(s, s, 1f));
                }
            }).Every(16);
            row.RegisterCallback<DetachFromPanelEvent>(_ => tick.Pause());

            return row;
        }

        /// <summary>
        /// An inline prompt input paired with a send button. Optionally submits on Enter, cancels on Escape,
        /// gates the send button through the canSend predicate, and focuses itself when shown.
        /// </summary>
        public static VisualElement CardInputRow(
            string draft,
            string placeholder,
            Action<string> onChanged,
            Action onSubmit,
            Action onCancel = null,
            bool autoFocus = false,
            bool submitOnEnter = false,
            Func<string, bool> canSend = null)
        {
            var row = new VisualElement();
            row.AddToClassList("blocks-ai-input");

            var input = new TextField { value = draft, multiline = false };
            input.AddToClassList("blocks-ai-input__field");
            input.textEdition.placeholder = placeholder;
            input.textEdition.hidePlaceholderOnFocus = true;
            row.Add(input);

            var send = new Button(onSubmit) { text = "➤" };
            send.AddToClassList("blocks-button--primary");
            send.AddToClassList("blocks-ai-input__send");
            if (canSend != null) send.SetEnabled(canSend(draft));
            row.Add(send);

            row.RegisterCallback<FocusInEvent>(_ => row.AddToClassList("blocks-ai-input--focused"));
            row.RegisterCallback<FocusOutEvent>(_ => row.RemoveFromClassList("blocks-ai-input--focused"));

            input.RegisterValueChangedCallback(evt =>
            {
                onChanged?.Invoke(evt.newValue);
                if (canSend != null) send.SetEnabled(canSend(evt.newValue));
            });

            if (submitOnEnter || onCancel != null)
            {
                bool submitQueued = false;
                input.RegisterCallback<KeyDownEvent>(evt =>
                {
                    bool isEnter = evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter ||
                        evt.character == '\n' || evt.character == '\r';

                    if (submitOnEnter && isEnter)
                    {
                        // One Enter press arrives as two KeyDownEvents (the keycode, then the '\n'
                        // character). Swallow both before the text engine sees them — otherwise the field
                        // commits and select-alls — and submit once, after the pair finishes dispatching,
                        // because onSubmit rebuilds this very field.
                        evt.StopPropagation();
                        if (submitQueued) return;
                        if (canSend != null && !canSend(input.value)) return;
                        submitQueued = true;
                        input.schedule.Execute(() =>
                        {
                            submitQueued = false;
                            onSubmit?.Invoke();
                        });
                    }
                    else if (onCancel != null && evt.keyCode == KeyCode.Escape)
                    {
                        onCancel.Invoke();
                        evt.StopPropagation();
                    }
                }, TrickleDown.TrickleDown);
            }

            if (autoFocus) input.schedule.Execute(input.Focus);
            return row;
        }

        /// <summary>
        /// The row shown when a prompt was sent while signed out: the reason, and a link that opens the
        /// Unity sign-in window. Rendered in the slot the working indicator occupies while running.
        /// </summary>
        public static VisualElement SignedOutNotice() =>
            Notice("Sign in to your Unity account, then send again.",
                "Sign in", CloudProjectSettings.ShowLogin);

        /// <summary>
        /// The row shown when a prompt was sent while the project has no cloud organization. The Assistant
        /// silently goes nowhere in that state, so this explains the fix and opens the exact settings page.
        /// </summary>
        public static VisualElement NotLinkedNotice() =>
            Notice("Link this project to a Unity organization with AI credits, then send again.",
                "Open Services settings", () => SettingsService.OpenProjectSettings("Project/Services"),
                "Setup guide", () => Application.OpenURL(AiSignIn.AssistantDocsUrl));

        /// <summary>The row shown when a request failed: the reason, in the slot the input row sits in.</summary>
        public static VisualElement ErrorNotice(string message) =>
            Notice(message, null, null);

        static VisualElement Notice(string message, string linkLabel, Action onLink,
            string secondLinkLabel = null, Action onSecondLink = null)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.flexWrap = Wrap.Wrap;
            row.style.marginBottom = 6;

            var text = new Label(message);
            text.AddToClassList("blocks-text--dim");
            text.style.whiteSpace = WhiteSpace.Normal;
            text.style.flexShrink = 1;
            row.Add(text);

            if (linkLabel != null)
            {
                var link = Buttons.Link(linkLabel, onLink);
                link.style.marginLeft = 6;
                row.Add(link);
            }

            if (secondLinkLabel != null)
            {
                var second = Buttons.Link(secondLinkLabel, onSecondLink);
                second.style.marginLeft = 6;
                row.Add(second);
            }

            return row;
        }
    }

    /// <summary>
    /// The two account states the Assistant needs before it can answer: a signed-in Unity account, and a
    /// project linked to a cloud organization (which is where AI credits live).
    /// </summary>
    static class AiSignIn
    {
        public const string AssistantDocsUrl =
            "https://docs.unity3d.com/Packages/com.unity.ai.assistant@latest";

        public static bool IsSignedOut =>
            string.IsNullOrEmpty(CloudProjectSettings.userName) ||
            string.Equals(CloudProjectSettings.userName, "anonymous", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Whether the project is missing its cloud organization link. Requests sent in this state don't
        /// fail fast — they sit on a multi-minute timeout — so callers check this before sending.
        /// </summary>
        public static bool IsProjectUnlinked =>
            !CloudProjectSettings.projectBound || string.IsNullOrEmpty(CloudProjectSettings.organizationId);
    }
}
