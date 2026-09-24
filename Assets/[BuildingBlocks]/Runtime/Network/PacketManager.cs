using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Blocks.Network
{
    public class PacketManager : MonoBehaviour
    {
        private List<byte> m_ReceiveBuffer = new();
        private NetworkManager m_NetworkManager = NetworkManager.Instance;

        void Update()
        {
            byte[] data = m_NetworkManager.GetData();
            if (data.Length > 0)
            {
                m_ReceiveBuffer.AddRange(data);
                if (m_ReceiveBuffer.Count >= Marshal.SizeOf(typeof(PacketHeader)))
                {
                    Span<byte> headerSpan = m_ReceiveBuffer.GetRange(0, Marshal.SizeOf(typeof(PacketHeader))).ToArray();
                    PacketHeader header = MemoryMarshal.Read<PacketHeader>(headerSpan);
                    if (m_ReceiveBuffer.Count >= header.PacketSize)
                    {
                        Span<byte> packetSpan = m_ReceiveBuffer.GetRange(0, header.PacketSize).ToArray();
                        ProcessPacket(packetSpan.ToArray());
                        m_ReceiveBuffer.RemoveRange(0, header.PacketSize);
                    }
                }
            }
        }

        void ProcessPacket(byte[] packetData)
        {
            PacketHeader header = MemoryMarshal.Read<PacketHeader>(packetData);
            Span<byte> payload = packetData.AsSpan()[Marshal.SizeOf(typeof(PacketHeader))..];

            // Process
        }
    }
}
