using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using Message;

namespace Transport
{
public class Tcp : IChatClient
{
    private readonly Arguments _args;
    private Socket _socket = null!;
    private readonly TcpStateManager _stateManager;
    private readonly TcpCommandHandler _commandHandler;
    private readonly TcpReceiver _receiver;

    public Tcp(Arguments args)
    {
        _args = args;
        _stateManager = new TcpStateManager();
        _commandHandler = new TcpCommandHandler(_stateManager);
        _receiver = new TcpReceiver(_stateManager);
    }
    // https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.networkstream.writeasync
    public async Task RunAsync()
    {
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _socket.Connect(_args.Ip, _args.Port);

        var listen = _receiver.ListenForServerMessages(_socket);
        var input = _commandHandler.HandleUserInput(_socket, _args);

        await Task.WhenAny(listen, input);
        await _stateManager.DisconnectAsync(_socket);
    }

    public Task DisconnectAsync() => _stateManager.DisconnectAsync(_socket);
}
}





