using System;
using System.Runtime.InteropServices;

namespace Blocks.Network
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct PacketHeader
    {
        public int PacketId { get; set; }
        public int PacketSize { get; set; }
    }

    /// <summary>
    /// Attribute to automatically bind packet handler methods in PacketManager.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class PacketHandlerAttribute : Attribute
    {
        public PacketId Id { get; }

        public PacketHandlerAttribute(PacketId id)
        {
            Id = id;
        }
    }
}