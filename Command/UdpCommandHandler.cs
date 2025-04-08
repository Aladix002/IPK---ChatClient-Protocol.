using System;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Text;
using Message;

public class UdpCommandHandler
{
    private readonly UdpClient _client;
    private readonly IPEndPoint _serverEP;
    private readonly Arguments _options;
    private readonly Func<Task<int>> _waitReply;
    private readonly Func<byte[], Task<int>> _sendMessage;
    private readonly Func<Task> _sendBye;
    private string? _displayName;

    public UdpCommandHandler(
        UdpClient client,
        IPEndPoint serverEP,
        Arguments options,
        Func<Task<int>> waitReply,
        Func<byte[], Task<int>> sendMessage,
        Func<Task> sendBye)
    {
        _client = client;
        _serverEP = serverEP;
        _options = options;
        _waitReply = waitReply;
        _sendMessage = sendMessage;
        _sendBye = sendBye;
    }

    public async Task<bool> HandleAuth(string[] words, Action<string> setDisplayName)
    {
        if (words.Length != 4)
        {
            Console.Error.WriteLine("ERR: Wrong input, expected /auth <username> <secret> <displayName>");
            return false;
        }

        _displayName = words[3];
        if (_displayName.Length > 20)
        {
            Console.Error.WriteLine("ERR: Display name too long.");
            return false;
        }

        var auth = new Auth
        {
            Username = words[1],
            Secret = words[2],
            DisplayName = _displayName
        };

        setDisplayName(_displayName);

        int confirmed = await _sendMessage(auth.ToBytes(Udp.id));
        if (confirmed == 1)
        {
            int reply = await _waitReply();
            if (reply == 1)
                return true;
        }

        await _sendBye();
        return false;
    }

    public async Task<bool> HandleJoin(string[] words, string? displayName)
    {
        if (displayName == null)
        {
            Console.Error.WriteLine("ERR: You must authenticate first.");
            return false;
        }

        if (words.Length != 2)
        {
            Console.Error.WriteLine("ERR: Wrong input, expected /join <channelId>");
            return false;
        }

        if (words[1].Length > 20)
        {
            Console.Error.WriteLine("ERR: Channel ID too long.");
            return false;
        }

        var join = new Join
        {
            ChannelId = words[1],
            DisplayName = displayName
        };

        int confirmed = await _sendMessage(join.ToBytes(Udp.id));
        if (confirmed == 1)
        {
            int reply = await _waitReply();
            if (reply == 1 || reply == 0)
                return true;
        }

        await _sendBye();
        return false;
    }

    public bool HandleRename(string[] words, ref string? displayName)
    {
        if (words.Length != 2)
        {
            Console.Error.WriteLine("ERR: Wrong input, expected /rename <displayName>");
            return false;
        }

        var name = words[1];
        if (name.Length > 20 || !Regex.IsMatch(name, @"^[\x20-\x7E]*$"))
        {
            Console.Error.WriteLine("ERR: Invalid display name format");
            return false;
        }

        displayName = name;
        _displayName = name;
        Console.WriteLine($"Renamed to {displayName}");
        return true;
    }

    public async Task HandleMessage(string input, string? displayName)
    {
        if (string.IsNullOrEmpty(displayName))
        {
            Console.Error.WriteLine("ERR: You must authenticate before sending messages");
            return;
        }

        if (input.Length > 1400)
        {
            Console.Error.WriteLine("ERR: Message too long");
            return;
        }

        var msg = new Msg
        {
            DisplayName = displayName,
            MessageContents = input
        };

        int confirmed = await _sendMessage(msg.ToBytes(Udp.id));
        if (confirmed == 0)
            await _sendBye();
    }
}
