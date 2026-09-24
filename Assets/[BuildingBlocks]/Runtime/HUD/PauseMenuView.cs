using System;
using Blocks.Network;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blocks.HUD
{
    /// <summary>
    /// The pause menu's screen half: finds the menu inside the HUD's UIDocument, generates the controls
    /// columns from <see cref="ControlSection"/> data, and reports button presses. Holds no game state;
    /// <see cref="PlayerHUD"/> decides what pausing actually does.
    ///
    /// Plain C# rather than a MonoBehaviour, the same shape as <see cref="StatBarStack"/>: it is handed a
    /// subtree and owns only that.
    /// </summary>
    public sealed class PauseMenuView
    {
        const string k_RootName = "pause-menu";
        const string k_ColumnsName = "pause-menu-columns";
        const string k_ResumeName = "resume-button";
        const string k_QuitName = "quit-button";
        const string k_IpInputName = "ip-input";
        const string k_PortInputName = "port-input";
        const string k_ConnectButtonName = "connect-button";
        const string k_StatusLabelName = "connection-status";

        const string k_StatusDisconnectedClass = "server-connection__status--disconnected";
        const string k_StatusConnectedClass = "server-connection__status--connected";

        const string k_SectionClass = "pause-menu__section";
        const string k_HeaderClass = "pause-menu__header";
        const string k_RowsClass = "pause-menu__rows";
        const string k_RowClass = "pause-menu__row";
        const string k_RowNameClass = "pause-menu__row-name";
        const string k_RowKeysClass = "pause-menu__row-keys";

        // From the shared theme rather than this document's stylesheet.
        const string k_ThemeHeaderClass = "blocks-header";
        const string k_ThemeHeaderSmallClass = "blocks-header--sm";

        readonly VisualElement m_Root;
        readonly VisualElement m_Columns;
        readonly Button m_Resume;
        readonly Button m_Quit;
        readonly TextField m_IpInput;
        readonly TextField m_PortInput;
        readonly Button m_ConnectButton;
        readonly Label m_StatusLabel;

        bool m_Bound;
        bool m_IsConnected;

        /// <summary>Raised when Resume is pressed.</summary>
        public event Action ResumeRequested;

        /// <summary>Raised when Quit is pressed.</summary>
        public event Action QuitRequested;

        /// <summary>Raised when Connect is pressed. Passes IP and Port.</summary>
        public event Action<string, string> ConnectRequested;

        public string IpAddress => m_IpInput?.value ?? string.Empty;
        public string Port => m_PortInput?.value ?? string.Empty;
        public bool IsConnected => m_IsConnected;

        /// <summary>False when the menu markup isn't in the document, in which case this does nothing.</summary>
        public bool IsValid => m_Root != null;

        public PauseMenuView(VisualElement documentRoot)
        {
            m_Root = documentRoot?.Q<VisualElement>(k_RootName);
            if (m_Root == null) return;

            m_Columns = m_Root.Q<VisualElement>(k_ColumnsName);
            m_Resume = m_Root.Q<Button>(k_ResumeName);
            m_Quit = m_Root.Q<Button>(k_QuitName);
            m_IpInput = m_Root.Q<TextField>(k_IpInputName);
            m_PortInput = m_Root.Q<TextField>(k_PortInputName);
            m_ConnectButton = m_Root.Q<Button>(k_ConnectButtonName);
            m_StatusLabel = m_Root.Q<Label>(k_StatusLabelName);

            NetworkManager.Instance.OnNetworkStateChanged += (state, message) =>
            {
                if (state == NetworkState.Connected)
                {
                    SetConnectionStatus(true, message);
                }
                else if (state == NetworkState.Connecting)
                {
                    SetConnectionStatus(false, "Connecting...");
                }
                else if (state == NetworkState.Disconnected)
                {
                    SetConnectionStatus(false, message);
                }
            };
        }

        /// <summary>Fills the columns from <paramref name="sections"/>. Safe to call again after a rebind.</summary>
        public void Build(ControlSection[] sections)
        {
            if (m_Columns == null) return;

            m_Columns.Clear();
            if (sections == null) return;

            foreach (ControlSection section in sections)
            {
                if (section == null) continue;
                m_Columns.Add(BuildSection(section));
            }
        }

        public void Bind()
        {
            if (m_Bound) return;
            m_Bound = true;

            if (m_Resume != null) m_Resume.clicked += HandleResumeClicked;
            if (m_Quit != null) m_Quit.clicked += HandleQuitClicked;
            if (m_ConnectButton != null) m_ConnectButton.clicked += HandleConnectClicked;
        }

        public void Unbind()
        {
            if (!m_Bound) return;
            m_Bound = false;

            if (m_Resume != null) m_Resume.clicked -= HandleResumeClicked;
            if (m_Quit != null) m_Quit.clicked -= HandleQuitClicked;
            if (m_ConnectButton != null) m_ConnectButton.clicked -= HandleConnectClicked;
        }

        /// <summary>
        /// Takes the menu in and out of the layout. Kept separate from <see cref="SetOpacity"/> so the
        /// menu can be laid out and visible for the whole fade, then removed once it is fully clear.
        /// </summary>
        public void SetVisible(bool isVisible)
        {
            if (m_Root == null) return;
            m_Root.style.display = isVisible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void SetOpacity(float opacity)
        {
            if (m_Root == null) return;
            m_Root.style.opacity = opacity;
        }

        /// <summary>
        /// Drops focus as the menu opens. The theme draws a focused button exactly like a hovered one, so
        /// anything focused comes up looking pressed, including whichever button was clicked the last
        /// time the menu was open, which keeps its focus while the menu is hidden.
        /// </summary>
        public void ClearFocus()
        {
            Focusable focused = m_Root?.focusController?.focusedElement;
            focused?.Blur();
        }

        VisualElement BuildSection(ControlSection section)
        {
            VisualElement column = new VisualElement();
            column.AddToClassList(k_SectionClass);
            column.Add(BuildHeader(section.Title));

            VisualElement rows = new VisualElement();
            rows.AddToClassList(k_RowsClass);

            if (section.Rows != null)
            {
                foreach (ControlRow row in section.Rows)
                {
                    if (row == null) continue;
                    rows.Add(BuildRow(row));
                }
            }

            column.Add(rows);
            return column;
        }

        static Label BuildHeader(string title)
        {
            Label header = new Label(title);
            header.AddToClassList(k_ThemeHeaderClass);
            header.AddToClassList(k_ThemeHeaderSmallClass);
            header.AddToClassList(k_HeaderClass);
            header.pickingMode = PickingMode.Ignore;
            return header;
        }

        static VisualElement BuildRow(ControlRow row)
        {
            VisualElement line = new VisualElement();
            line.AddToClassList(k_RowClass);

            Label name = new Label(row.Label);
            name.AddToClassList(k_RowNameClass);

            Label keys = new Label(ResolveKeys(row));
            keys.AddToClassList(k_RowKeysClass);

            line.Add(name);
            line.Add(keys);
            return line;
        }

        static string ResolveKeys(ControlRow row)
        {
            if (!string.IsNullOrEmpty(row.CustomKeys)) return row.CustomKeys;
            return row.Action != null ? InputBindingText.ForAction(row.Action.action) : string.Empty;
        }

        void HandleResumeClicked() => ResumeRequested?.Invoke();

        void HandleQuitClicked() => QuitRequested?.Invoke();

        void HandleConnectClicked() => ConnectRequested?.Invoke(IpAddress, Port);

        /// <summary>
        /// Updates the connection status UI. Only controls display state; performs no network logic.
        /// </summary>
        public void SetConnectionStatus(bool isConnected, string statusText = null)
        {
            m_IsConnected = isConnected;
            if (m_StatusLabel == null) return;

            m_StatusLabel.RemoveFromClassList(k_StatusDisconnectedClass);
            m_StatusLabel.RemoveFromClassList(k_StatusConnectedClass);

            if (isConnected)
            {
                m_StatusLabel.text = (statusText ?? "Status: Connected").Trim();
                m_StatusLabel.AddToClassList(k_StatusConnectedClass);
            }
            else
            {
                m_StatusLabel.text = (statusText ?? "Status: Disconnected").Trim();
                m_StatusLabel.AddToClassList(k_StatusDisconnectedClass);
            }
        }
    }
}
