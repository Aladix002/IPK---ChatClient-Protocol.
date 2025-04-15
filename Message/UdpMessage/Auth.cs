using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Message;

public class Auth
{
    public MessageType MessageType => MessageType.AUTH;

    public required string Username { get; init; }
    public required string DisplayName { get; init; }
    public required string Secret { get; init; }

    private static readonly Regex ValidUsername = new("^[a-zA-Z0-9_-]{1,20}$", RegexOptions.Compiled);
    private static readonly Regex ValidDisplayName = new(@"^[\x21-\x7E]{1,20}$", RegexOptions.Compiled);
    private static readonly Regex ValidSecret = new("^[a-zA-Z0-9_-]{1,128}$", RegexOptions.Compiled);

    private static void RequireMatch(string value, Regex regex, string name)
    {
        if (!regex.IsMatch(value))
            throw new ArgumentException($"Invalid {name}");
    }

    private void Validate()
    {
        RequireMatch(Username, ValidUsername, nameof(Username));
        RequireMatch(DisplayName, ValidDisplayName, nameof(DisplayName));
        RequireMatch(Secret, ValidSecret, nameof(Secret));
    }

    public byte[] ToBytes(ushort messageId)
    {
        Validate();

        byte[] idBytes = BitConverter.GetBytes((ushort)IPAddress.HostToNetworkOrder((short)messageId));
        byte[] username = Encoding.ASCII.GetBytes(Username);
        byte[] display = Encoding.ASCII.GetBytes(DisplayName);
        byte[] secret = Encoding.ASCII.GetBytes(Secret);

        byte[] result = new byte[1 + 2 + username.Length + 1 + display.Length + 1 + secret.Length + 1];
        int offset = 0;

        result[offset++] = (byte)MessageType;
        result[offset++] = idBytes[0];
        result[offset++] = idBytes[1];

        offset += CopyWithNullTerminator(username, result, offset);
        offset += CopyWithNullTerminator(display, result, offset);
        CopyWithNullTerminator(secret, result, offset);

        return result;
    }

    private static int CopyWithNullTerminator(byte[] source, byte[] destination, int offset)
    {
        Array.Copy(source, 0, destination, offset, source.Length);
        destination[offset + source.Length] = 0;
        return source.Length + 1;
    }
}
