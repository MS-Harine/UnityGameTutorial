using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Blocks.Extras;

namespace Blocks
{
    /// <summary>
    /// Custom inspector for MovingPlatform. Draws the path as one row per waypoint, and puts a
    /// draggable handle on each waypoint in the Scene view.
    /// </summary>
    [CustomEditor(typeof(MovingPlatform))]
    sealed class MovingPlatformEditor : Editor
    {
        /// <summary>
        /// Serialized property names on MovingPlatform, so this inspector can address its private
        /// fields without hard-coding strings at each call site.
        /// </summary>
        static class Fields
        {
            public const string Nodes = "nodes";
            public const string Speed = "speed";
            public const string PlatformType = "platformType";
            public const string IsMovingAtStart = "isMovingAtStart";

            /// <summary>Serialized property names for a single entry in the path.</summary>
            public static class Node
            {
                public const string LocalPosition = "localPosition";
                public const string WaitTime = "waitTime";
            }
        }

        const int k_RowLabelWidth = 64;

        MovingPlatform m_MovingPlatform;

        SerializedProperty m_NodesProperty;
        SerializedProperty m_SpeedProperty;
        SerializedProperty m_PlatformTypeProperty;
        SerializedProperty m_IsMovingAtStartProperty;

        void OnEnable()
        {
            m_MovingPlatform = target as MovingPlatform;

            m_NodesProperty = serializedObject.FindProperty(Fields.Nodes);
            m_SpeedProperty = serializedObject.FindProperty(Fields.Speed);
            m_PlatformTypeProperty = serializedObject.FindProperty(Fields.PlatformType);
            m_IsMovingAtStartProperty = serializedObject.FindProperty(Fields.IsMovingAtStart);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(m_IsMovingAtStartProperty);
            EditorGUILayout.PropertyField(m_PlatformTypeProperty);
            EditorGUILayout.PropertyField(m_SpeedProperty);

            EditorGUILayout.Separator();

            if (GUILayout.Button("Add Node"))
                AddNode();

            DrawNodeRows();

            serializedObject.ApplyModifiedProperties();
        }

        void AddNode()
        {
            Undo.RecordObject(target, "added node");

            int index = m_NodesProperty.arraySize;
            Vector3 position = LocalPositionOf(index - 1).vector3Value + Vector3.right;

            m_NodesProperty.InsertArrayElementAtIndex(index);
            LocalPositionOf(index).vector3Value = position;
            WaitTimeOf(index).floatValue = 0f;
        }

        void DrawNodeRows()
        {
            EditorGUIUtility.labelWidth = k_RowLabelWidth;

            int delete = -1;
            for (int i = 0; i < m_NodesProperty.arraySize; ++i)
            {
                EditorGUILayout.BeginHorizontal();

                EditorGUILayout.BeginVertical(GUILayout.Width(k_RowLabelWidth));
                EditorGUILayout.LabelField("Node " + i, GUILayout.Width(k_RowLabelWidth));
                if (i != 0 && GUILayout.Button("Delete", GUILayout.Width(k_RowLabelWidth)))
                    delete = i;
                EditorGUILayout.EndVertical();

                EditorGUILayout.BeginVertical();
                // Node 0 is the platform's own starting position, so it has nothing to edit.
                if (i != 0)
                {
                    EditorGUILayout.PropertyField(LocalPositionOf(i), new GUIContent("Pos"));
                    EditorGUILayout.PropertyField(WaitTimeOf(i), new GUIContent("Wait Time"));
                }
                EditorGUILayout.EndVertical();

                EditorGUILayout.EndHorizontal();
            }

            EditorGUIUtility.labelWidth = 0;

            if (delete != -1)
                m_NodesProperty.DeleteArrayElementAtIndex(delete);
        }

        void OnSceneGUI()
        {
            int count = m_NodesProperty.arraySize;
            bool isLoop = (MovingPlatformType)m_PlatformTypeProperty.enumValueIndex == MovingPlatformType.Loop;

            for (int i = 0; i < count; ++i)
            {
                Vector3 worldPos = WorldNodePosition(i);

                if (i == 0)
                {
                    // Node 0 sits under the platform and can't be dragged. It only draws the closing
                    // segment of a looping path, back from the last node.
                    if (isLoop && count > 1)
                    {
                        Handles.color = Color.red;
                        Handles.DrawDottedLine(worldPos, WorldNodePosition(count - 1), 10f);
                    }
                    continue;
                }

                Vector3 newWorld = Handles.PositionHandle(worldPos, Quaternion.identity);

                Handles.color = Color.red;
                Handles.DrawDottedLine(worldPos, WorldNodePosition(i - 1), 10f);

                if (worldPos != newWorld)
                {
                    Undo.RecordObject(target, "moved point");

                    LocalPositionOf(i).vector3Value = m_MovingPlatform.transform.InverseTransformPoint(newWorld);
                    serializedObject.ApplyModifiedProperties();
                }
            }
        }

        /// <summary>
        /// Where a waypoint sits in world space. During play the platform has moved away from its
        /// authored origin, so the baked world path is the only correct source.
        /// </summary>
        Vector3 WorldNodePosition(int index)
        {
            IReadOnlyList<Vector3> worldNodes = m_MovingPlatform.WorldNodes;
            if (Application.isPlaying && worldNodes != null)
                return worldNodes[index];

            return m_MovingPlatform.transform.TransformPoint(LocalPositionOf(index).vector3Value);
        }

        SerializedProperty LocalPositionOf(int index) =>
            m_NodesProperty.GetArrayElementAtIndex(index).FindPropertyRelative(Fields.Node.LocalPosition);

        SerializedProperty WaitTimeOf(int index) =>
            m_NodesProperty.GetArrayElementAtIndex(index).FindPropertyRelative(Fields.Node.WaitTime);
    }
}
