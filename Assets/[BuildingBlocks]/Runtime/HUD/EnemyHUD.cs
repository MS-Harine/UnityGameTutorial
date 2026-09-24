using UnityEngine;
using UnityEngine.UIElements;
using Blocks.Character;

namespace Blocks.HUD
{
    /// <summary>
    /// World-space stat bars for an enemy, built by <see cref="StatBarStack"/> into this object's
    /// UIDocument. Put it on the character or a child; the character is found from the parents.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public class EnemyHUD : MonoBehaviour
    {
        const string k_StackName = "stat-bar-stack";

        [Header("References")]
        [Tooltip("Optional. Resolves from parents at Awake if left empty.")]
        [SerializeField] BuildingBlocksCharacter character;

        UIDocument m_UIDocument;
        StatBarStack m_Stack;

        void Awake()
        {
            m_UIDocument = GetComponent<UIDocument>();

            if (character == null)
            {
                character = GetComponentInParent<BuildingBlocksCharacter>();
            }

            if (character == null)
            {
                Debug.LogWarning($"[EnemyHUD] {name}: No BuildingBlocksCharacter found — HUD disabled.", this);
            }
        }

        void OnEnable()
        {
            if (character == null) return;
            character.OnEliminated += HandleEliminated;
            character.OnRespawned += HandleRespawned;
        }

        void Start()
        {
            BuildHud();
        }

        void OnDisable()
        {
            if (character == null) return;
            character.OnEliminated -= HandleEliminated;
            character.OnRespawned -= HandleRespawned;
            m_Stack?.Unbind();
        }

        void BuildHud()
        {
            if (m_UIDocument == null || character == null) return;

            VisualElement root = m_UIDocument.rootVisualElement;
            if (root == null) return;

            VisualElement container = root.Q<VisualElement>(k_StackName);
            if (container == null)
            {
                Debug.LogWarning($"[EnemyHUD] '{k_StackName}' not found in UIDocument.", this);
                return;
            }

            m_Stack = new StatBarStack(character, container);
            m_Stack.Build();
            m_Stack.Bind();
        }

        void HandleEliminated()
        {
            m_Stack?.SetVisible(false);
        }

        void HandleRespawned()
        {
            m_Stack?.RefreshAll();
            m_Stack?.SetVisible(true);
        }
    }
}
