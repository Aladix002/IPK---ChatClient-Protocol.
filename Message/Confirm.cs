using System.Buffers.Binary;

namespace Message;

public class Confirm : IMessage
{
    public MessageType MessageType => MessageType.CONFIRM;

    public required ushort RefMessageId { get; init; }

    public byte[] ToBytes(ushort messageId)
    {
        var result = new byte[5];
        result[0] = (byte)MessageType.CONFIRM;
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(1, 2), messageId);
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(3, 2), RefMessageId);
        return result;
    }

    public static Confirm FromBytes(ReadOnlySpan<byte> data)
    {
        if (data.Length < 5 || data[0] != (byte)MessageType.CONFIRM)
            throw new ArgumentException("Invalid CONFIRM message");

        ushort refId = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(3, 2));
        return new Confirm { RefMessageId = refId };
    }


}

