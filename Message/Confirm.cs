using System.Net;
using System.Buffers.Binary;

namespace Message;
public class Confirm : IMessage
{
    public MessageType MessageType => MessageType.CONFIRM;
    public required ushort MessageId { get; init; }

    public byte[] ToBytes(ushort _) // underscore = ignored parameter
    {
        ushort netId = BinaryPrimitives.ReverseEndianness(MessageId);
        return new byte[]
        {
            (byte)MessageType.CONFIRM,
            (byte)(netId >> 8),
            (byte)(netId & 0xFF)
        };
    }

    public static Confirm FromBytes(ReadOnlySpan<byte> data)
    {
        if (data.Length < 3 || data[0] != (byte)MessageType.CONFIRM)
            throw new ArgumentException("Invalid Confirm message");

        ushort rawId = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(1, 2));

        return new Confirm
        {
            MessageId = rawId
        };
    }
}