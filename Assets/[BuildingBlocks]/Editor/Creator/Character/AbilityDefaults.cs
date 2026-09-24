using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using Blocks.Attack;

namespace Blocks
{
    /// <summary>
    /// Fills in the asset references a freshly attached ability needs before it can do anything: the input
    /// actions it listens to, and the projectile it fires. AddComponent leaves every reference null, and a
    /// null input action reads as "no input at all" rather than as a mistake — so without this an ability
    /// added by the Creator would sit in the inspector looking correct and never respond to a key.
    /// Only ever fills fields that are empty; anything already assigned is left exactly as it is.
    /// </summary>
    static class AbilityDefaults
    {
        /// <summary>
        /// Input fields whose name doesn't match the action they want. The convention — the field name
        /// minus its trailing "Action" — already resolves moveAction, sprintAction, jumpAction and
        /// meleeAction. Only the projectile breaks it, because the action is named for the range rather
        /// than for the thing being thrown. Add a pair here when the next field breaks it too.
        /// </summary>
        static readonly (string Field, string Action)[] k_ActionAliases =
        {
            ("projectileAction", "Ranged"),
        };

        const string k_ActionSuffix = "Action";

        /// <summary>The map holding the actions a playable character binds to.</summary>
        const string k_PlayerMapName = "Player";

        /// <summary>What the sample Player fires, so a Creator-made Player throws the same thing.</summary>
        const string k_DefaultProjectilePath = "Assets/[BuildingBlocks]/Prefabs/Projectile_Slash.prefab";

        public static void Apply(Component ability)
        {
            if (ability == null) return;

            var serializedAbility = new SerializedObject(ability);
            List<InputActionReference> actions = null;
            bool hasChanges = false;

            SerializedProperty property = serializedAbility.GetIterator();
            while (property.NextVisible(enterChildren: true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                if (property.objectReferenceValue != null) continue;

                if (IsReferenceTo<InputActionReference>(property))
                {
                    // Resolved on first need, so an ability with no input fields never touches the database.
                    actions ??= LoadActionReferences();
                    hasChanges |= TryAssignAction(ability, property, actions);
                }
                else if (IsReferenceTo<Projectile>(property))
                {
                    hasChanges |= TryAssignProjectile(ability, property);
                }
            }

            if (hasChanges) serializedAbility.ApplyModifiedProperties();
        }

        #region Input actions

        static bool TryAssignAction(Component ability, SerializedProperty property, List<InputActionReference> actions)
        {
            string wantedName = ResolveActionName(property.name);

            InputActionReference match = FindAction(actions, wantedName);
            if (match == null)
            {
                Debug.LogWarning(
                    $"[BuildingBlocksCreator] {ability.GetType().Name} on '{ability.gameObject.name}': " +
                    $"no input action named '{wantedName}' found for the '{property.name}' field. " +
                    "Assign it in the inspector, or the ability won't respond to that input.",
                    ability);
                return false;
            }

            property.objectReferenceValue = match;
            return true;
        }

        /// <summary>The action a field wants: its alias if it has one, otherwise the name minus "Action".</summary>
        static string ResolveActionName(string fieldName)
        {
            foreach ((string field, string action) in k_ActionAliases)
            {
                if (string.Equals(field, fieldName, StringComparison.OrdinalIgnoreCase)) return action;
            }

            return fieldName.EndsWith(k_ActionSuffix, StringComparison.OrdinalIgnoreCase)
                ? fieldName.Substring(0, fieldName.Length - k_ActionSuffix.Length)
                : fieldName;
        }

        // Prefers the Player map, so a project that binds the same action name in several maps still gets
        // the playable one rather than whichever happened to be indexed first.
        static InputActionReference FindAction(List<InputActionReference> actions, string actionName)
        {
            InputActionReference fallback = null;

            foreach (InputActionReference reference in actions)
            {
                InputAction action = reference.action;
                if (!string.Equals(action.name, actionName, StringComparison.OrdinalIgnoreCase)) continue;

                if (string.Equals(action.actionMap?.name, k_PlayerMapName, StringComparison.OrdinalIgnoreCase))
                {
                    return reference;
                }

                fallback ??= reference;
            }

            return fallback;
        }

        static List<InputActionReference> LoadActionReferences()
        {
            var actions = new List<InputActionReference>();

            foreach (string guid in AssetDatabase.FindAssets("t:InputActionAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                // The references are sub-assets of the .inputactions file, one per action.
                foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset is InputActionReference reference && reference.action != null)
                    {
                        actions.Add(reference);
                    }
                }
            }

            if (actions.Count == 0)
            {
                Debug.LogWarning(
                    "[BuildingBlocksCreator] No input actions found in the project, so input fields were " +
                    "left empty. Create an Input Actions asset, then assign the actions on the new " +
                    "character's abilities.");
            }

            return actions;
        }

        #endregion

        #region Projectile

        static bool TryAssignProjectile(Component ability, SerializedProperty property)
        {
            var projectile = AssetDatabase.LoadAssetAtPath<Projectile>(k_DefaultProjectilePath);
            if (projectile == null)
            {
                Debug.LogWarning(
                    $"[BuildingBlocksCreator] {ability.GetType().Name} on '{ability.gameObject.name}': " +
                    $"no projectile prefab at '{k_DefaultProjectilePath}' for the '{property.name}' field. " +
                    "Assign one in the inspector, or the attack won't fire.",
                    ability);
                return false;
            }

            property.objectReferenceValue = projectile;
            return true;
        }

        #endregion

        // SerializedProperty reports object fields as "PPtr<$TypeName>".
        static bool IsReferenceTo<T>(SerializedProperty property)
        {
            return property.type == $"PPtr<${typeof(T).Name}>";
        }
    }
}
