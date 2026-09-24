using System;
using UnityEngine;
using Unity.AI.Assistant;
using Unity.AI.Assistant.Agents;
using Unity.AI.Assistant.Editor.Api;

namespace Blocks
{
    /// <summary>
    /// Builds the Building Blocks assistant agent from the prompt constants in <see cref="AgentPrompts"/>.
    /// </summary>
    static class BuildingBlocksAgent
    {
        /// <summary>
        /// Creates an <see cref="LlmAgent"/> wired with the Building Blocks id, name, description, and system prompt.
        /// </summary>
        /// <returns>A configured agent ready to handle assistant requests.</returns>
        public static LlmAgent New() =>
            new LlmAgent()
                .WithId(AgentPrompts.AgentId)
                .WithName(AgentPrompts.AgentName)
                .WithDescription(AgentPrompts.AgentDescription)
                .WithSystemPrompt(AgentPrompts.SystemPrompt);
    }

    /// <summary>
    /// Sends prompts to the Unity AI Assistant. A thin wrapper over <see cref="AssistantApi.Run(string)"/>
    /// that the editor tooling uses to talk to the assistant.
    /// </summary>
    static class AssistantChat
    {
        #region Public API

        /// <summary>
        /// Sends a prompt to the Assistant chat window, optionally attaching a text blob it can read as
        /// context. Blank prompts are skipped with a warning.
        /// </summary>
        /// <param name="prompt">The instruction to send.</param>
        /// <param name="attachmentBody">Text to attach as context, or null/blank to send no attachment.</param>
        /// <param name="attachmentDisplayName">The name shown for the attachment in the assistant.</param>
        public static void Run(string prompt, string attachmentBody = null, string attachmentDisplayName = null)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                Debug.LogWarning("[BuildingBlocks] Empty prompt, nothing to send.");
                return;
            }

            try
            {
                if (string.IsNullOrWhiteSpace(attachmentBody))
                {
                    _ = AssistantApi.Run(prompt);
                    return;
                }

                var ctx = new AssistantApi.AttachedContext();
                ctx.Add(new VirtualAttachment(attachmentBody, "text/plain", attachmentDisplayName, null));
                _ = AssistantApi.Run(prompt, ctx);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BuildingBlocks] Failed to send prompt to Assistant: {ex.Message}");
            }
        }

        /// <summary>
        /// Sends a code generation prompt with a text attachment the assistant can read.
        /// </summary>
        public static void Codegen(string prompt, string attachmentBody, string attachmentDisplayName)
            => Run(prompt, attachmentBody, attachmentDisplayName);

        /// <summary>
        /// Sends a plain prompt with no attached context.
        /// </summary>
        public static void Prompt(string prompt) => Run(prompt);

        #endregion
    }
}
