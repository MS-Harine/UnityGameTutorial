using System.Runtime.InteropServices;

namespace Blocks.Network
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct PacketHeader
    {
        public int PacketId { get; set; }
        public int PacketSize { get; set; }
    }
}