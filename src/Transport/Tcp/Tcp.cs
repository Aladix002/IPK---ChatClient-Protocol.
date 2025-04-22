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
    public async Task Run()
    {
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _socket.Connect(_args.Ip, _args.Port);

        var listen = _receiver.ListenForServerMessages(_socket); // prijem zo servera
        var input = _commandHandler.HandleUserInput(_socket, _args); // posielanie na server

        await Task.WhenAny(listen, input); // caka na eof alebo bye 
        await _stateManager.Stop(_socket);
    }
    public Task Stop() => _stateManager.Stop(_socket); // ctrl c volanie z mainu
}
}





