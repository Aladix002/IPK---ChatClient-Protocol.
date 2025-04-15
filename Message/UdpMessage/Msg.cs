using System.Text;
using System.Buffers.Binary;
using System.Text.RegularExpressions;
using Message;

public class Msg
{
    public MessageType MessageType { get; set; } = MessageType.MSG;
    public ushort MessageId { get; set; } 
    public string? DisplayName { get; set; }
    public string? MessageContents { get; set; }

    public byte[] ToBytes(ushort id)
    {
        if (DisplayName is null || MessageContents is null)
            throw new InvalidOperationException("DisplayName and MessageContents must not be null");

        byte[] displayNameBytes = Encoding.UTF8.GetBytes(DisplayName);
        byte[] messageBytes = Encoding.UTF8.GetBytes(MessageContents);

        byte[] result = new byte[1 + 2 + displayNameBytes.Length + 1 + messageBytes.Length + 1];
        result[0] = (byte)MessageType;
        
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(1, 2), id);

        int offset = 3;
        Array.Copy(displayNameBytes, 0, result, offset, displayNameBytes.Length);
        offset += displayNameBytes.Length;
        result[offset++] = 0;

        Array.Copy(messageBytes, 0, result, offset, messageBytes.Length);
        offset += messageBytes.Length;
        result[offset] = 0;

        return result;
    }

    public static Msg FromBytes(byte[] data)
    {
        if (data.Length < 3)
            throw new ArgumentException("Data too short");

        var msg = new Msg
        {
            MessageType = (MessageType)data[0],
            MessageId = BitConverter.ToUInt16(data, 1)
        };

        int offset = 3;

        int dnameEnd = Array.IndexOf<byte>(data, 0, offset);
        if (dnameEnd < 0)
            throw new ArgumentException("Missing null terminator for DisplayName");

        msg.DisplayName = Encoding.UTF8.GetString(data, offset, dnameEnd - offset);
        offset = dnameEnd + 1;

        int msgEnd = Array.IndexOf<byte>(data, 0, offset);
        if (msgEnd < 0)
            throw new ArgumentException("Missing null terminator for MessageContents");

        msg.MessageContents = Encoding.UTF8.GetString(data, offset, msgEnd - offset);

        return msg;
    }
}
