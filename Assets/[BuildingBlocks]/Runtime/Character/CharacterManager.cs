using System;
using System.Collections.Generic;
using Blocks.HUD;
using UnityEngine;

namespace Blocks.Character
{
    /// <summary>
    /// Central manager for all characters in the game, including the local player and remote player puppets.
    /// Handles registration, network spawn/despawn, health/damage routing, and puppet transform updates.
    /// </summary>
    [DisallowMultipleComponent]
    public class CharacterManager : MonoBehaviour
    {
        static CharacterManager s_Instance;

        public static CharacterManager Instance
        {
            get
            {
                if (s_Instance == null)
                {
                    s_Instance = FindAnyObjectByType<CharacterManager>();
                    if (s_Instance == null)
                    {
                        var go = new GameObject("CharacterManager");
                        s_Instance = go.AddComponent<CharacterManager>();
                    }
                }
                return s_Instance;
            }
            private set => s_Instance = value;
        }

        [Header("Prefab References")]
        [Tooltip("Prefab instantiated when a remote player joins.")]
        [SerializeField] GameObject remotePlayerPrefab;

        [Header("Local Player")]
        [Tooltip("Local player character. Auto-detected from scene if empty.")]
        [SerializeField] BuildingBlocksCharacter localCharacter;
        [SerializeField] ulong localPlayerId = 0;

        readonly Dictionary<ulong, BuildingBlocksCharacter> m_Characters = new();
        readonly Dictionary<ulong, RemotePlayerController> m_RemoteControllers = new();

        public event Action<ulong, BuildingBlocksCharacter> OnPlayerRegistered;
        public event Action<ulong> OnPlayerUnregistered;
        public event Action<ulong, BuildingBlocksCharacter> OnRemotePlayerSpawned;
        public event Action<ulong> OnRemotePlayerDespawned;

        public ulong LocalPlayerId
        {
            get => localPlayerId;
            set
            {
                if (localPlayerId == value) return;
                if (localCharacter != null && m_Characters.ContainsKey(localPlayerId))
                {
                    m_Characters.Remove(localPlayerId);
                    m_Characters[value] = localCharacter;
                }
                localPlayerId = value;
            }
        }

        public BuildingBlocksCharacter LocalCharacter => localCharacter;
        public IReadOnlyDictionary<ulong, BuildingBlocksCharacter> AllCharacters => m_Characters;
        public IReadOnlyDictionary<ulong, RemotePlayerController> RemoteControllers => m_RemoteControllers;

        public bool IsLocalPlayer(ulong id) => id == localPlayerId;

        void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            s_Instance = this;

            if (localCharacter == null)
            {
                FindAndRegisterLocalCharacter();
            }
            else
            {
                RegisterLocalCharacter(localPlayerId, localCharacter);
            }
        }

        void OnDestroy()
        {
            if (s_Instance == this)
            {
                s_Instance = null;
            }
        }

        /// <summary>
        /// Registers the local player character with the given ID.
        /// </summary>
        public void RegisterLocalCharacter(ulong id, BuildingBlocksCharacter character)
        {
            if (character == null) return;

            if (localCharacter != null && localCharacter != character && m_Characters.ContainsKey(localPlayerId))
            {
                m_Characters.Remove(localPlayerId);
            }

            localPlayerId = id;
            localCharacter = character;
            m_Characters[id] = character;
            OnPlayerRegistered?.Invoke(id, character);
        }

        void FindAndRegisterLocalCharacter()
        {
            if (PlayerHUD.Instance != null && PlayerHUD.Instance.LocalCharacter != null)
            {
                RegisterLocalCharacter(localPlayerId, PlayerHUD.Instance.LocalCharacter);
                return;
            }

            var playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null)
            {
                var character = playerObj.GetComponent<BuildingBlocksCharacter>();
                if (character != null)
                {
                    RegisterLocalCharacter(localPlayerId, character);
                }
            }
        }

        #region Spawn and Despawn

        /// <summary>
        /// Spawns a remote player puppet character in the world and registers it with the HUD.
        /// </summary>
        public BuildingBlocksCharacter SpawnRemotePlayer(ulong id, Vector3 spawnPosition, string displayName = null)
        {
            if (id == localPlayerId)
            {
                Debug.LogWarning($"[CharacterManager] ID {id} matches LocalPlayerId. Skipping remote spawn.");
                return localCharacter;
            }

            if (m_Characters.TryGetValue(id, out var existingChar))
            {
                existingChar.transform.position = spawnPosition;
                return existingChar;
            }

            GameObject prefabToSpawn = remotePlayerPrefab;
            if (prefabToSpawn == null)
            {
                prefabToSpawn = Resources.Load<GameObject>("RemotePlayer");
            }
#if UNITY_EDITOR
            if (prefabToSpawn == null)
            {
                prefabToSpawn = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/[BuildingBlocks]/Prefabs/RemotePlayer.prefab");
            }
#endif
            if (prefabToSpawn == null)
            {
                Debug.LogError("[CharacterManager] Remote player prefab is not assigned or found! Cannot spawn remote player.");
                return null;
            }

            GameObject playerGo = Instantiate(prefabToSpawn, spawnPosition, Quaternion.identity);
            string nameToUse = !string.IsNullOrEmpty(displayName) ? displayName : $"Player {id}";
            playerGo.name = $"RemotePlayer_{id}";

            var character = playerGo.GetComponent<BuildingBlocksCharacter>();
            var controller = playerGo.GetComponent<RemotePlayerController>();

            if (controller != null)
            {
                controller.Initialize(id, nameToUse);
                m_RemoteControllers[id] = controller;
            }

            if (character != null)
            {
                m_Characters[id] = character;
                // Automatically add to local player's HUD
                PlayerHUD.Instance?.AddRemoteCharacter(character, nameToUse, id);

                OnRemotePlayerSpawned?.Invoke(id, character);
                OnPlayerRegistered?.Invoke(id, character);
            }

            return character;
        }

        /// <summary>
        /// Despawns a remote player, removing their GameObject and HUD entry.
        /// </summary>
        public bool DespawnRemotePlayer(ulong id)
        {
            if (id == localPlayerId) return false;

            if (!m_Characters.TryGetValue(id, out var character))
            {
                return false;
            }

            PlayerHUD.Instance?.RemoveRemotePlayer(id);

            m_Characters.Remove(id);
            m_RemoteControllers.Remove(id);

            if (character != null)
            {
                Destroy(character.gameObject);
            }

            OnRemotePlayerDespawned?.Invoke(id);
            OnPlayerUnregistered?.Invoke(id);
            return true;
        }

        /// <summary>
        /// Clears all active remote players.
        /// </summary>
        public void ClearAllRemotePlayers()
        {
            var remoteIds = new List<ulong>();
            foreach (var kvp in m_Characters)
            {
                if (kvp.Key != localPlayerId)
                {
                    remoteIds.Add(kvp.Key);
                }
            }

            for (int i = 0; i < remoteIds.Count; i++)
            {
                DespawnRemotePlayer(remoteIds[i]);
            }
        }

        #endregion

        #region Central Control / Server Event Routing

        /// <summary>
        /// Applies damage to the character with the given ID.
        /// </summary>
        public void ApplyDamage(ulong id, float damageAmount)
        {
            if (damageAmount <= 0f) return;
            if (m_Characters.TryGetValue(id, out var character))
            {
                character.TakeDamage(new DamageInfo(damageAmount, null, character.transform.position, Vector2.zero));
            }
        }

        /// <summary>
        /// Applies custom DamageInfo to the character with the given ID.
        /// </summary>
        public void ApplyDamage(ulong id, in DamageInfo damageInfo)
        {
            if (damageInfo.Amount <= 0f) return;
            if (m_Characters.TryGetValue(id, out var character))
            {
                character.TakeDamage(damageInfo);
            }
        }

        /// <summary>
        /// Authoritatively sets the health of the character with the given ID.
        /// </summary>
        public void SetHealth(ulong id, float currentHealth, float maxHealth = -1f)
        {
            if (m_Characters.TryGetValue(id, out var character))
            {
                float current = character.CurrentHealth;
                float delta = currentHealth - current;
                if (Mathf.Abs(delta) > 0.001f)
                {
                    character.Apply(StatType.Health, delta);
                }
            }
        }

        /// <summary>
        /// Updates the transform and movement state of a player from a network packet.
        /// </summary>
        public void UpdatePlayerTransform(ulong id, Vector3 position, Vector2 velocity, bool facingRight)
        {
            if (m_RemoteControllers.TryGetValue(id, out var controller))
            {
                controller.UpdateNetworkState(position, velocity, facingRight);
            }
        }

        /// <summary>
        /// Updates position and facing direction; infers velocity from delta.
        /// </summary>
        public void UpdatePlayerTransform(ulong id, Vector3 position, bool facingRight)
        {
            if (m_RemoteControllers.TryGetValue(id, out var controller))
            {
                controller.UpdateNetworkState(position, facingRight);
            }
        }

        /// <summary>
        /// Triggers an attack action/animation on the specified character.
        /// </summary>
        /// <param name="id">Player ID</param>
        /// <param name="attackType">0 = Melee, 1 = Charged Melee, 2 = Ranged</param>
        public void TriggerAttack(ulong id, int attackType = 0)
        {
            if (m_RemoteControllers.TryGetValue(id, out var controller))
            {
                controller.TriggerAttack(attackType);
            }
            else if (m_Characters.TryGetValue(id, out var character))
            {
                if (attackType == 1) character.NotifyChargedAttackPerformed();
                else character.NotifyAttackPerformed();
            }
        }

        /// <summary>
        /// Eliminates the character with the given ID.
        /// </summary>
        public void EliminatePlayer(ulong id)
        {
            if (m_Characters.TryGetValue(id, out var character) && !character.IsEliminated)
            {
                character.TakeDamage(new DamageInfo(character.CurrentHealth + 10f, null, character.transform.position, Vector2.zero));
            }
        }

        /// <summary>
        /// Respawns the character with the given ID at the specified position.
        /// </summary>
        public void RespawnPlayer(ulong id, Vector3 respawnPosition)
        {
            if (m_Characters.TryGetValue(id, out var character))
            {
                character.Respawn(respawnPosition);
                if (m_RemoteControllers.TryGetValue(id, out var controller))
                {
                    controller.SetPositionImmediate(respawnPosition);
                }
            }
        }

        #endregion

        #region Lookups

        public BuildingBlocksCharacter GetCharacter(ulong id) => m_Characters.GetValueOrDefault(id);
        public bool TryGetCharacter(ulong id, out BuildingBlocksCharacter character) => m_Characters.TryGetValue(id, out character);
        public RemotePlayerController GetRemoteController(ulong id) => m_RemoteControllers.GetValueOrDefault(id);
        public bool TryGetRemoteController(ulong id, out RemotePlayerController controller) => m_RemoteControllers.TryGetValue(id, out controller);

        #endregion

#if UNITY_EDITOR
        [ContextMenu("Debug: Spawn Test Remote Player 1")]
        void TestSpawnRemotePlayer1()
        {
            Vector3 spawnPos = localCharacter != null ? localCharacter.transform.position + Vector3.right * 2f : Vector3.zero;
            SpawnRemotePlayer(1, spawnPos, "Player 1");
        }

        [ContextMenu("Debug: Damage Test Remote Player 1 (25 HP)")]
        void TestDamageRemotePlayer1()
        {
            ApplyDamage(1, 25f);
        }

        [ContextMenu("Debug: Despawn Test Remote Player 1")]
        void TestDespawnRemotePlayer1()
        {
            DespawnRemotePlayer(1);
        }
#endif
    }
}
