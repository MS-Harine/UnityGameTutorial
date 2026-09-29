using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using Blocks.Character;
using UnityEngine;

namespace Blocks.Network
{
    public class PacketManager : MonoBehaviour
    {
        public static PacketManager Instance { get; private set; }

        private readonly List<byte> m_ReceiveBuffer = new();
        private readonly NetworkManager m_NetworkManager = NetworkManager.Instance;
        private readonly Dictionary<uint, Action<byte[], int, int>> m_Handlers = new();

        [Header("Local User Info")]
        [SerializeField] private string localUsername = "Player";

        public string LocalUsername
        {
            get => localUsername;
            set => localUsername = value;
        }

        public event Action OnConnected;
        public event Action OnDisconnected;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            RegisterPacketHandlers();
        }

        void OnEnable()
        {
            m_NetworkManager.OnNetworkStateChanged += HandleNetworkStateChanged;
        }

        void OnDisable()
        {
            m_NetworkManager.OnNetworkStateChanged -= HandleNetworkStateChanged;
        }

        private void HandleNetworkStateChanged(NetworkState state, string message)
        {
            switch (state)
            {
                case NetworkState.Connected:
                    OnConnectedToServer();
                    OnConnected?.Invoke();
                    break;
                case NetworkState.Disconnected:
                    OnDisconnectedFromServer();
                    OnDisconnected?.Invoke();
                    break;
            }
        }

        /// <summary>
        /// Called when the connection to the server succeeds.
        /// Sends initial handshake and spawn packets.
        /// </summary>
        public void OnConnectedToServer()
        {
            // 1. Send C2S_Connect (handshake/login)
            string username = string.IsNullOrEmpty(localUsername) ? "Player" : localUsername;
            var connectPacket = new C2S_Connect
            {
                username = username
            };
            SendPacket(connectPacket.Serialize());
        }

        private void OnDisconnectedFromServer()
        {
            m_ReceiveBuffer.Clear();
            CharacterManager.Instance?.ClearAllRemotePlayers();
        }

        /// <summary>
        /// Sends raw serialized packet bytes to the server.
        /// </summary>
        public void SendPacket(byte[] packetBytes)
        {
            if (packetBytes == null || packetBytes.Length == 0) return;
            _ = m_NetworkManager.SendData(packetBytes);
        }

        /// <summary>
        /// Sends C2S_Move packet containing current position, velocity, and facing direction.
        /// </summary>
        public void SendMove(Vector2 position, Vector2 velocity, bool facingRight)
        {
            if (m_NetworkManager == null || !m_NetworkManager.IsConnected()) return;

            var movePacket = new C2S_Move
            {
                x = position.x,
                y = position.y,
                vx = velocity.x,
                vy = velocity.y,
                facing_right = facingRight
            };
            SendPacket(movePacket.Serialize());
        }

        private void RegisterPacketHandlers()
        {
            m_Handlers.Clear();
            var methods = GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            foreach (var method in methods)
            {
                var attr = method.GetCustomAttribute<PacketHandlerAttribute>();
                if (attr == null) continue;

                var parameters = method.GetParameters();
                if (parameters.Length != 1)
                {
                    Debug.LogWarning($"[PacketManager] Method {method.Name} with [PacketHandler] must have exactly 1 parameter.");
                    continue;
                }

                Type packetType = parameters[0].ParameterType;

                // Look for static Deserialize(byte[] payload, int offset, int length) on the packet type
                MethodInfo deserializeMethod = packetType.GetMethod(
                    "Deserialize",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(byte[]), typeof(int), typeof(int) },
                    null);

                if (deserializeMethod == null)
                {
                    Debug.LogError($"[PacketManager] Could not find static Deserialize(byte[], int, int) on {packetType.Name}");
                    continue;
                }

                try
                {
                    // Build compiled expression: (data, offset, length) => this.Method(PacketType.Deserialize(data, offset, length))
                    var dataParam = Expression.Parameter(typeof(byte[]), "data");
                    var offsetParam = Expression.Parameter(typeof(int), "offset");
                    var lengthParam = Expression.Parameter(typeof(int), "length");

                    var callDeserialize = Expression.Call(deserializeMethod, dataParam, offsetParam, lengthParam);
                    var callHandler = Expression.Call(Expression.Constant(this), method, callDeserialize);

                    var lambda = Expression.Lambda<Action<byte[], int, int>>(callHandler, dataParam, offsetParam, lengthParam).Compile();
                    m_Handlers[(uint)attr.Id] = lambda;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[PacketManager] Expression compilation failed for {method.Name}, falling back to reflection invoke: {ex.Message}");
                    m_Handlers[(uint)attr.Id] = (data, offset, length) =>
                    {
                        object packet = deserializeMethod.Invoke(null, new object[] { data, offset, length });
                        method.Invoke(this, new[] { packet });
                    };
                }
            }
        }

        void Update()
        {
            byte[] data = m_NetworkManager.GetData();
            if (data.Length > 0)
            {
                m_ReceiveBuffer.AddRange(data);
            }

            int headerSize = Marshal.SizeOf(typeof(PacketHeader));
            while (m_ReceiveBuffer.Count >= headerSize)
            {
                Span<byte> headerSpan = m_ReceiveBuffer.GetRange(0, headerSize).ToArray();
                PacketHeader header = MemoryMarshal.Read<PacketHeader>(headerSpan);

                // Sanity check to prevent infinite loop on corrupted packets
                if (header.PacketSize < headerSize)
                {
                    Debug.LogError($"[PacketManager] Invalid packet size: {header.PacketSize} (less than header size {headerSize}). Clearing buffer to recover.");
                    m_ReceiveBuffer.Clear();
                    break;
                }

                Debug.Log($"[PacketManager] Receive packet {header.PacketId}.");

                // If full packet has not arrived yet, wait for more data
                if (m_ReceiveBuffer.Count < header.PacketSize)
                {
                    break;
                }

                byte[] packetBytes = m_ReceiveBuffer.GetRange(0, header.PacketSize).ToArray();
                m_ReceiveBuffer.RemoveRange(0, header.PacketSize);

                ProcessPacket(header, packetBytes, headerSize);
            }
        }

        void ProcessPacket(PacketHeader header, byte[] packetBytes, int headerSize)
        {
            int payloadLength = packetBytes.Length - headerSize;

            if (m_Handlers.TryGetValue((uint)header.PacketId, out var handler))
            {
                handler.Invoke(packetBytes, headerSize, payloadLength);
            }
            else
            {
                Debug.LogWarning($"[PacketManager] No handler registered for PacketId: {header.PacketId}");
            }
        }

        #region Packet Handlers

        [PacketHandler(PacketId.Connect)]
        private void Handle_S2C_Connect(S2C_Connect packet)
        {
            if (!packet.is_other_user)
            {
                CharacterManager.Instance.LocalPlayerId = (ulong)packet.userid;
                
                var localChar = CharacterManager.Instance?.LocalCharacter;
                if (localChar != null)
                {
                    var pos = localChar.transform.position;
                    var setPosPacket = new C2S_SetPosition
                    {
                        x = pos.x,
                        y = pos.y,
                        facing_right = localChar.FacingDirection >= 0,
                        reset_velocity = true
                    };
                    SendPacket(setPosPacket.Serialize());
                }
            }
            else
            {
                Vector3 defaultSpawnPos = Vector3.zero;
                CharacterManager.Instance.SpawnRemotePlayer((ulong)packet.userid, defaultSpawnPos, packet.username);
            }
        }

        [PacketHandler(PacketId.Disconnect)]
        private void Handle_S2C_Disconnect(S2C_Disconnect packet)
        {
            CharacterManager.Instance.DespawnRemotePlayer((ulong)packet.userid);
        }

        [PacketHandler(PacketId.SetPosition)]
        private void Handle_S2C_SetPosition(S2C_SetPosition packet)
        {
            Vector3 targetPos = new Vector3((float)packet.x, (float)packet.y, 0f);

            if ((ulong)packet.userid == CharacterManager.Instance.LocalPlayerId)
            {
                var localChar = CharacterManager.Instance.LocalCharacter;
                if (localChar != null)
                {
                    localChar.transform.position = targetPos;
                    if (packet.reset_velocity)
                    {
                        var rb = localChar.GetComponent<Rigidbody2D>();
                        if (rb != null) rb.linearVelocity = Vector2.zero;
                        localChar.SetVerticalVelocity(0f);
                    }
                    Vector2 facePos = (Vector2)targetPos + (packet.facing_right ? Vector2.right : Vector2.left);
                    localChar.FacePosition(facePos);
                }
            }
            else
            {
                var controller = CharacterManager.Instance.GetRemoteController((ulong)packet.userid);
                Debug.Log($"Player {packet.userid}, Controller: {controller}");
                if (controller != null)
                {
                    controller.SetPositionImmediate(targetPos);
                }
            }
        }

        [PacketHandler(PacketId.Move)]
        private void Handle_S2C_Move(S2C_Move packet)
        {
            if ((ulong)packet.userid == CharacterManager.Instance.LocalPlayerId) return;

            Vector3 pos = new Vector3((float)packet.x, (float)packet.y, 0f);
            Vector2 vel = new Vector2((float)packet.vx, (float)packet.vy);

            if (!CharacterManager.Instance.TryGetCharacter((ulong)packet.userid, out _))
            {
                CharacterManager.Instance.SpawnRemotePlayer((ulong)packet.userid, pos);
            }

            CharacterManager.Instance.UpdatePlayerTransform((ulong)packet.userid, pos, vel, packet.facing_right);
        }

        #endregion
    }
}
