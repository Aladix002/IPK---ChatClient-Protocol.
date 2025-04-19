using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection.Metadata;
using System.Threading.Tasks;
using Message;

namespace Transport
{
    public class Udp : IChatClient
{
    private readonly Arguments _args;
    private readonly IPAddress _serverIp;
    private UdpClient? _client;
    private IPEndPoint? _dynamicServerEP;
    private bool _running = true;

    public Udp(Arguments args, IPAddress serverIp)
    {
        _args = args;
        _serverIp = serverIp;
    }

    public async Task RunAsync()
    {
        _client = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        var serverEP = new IPEndPoint(_serverIp, _args.Port);

        var auth = new UdpAuthHandler(_client, _args);
        _dynamicServerEP = await auth.AuthenticateAsync(serverEP);

        var receiver = new UdpReceiver(_client, _args);
        _ = Task.Run(() => receiver.ReceiveLoopAsync());

        while (_running)
        {
            var input = Console.ReadLine();
            if (input == null) break;
            if (string.IsNullOrWhiteSpace(input)) continue;

            var tokens = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) continue;

            var currentState = UdpState.GetState();
            var command = tokens[0];

            if (command == "/help")
            {
                HandleHelp();
                continue;
            }

            if (currentState == State.start && command != "/auth")
            {
                Console.WriteLine("ERROR: You must authenticate first");
                HandleHelp();
                continue;
            }

            switch (command)
            {
                case "/help" when currentState is State.start or State.auth:
                    HandleHelp();
                    break;
                case "/auth" when currentState is State.start or State.auth:
                    Console.WriteLine("INFO: Already authenticated.");
                    break;

                case "/auth" when currentState == State.open:
                    Console.WriteLine("ERROR: Already authenticated – cannot use /auth again.");
                    break;

                case "/join" when currentState == State.open:
                    if (tokens.Length != 2 || UdpState.UserDisplayName == null)
                    {
                        Console.Error.WriteLine("ERR: Usage: /join <channelId>");
                        break;
                    }

                    var join = new Join
                    {
                        ChannelId = tokens[1],
                        DisplayName = UdpState.UserDisplayName
                    };
                    ushort joinId = UdpState.GetNextMessageId();
                    byte[] joinBytes = join.ToBytes(joinId);
                    await UdpConfirmHelper.SendWithConfirm(_client, joinBytes, _dynamicServerEP!, joinId, _args);
                    break;

                case "/rename" when currentState == State.open:
                    if (tokens.Length != 2)
                    {
                        Console.Error.WriteLine("ERR: Usage: /rename <displayName>");
                        break;
                    }

                    UdpState.UserDisplayName = tokens[1];
                    Console.WriteLine($"Renamed to {UdpState.UserDisplayName}");
                    break;

                default:
                    if (command.StartsWith("/"))
                    {
                        Console.Error.WriteLine("ERR: Unknown or disallowed command");
                    }
                    else if (currentState == State.open)
                    {
                        var msg = new Msg
                        {
                            DisplayName = UdpState.UserDisplayName ?? "?",
                            MessageContents = input
                        };
                        ushort msgId = UdpState.GetNextMessageId();
                        byte[] msgBytes = msg.ToBytes(msgId);
                        await UdpConfirmHelper.SendWithConfirm(_client, msgBytes, _dynamicServerEP!, msgId, _args);
                    }
                    else
                    {
                        Console.WriteLine("ERROR: You must authenticate first");
                    }
                    break;
            }
        }
    }

    public async Task DisconnectAsync()
    {
        if (_dynamicServerEP != null && _client != null && UdpState.GetState() == State.open)
        {
            ushort byeId = UdpState.GetNextMessageId();
            var bye = new Bye
            {
                DisplayName = UdpState.UserDisplayName ?? "?"
            };
            byte[] byeBytes = bye.ToBytes(byeId);
            await _client.SendAsync(byeBytes, byeBytes.Length, _dynamicServerEP);
        }

        _running = false;
        UdpState.SetState(State.end);
        _client?.Close();
    }

    public static void HandleHelp()
    {
        Console.WriteLine("Available commands:");
        Console.WriteLine("/auth <username> <secret> <displayName>");
        Console.WriteLine("/join <channelId>");
        Console.WriteLine("/rename <displayName>");
        Console.WriteLine("/help");
        Console.Out.Flush();
    }
}
}





