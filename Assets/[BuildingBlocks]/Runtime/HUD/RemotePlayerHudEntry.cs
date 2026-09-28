using Blocks.Character;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blocks.HUD
{
    /// <summary>
    /// Represents a single remote/party player's status entry in the HUD.
    /// Manages its visual hierarchy, avatar, name label, and stat bars.
    /// </summary>
    public sealed class RemotePlayerHudEntry
    {
        const string k_ItemClass = "remote-hud-item";
        const string k_AvatarFrameClass = "avatar-frame";
        const string k_AvatarFrameSmClass = "avatar-frame--sm";
        const string k_AvatarImageClass = "avatar-image";
        const string k_AvatarImageRemoteClass = "avatar-image--remote";
        const string k_AvatarRingClass = "avatar-ring";
        const string k_ColumnClass = "player-hud-corner__column";
        const string k_NameClass = "remote-player-name";
        const string k_StackClass = "stat-bar-stack";
        const string k_StackCornerClass = "stat-bar-stack--corner";
        const string k_StackRemoteClass = "stat-bar-stack--remote";

        public object Id { get; }
        public BuildingBlocksCharacter Character { get; }
        public VisualElement Root { get; }
        public Label NameLabel { get; }
        public StatBarStack StatStack { get; }

        bool m_Bound;

        public RemotePlayerHudEntry(
            object id,
            BuildingBlocksCharacter character,
            string displayName,
            VisualElement parent,
            Color? avatarTint = null)
        {
            Id = id;
            Character = character;

            Root = new VisualElement();
            Root.AddToClassList(k_ItemClass);
            Root.pickingMode = PickingMode.Ignore;

            // Avatar
            var avatarFrame = new VisualElement();
            avatarFrame.AddToClassList(k_AvatarFrameClass);
            avatarFrame.AddToClassList(k_AvatarFrameSmClass);
            avatarFrame.pickingMode = PickingMode.Ignore;

            var avatarImage = new VisualElement();
            avatarImage.AddToClassList(k_AvatarImageClass);
            avatarImage.AddToClassList(k_AvatarImageRemoteClass);
            avatarImage.pickingMode = PickingMode.Ignore;
            if (avatarTint.HasValue)
            {
                avatarImage.style.unityBackgroundImageTintColor = avatarTint.Value;
            }

            var avatarRing = new VisualElement();
            avatarRing.AddToClassList(k_AvatarRingClass);
            avatarRing.pickingMode = PickingMode.Ignore;

            avatarFrame.Add(avatarImage);
            avatarFrame.Add(avatarRing);

            // Column for Name + Bars
            var column = new VisualElement();
            column.AddToClassList(k_ColumnClass);
            column.pickingMode = PickingMode.Ignore;

            NameLabel = new Label(displayName);
            NameLabel.AddToClassList(k_NameClass);
            NameLabel.pickingMode = PickingMode.Ignore;

            var stackContainer = new VisualElement();
            stackContainer.AddToClassList(k_StackClass);
            stackContainer.AddToClassList(k_StackCornerClass);
            stackContainer.AddToClassList(k_StackRemoteClass);
            stackContainer.pickingMode = PickingMode.Ignore;

            column.Add(NameLabel);
            column.Add(stackContainer);

            Root.Add(avatarFrame);
            Root.Add(column);

            parent.Add(Root);

            if (character != null)
            {
                StatStack = new StatBarStack(character, stackContainer);
                StatStack.Build();
                Bind();
            }
        }

        public void Bind()
        {
            if (m_Bound) return;
            StatStack?.Bind();
            if (Character != null)
            {
                Character.OnRespawning += HandleRespawning;
                Character.OnRespawned += HandleRespawned;
            }
            m_Bound = true;
        }

        public void Unbind()
        {
            if (!m_Bound) return;
            StatStack?.Unbind();
            if (Character != null)
            {
                Character.OnRespawning -= HandleRespawning;
                Character.OnRespawned -= HandleRespawned;
            }
            m_Bound = false;
        }

        public void SetName(string name)
        {
            if (NameLabel != null) NameLabel.text = name;
        }

        public void SetVisible(bool visible)
        {
            Root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void Destroy()
        {
            Unbind();
            Root.RemoveFromHierarchy();
        }

        void HandleRespawning(float secondsRemaining)
        {
            Root.style.opacity = 0.4f;
        }

        void HandleRespawned()
        {
            Root.style.opacity = 1f;
            StatStack?.RefreshAll();
        }
    }
}
