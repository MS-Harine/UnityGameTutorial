using System;
using UnityEditor;
using UnityEngine;
using Blocks.Character;
using Blocks.GameFeel;

namespace Blocks
{
    /// <summary>
    /// Spawns a configured character into the scene: creates the GameObject, adds the renderer, physics,
    /// animator, and BuildingBlocksCharacter, applies the spec through SerializedObject, attaches the
    /// ability components, and for a player wires up the camera. Everything goes through Undo.
    /// </summary>
    static class CharacterSpawner
    {
        const string k_PlayerTag = "Player";

        #region Spawn

        public static GameObject Spawn(CharacterSpec spec, CharacterMode mode)
        {
            var go = new GameObject(string.IsNullOrWhiteSpace(spec.Name) ? "NewCharacter" : spec.Name);
            Undo.RegisterCreatedObjectUndo(go, $"Create {go.name}");

            go.transform.position = ResolveSpawnPosition();

            if (mode == CharacterMode.Player) TrySetTag(go, k_PlayerTag);

            Undo.AddComponent<Rigidbody2D>(go);

            var renderer = Undo.AddComponent<SpriteRenderer>(go);
            if (spec.Sprite != null) renderer.sprite = spec.Sprite;

            Undo.AddComponent<BoxCollider2D>(go);

            Undo.AddComponent<Animator>(go);
            Undo.AddComponent<CharacterAnimator>(go);

            var character = Undo.AddComponent<BuildingBlocksCharacter>(go);
            if (character == null)
            {
                Debug.LogError($"[BuildingBlocksCreator] Failed to add BuildingBlocksCharacter to {go.name}. Aborting spawn.");
                return go;
            }

            ConfigureCharacter(character, spec);

            foreach (Type movement in spec.MovementAbilityTypes)
            {
                if (movement == null) continue;
                AbilityDefaults.Apply(Undo.AddComponent(go, movement));
            }

            if (spec.AttackAbilityType != null)
            {
                AbilityDefaults.Apply(Undo.AddComponent(go, spec.AttackAbilityType));
            }

            if (mode == CharacterMode.Player) EnsureCameraFollows(go.transform);
            MoveToTop(character);

            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);
            SceneView.lastActiveSceneView?.FrameSelected();
            return go;
        }

        static void ConfigureCharacter(BuildingBlocksCharacter character, CharacterSpec spec)
        {
            var so = new SerializedObject(character);

            var statsProp = so.FindProperty(CharacterFields.Stats);
            statsProp.arraySize = spec.Stats.Count;
            for (int i = 0; i < spec.Stats.Count; i++)
            {
                var stat = spec.Stats[i];
                var element = statsProp.GetArrayElementAtIndex(i);
                element.FindPropertyRelative(CharacterFields.Stat.Type).enumValueIndex = (int)stat.Type;
                element.FindPropertyRelative(CharacterFields.Stat.Color).colorValue = stat.Color;
                element.FindPropertyRelative(CharacterFields.Stat.MaxValue).floatValue = stat.MaxValue;
                element.FindPropertyRelative(CharacterFields.Stat.StartValue).floatValue = Mathf.Clamp(stat.StartValue, 0f, stat.MaxValue);
                element.FindPropertyRelative(CharacterFields.Stat.RegenRate).floatValue = stat.RegenRate;
                element.FindPropertyRelative(CharacterFields.Stat.RegenDelay).floatValue = 0f;
            }

            so.FindProperty(CharacterFields.OnEliminated).enumValueIndex = (int)spec.OnEliminated;
            so.FindProperty(CharacterFields.Delay).floatValue = Mathf.Max(0f, spec.EliminationDelay);

            so.FindProperty(CharacterFields.IsMovementEnabled).boolValue = spec.MovementAbilityTypes.Count > 0;
            so.FindProperty(CharacterFields.IsAttackEnabled).boolValue = spec.AttackAbilityType != null;
            so.FindProperty(CharacterFields.IsTargetingEnabled).boolValue = spec.IsTargetingEnabled;

            so.FindProperty(CharacterFields.TargetingMode).enumValueIndex = (int)spec.TargetingMode;
            so.FindProperty(CharacterFields.TargetTag).stringValue = spec.TargetTag ?? string.Empty;
            so.FindProperty(CharacterFields.TargetRadius).floatValue = Mathf.Max(0f, spec.TargetRadius);

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        #endregion

        #region Scene helpers

        static void MoveToTop(Component component)
        {
            if (component == null) return;
            while (UnityEditorInternal.ComponentUtility.MoveComponentUp(component)) { }
        }

        static Vector3 ResolveSpawnPosition()
        {
            var view = SceneView.lastActiveSceneView;
            if (view != null && view.camera != null)
            {
                Vector3 p = view.pivot;
                p.z = 0f;
                return p;
            }
            return Vector3.zero;
        }

        static void TrySetTag(GameObject go, string tag)
        {
            try { go.tag = tag; }
            catch (UnityException)
            {
                Debug.LogWarning($"[BuildingBlocksCreator] Tag '{tag}' not defined. Add it in Tags & Layers to enable enemy targeting.");
            }
        }

        static void EnsureCameraFollows(Transform target)
        {
            var cameraFeedback = UnityEngine.Object.FindAnyObjectByType<CameraFeedback>();
            if (cameraFeedback == null)
            {
                Debug.LogError(
                    $"[BuildingBlocksCreator] '{target.name}' spawned without a camera following it: no " +
                    $"{nameof(CameraFeedback)} in the scene. Add a Cinemachine Camera, put " +
                    $"{nameof(CameraFeedback)} on it, then set its Tracking Target to '{target.name}'.",
                    target);
                return;
            }

            cameraFeedback.SetTarget(target);
        }

        #endregion
    }
}
