using System.Net;
using System.Buffers.Binary;

namespace Message;

public class Bye : ITcpMessage
{
    public MessageType MessageType => MessageType.BYE;

    public string ToTcpString() => "BYE\r\n";

    public byte[] ToBytes(ushort messageId)
    {
        ushort netId = BinaryPrimitives.ReverseEndianness(messageId);
        return new byte[] { (byte)MessageType.BYE, (byte)(netId >> 8), (byte)(netId & 0xFF) };
    }
}