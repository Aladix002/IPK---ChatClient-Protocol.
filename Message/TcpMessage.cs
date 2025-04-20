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

    //textova reprezentacia protokolu na TcpMessage
    public static TcpMessage ParseTcp(string line)
    {
        if (line.StartsWith("AUTH"))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 5 || parts[2] != "AS" || parts[4] != "USING")
                throw new ArgumentException("Malformed AUTH");

            return new TcpMessage
            {
                Type = MessageType.AUTH,
                Username = parts[1],
                DisplayName = parts[3],
                Secret = parts[5]
            };
        }
        if (line.StartsWith("JOIN"))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 4 || parts[2] != "AS")
                throw new ArgumentException("Malformed JOIN");

            return new TcpMessage
            {
                Type = MessageType.JOIN,
                ChannelId = parts[1],
                DisplayName = parts[3]
            };
        }

        if (line.StartsWith("MSG"))
        {
            var parts = line.Split(" IS ", 2);// rozdeli prvy vyskyt IS 
            return new TcpMessage
            {
                Type = MessageType.MSG,
                DisplayName = parts[0].Substring("MSG FROM ".Length),
                MessageContents = parts[1]
            };
        }

        if (line.StartsWith("ERR"))
        {
            var parts = line.Split(" IS ", 2);
            return new TcpMessage
            {
                Type = MessageType.ERR,
                DisplayName = parts[0].Substring("ERR FROM ".Length),
                MessageContents = parts[1]
            };
        }

        // REPLY OK|NOK IS <msg>
        if (line.StartsWith("REPLY"))
        {
            var parts = line.Split(' ', 4);
            return new TcpMessage
            {
                Type = MessageType.REPLY,
                Result = parts[1] == "OK",//ture ak ok, inak nok
                MessageContents = parts[3]
            };
        }

        if (line.StartsWith("BYE FROM "))
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

