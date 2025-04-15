using System.Text;
using Message;

public class PingMessage
{
    public MessageType MessageType { get; set; } = MessageType.PING;

    public byte[] ToBytes(ushort id)
    {
        byte[] result = new byte[3];
        result[0] = (byte)MessageType;
        BitConverter.TryWriteBytes(result.AsSpan(1, 2), id);
        return result;
    }

    public static PingMessage FromBytes(ReadOnlySpan<byte> data)
    {
        if (data.Length < 3 || data[0] != (byte)MessageType.PING)
            throw new ArgumentException("Invalid PING message");

        return new PingMessage();
    }

    public override string ToString() => "PING";
} 