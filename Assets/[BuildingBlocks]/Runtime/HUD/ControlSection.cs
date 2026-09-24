using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Blocks.HUD
{
    /// <summary>
    /// One line of the pause menu's controls list — a name on the left, the keys for it on the right.
    /// </summary>
    [Serializable]
    public class ControlRow
    {
        [SerializeField] string label;

        [Tooltip("The keys on the right are read from this action, so the list can't drift out of sync " +
                 "with InputSystem_Actions. Drag an action out of InputSystem_Actions.")]
        [SerializeField] InputActionReference action;

        [Tooltip("Listed instead of the action's own bindings. Use it where the raw list reads badly — " +
                 "Move is clearer as 'WASD, Arrow Keys' than as eight separate keys.")]
        [SerializeField] string customKeys;

        public string Label => label;
        public InputActionReference Action => action;
        public string CustomKeys => customKeys;
    }

    /// <summary>
    /// One titled column of the pause menu's controls list. Add a section for a new column, a row for a
    /// new line; the menu builds itself from these, so neither needs any objects made by hand.
    /// </summary>
    [Serializable]
    public class ControlSection
    {
        [SerializeField] string title;
        [SerializeField] ControlRow[] rows;

        public string Title => title;
        public ControlRow[] Rows => rows;
    }
}
