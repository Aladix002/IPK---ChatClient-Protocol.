using System;
using System.Text;
using System.Buffers.Binary;

namespace Message;

public class Bye
{
    public MessageType MessageType => MessageType.BYE;
    public ushort MessageId { get; set; }
    public string DisplayName { get; set; } = "?";

    public byte[] ToBytes(ushort messageId)
    {
        MessageId = messageId;
        byte[] nameBytes = Encoding.UTF8.GetBytes(DisplayName);
        byte[] result = new byte[1 + 2 + nameBytes.Length + 1]; 

        result[0] = (byte)MessageType.BYE;
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(1, 2), messageId);
        Array.Copy(nameBytes, 0, result, 3, nameBytes.Length);
        result[^1] = 0x00;

        return result;
    }

    public static Bye FromBytes(byte[] data)
    {
        if (data.Length < 4 || data[0] != (byte)MessageType.BYE || data[^1] != 0x00)
            throw new ArgumentException("Invalid BYE message format");

        ushort msgId = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(1, 2));
        string name = Encoding.UTF8.GetString(data, 3, data.Length - 4);

        return new Bye
        {
            MessageId = msgId,
            DisplayName = name
        };
    }
}
