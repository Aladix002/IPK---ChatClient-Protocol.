using System.Net;
using System.Buffers.Binary;

namespace Message;

public class Bye
{
    public MessageType MessageType => MessageType.BYE;

    public byte[] ToBytes(ushort messageId)
    {
        ushort netId = BinaryPrimitives.ReverseEndianness(messageId);
        return new byte[] { (byte)MessageType.BYE, (byte)(netId >> 8), (byte)(netId & 0xFF) };
    }
}