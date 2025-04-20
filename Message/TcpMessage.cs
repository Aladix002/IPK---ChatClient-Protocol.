namespace Message;

public class TcpMessage
{
    public MessageType Type { get; init; }   
    public string? Username { get; init; }    
    public string? Secret { get; init; }       
    public string? ChannelId { get; init; }       
    public string? DisplayName { get; init; }    
    public string? MessageContents { get; init; }   
    public bool Result { get; init; }               

    // textova reprezentacia protokolu na TcpMessage
    public static TcpMessage ParseTcp(string line)
    {
        if (line.StartsWith("AUTH", StringComparison.OrdinalIgnoreCase))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 6 ||
                !parts[2].Equals("AS", StringComparison.OrdinalIgnoreCase) ||
                !parts[4].Equals("USING", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Malformed AUTH");

            return new TcpMessage
            {
                Type = MessageType.AUTH,
                Username = parts[1],
                DisplayName = parts[3],
                Secret = parts[5]
            };
        }

        if (line.StartsWith("JOIN", StringComparison.OrdinalIgnoreCase))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 4 ||
                !parts[2].Equals("AS", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Malformed JOIN");

            return new TcpMessage
            {
                Type = MessageType.JOIN,
                ChannelId = parts[1],
                DisplayName = parts[3]
            };
        }

        if (line.StartsWith("MSG", StringComparison.OrdinalIgnoreCase))
        {
            var prefix = "MSG FROM ";
            var splitIndex = line.IndexOf(" IS ", StringComparison.OrdinalIgnoreCase);
            if (splitIndex == -1 || !line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Malformed MSG");

            var displayName = line.Substring(prefix.Length, splitIndex - prefix.Length);
            var content = line.Substring(splitIndex + 4);
            return new TcpMessage
            {
                Type = MessageType.MSG,
                DisplayName = displayName,
                MessageContents = content
            };
        }

        if (line.StartsWith("ERR", StringComparison.OrdinalIgnoreCase))
        {
            var prefix = "ERR FROM ";
            var splitIndex = line.IndexOf(" IS ", StringComparison.OrdinalIgnoreCase);
            if (splitIndex == -1 || !line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Malformed ERR");

            var displayName = line.Substring(prefix.Length, splitIndex - prefix.Length);
            var content = line.Substring(splitIndex + 4);
            return new TcpMessage
            {
                Type = MessageType.ERR,
                DisplayName = displayName,
                MessageContents = content
            };
        }

        if (line.StartsWith("REPLY", StringComparison.OrdinalIgnoreCase))
        {
            var parts = line.Split(' ', 4, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 4 ||
                !parts[2].Equals("IS", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Malformed REPLY");

            return new TcpMessage
            {
                Type = MessageType.REPLY,
                Result = parts[1].Equals("OK", StringComparison.OrdinalIgnoreCase),
                MessageContents = parts[3]
            };
        }

        if (line.StartsWith("BYE FROM ", StringComparison.OrdinalIgnoreCase))
        {
            var displayName = line.Substring("BYE FROM ".Length);
            return new TcpMessage
            {
                Type = MessageType.BYE,
                DisplayName = displayName
            };
        }

        throw new ArgumentException("Unsupported TCP message format.");
    }

    // https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/switch-expression
    public string ToTcpString() => Type switch
    {
        MessageType.AUTH   => $"AUTH {Username} AS {DisplayName} USING {Secret}\r\n",
        MessageType.JOIN   => $"JOIN {ChannelId} AS {DisplayName}\r\n",
        MessageType.MSG    => $"MSG FROM {DisplayName} IS {MessageContents}\r\n",
        MessageType.ERR    => $"ERR FROM {DisplayName} IS {MessageContents}\r\n",
        MessageType.REPLY  => $"REPLY {(Result ? "OK" : "NOK")} IS {MessageContents}\r\n",
        MessageType.BYE    => $"BYE FROM {DisplayName}\r\n",
        _ => throw new InvalidOperationException("Unsupported message type")
    };
}




