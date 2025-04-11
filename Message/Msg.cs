using System.Text;
using System.Text.RegularExpressions;
using Message;

public class Msg : IMessage
{
    public MessageType MessageType { get; set; } = MessageType.MSG;
    public ushort MessageId { get; set; }  // <- DOPLNENÉ
    public string? DisplayName { get; set; }
    public string? MessageContents { get; set; }

    public static string ToTcpString(Msg msg)
    {
        if (string.IsNullOrEmpty(msg.DisplayName) || string.IsNullOrEmpty(msg.MessageContents))
            throw new ArgumentException("DisplayName or MessageContents cannot be null or empty.");

        if (msg.DisplayName.Length > 20 || msg.MessageContents.Length > 1400)
            throw new ArgumentException("Display name or message too long.");

        string patternDname = @"^[\x20-\x7E]*$";
        if (!Regex.IsMatch(msg.DisplayName, patternDname))
            throw new ArgumentException("Invalid characters in DisplayName.");

        string pattern = @"^[\x20-\x7E\s]*$";
        if (!Regex.IsMatch(msg.MessageContents, pattern))
            throw new ArgumentException("Invalid characters in MessageContents.");

        return $"MSG FROM {msg.DisplayName} IS {msg.MessageContents}\r\n";
    }

    public static Msg FromStringTcp(string[] words)
    {
        if (words.Length != 5 || words[1] != "FROM" || words[3] != "IS")
            throw new ArgumentException("Wrong format");

        string patternDname = @"^[\x20-\x7E]*$";
        if (!Regex.IsMatch(words[2], patternDname))
            throw new ArgumentException("Invalid characters in DisplayName.");

        string pattern = @"^[\x20-\x7E\s]*$";
        if (!Regex.IsMatch(words[4], pattern))
            throw new ArgumentException("Invalid characters in MessageContents.");

        return new Msg
        {
            DisplayName = words[2],
            MessageContents = words[4]
        };
    }

    public byte[] ToBytes(ushort id)
    {
        MessageId = id;

        if (DisplayName is null || MessageContents is null)
            throw new InvalidOperationException("DisplayName and MessageContents must not be null");

        byte[] displayNameBytes = Encoding.UTF8.GetBytes(DisplayName);
        byte[] messageBytes = Encoding.UTF8.GetBytes(MessageContents);

        byte[] result = new byte[1 + 2 + displayNameBytes.Length + 1 + messageBytes.Length + 1];
        result[0] = (byte)MessageType;

        byte[] messageIdBytes = BitConverter.GetBytes(id);
        Array.Copy(messageIdBytes, 0, result, 1, 2);

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
