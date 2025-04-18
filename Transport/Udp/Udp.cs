using System;
using System.Net;
using System.Net.Sockets;
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
            string? input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input)) continue;
            if (UdpState.GetState() != State.open)
            {
                Console.WriteLine("ERROR: You must authenticate first");
                continue;
            }

            if (input.StartsWith("/rename "))
            {
                var tokens = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 2)
                {
                    UdpState.UserDisplayName = tokens[1];
                    Console.WriteLine($"Renamed to '{UdpState.UserDisplayName}'");
                }
                continue;
            }

            if (input.StartsWith("/join "))
            {
                var tokens = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 2)
                {
                    var join = new Join
                    {
                        ChannelId = tokens[1],
                        DisplayName = UdpState.UserDisplayName ?? "?"
                    };

                    ushort joinId = UdpState.GetNextMessageId();
                    byte[] joinBytes = join.ToBytes(joinId);
                    if (!await UdpConfirmHelper.SendWithConfirm(_client, joinBytes, _dynamicServerEP!, joinId, _args)) continue;
                }
                continue;
            }

            var message = new Msg
            {
                DisplayName = UdpState.UserDisplayName ?? "?",
                MessageContents = input
            };

            ushort currentId = UdpState.GetNextMessageId();
            byte[] msgBytes = message.ToBytes(currentId);
            _ = await UdpConfirmHelper.SendWithConfirm(_client, msgBytes, _dynamicServerEP!, currentId, _args);
        }
    }

    public async Task DisconnectAsync()
    {
        _running = false;
        UdpState.SetState(State.end);

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

        _client?.Close();
        await Task.CompletedTask;
    }
}
}




