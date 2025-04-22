using System.Net.Sockets;
using System.Text;
using Message;

namespace Transport;

public class Tcp : IChatClient
{
    private readonly Arguments _args;
    private Socket _socket = null!;
    private string? _displayName;
    private State _state = State.start;
    private bool _shutdownInitiated = false;
    private readonly TcpCommandHandler _commandHandler;
    private readonly TcpReceiver _receiver;

    public Tcp(Arguments args)
    {
        _args = args;
        _commandHandler = new TcpCommandHandler(this);
        _receiver = new TcpReceiver(this);
    }

    // hlavna metoda spustenie z mainu
    // https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.networkstream.writeasync
    public async Task Run()
    {
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _socket.Connect(_args.Ip, _args.Port);

        Console.CancelKeyPress += async (_, e) =>
        {
            e.Cancel = true;
            await Stop();
        };

        var receiver = _receiver.ListenForMessages();
        var input = _commandHandler.HandleUserInput();

        await Task.WhenAny(receiver, input);
        await Stop();
    }

    public async Task Stop()
    {
        if (_shutdownInitiated) return;
        _shutdownInitiated = true;

        try
        {
            if (_state == State.open && _displayName != null)
            {
                var bye = new TcpMessage
                {
                    Type = MessageType.BYE,
                    DisplayName = _displayName
                };
                var data = Encoding.ASCII.GetBytes(bye.ToTcpString());
                await _socket.SendAsync(data, SocketFlags.None);
            }
        }
        catch { }

        _socket.Close();
        Environment.Exit(0);
    }

    public Socket Socket => _socket;
    public State CurrentState => _state;
    public void SetState(State s) => _state = s;
    public string? DisplayName => _displayName;
    public void SetDisplayName(string? name) => _displayName = name;
    public Arguments Args => _args;
    public bool IsShutdown => _shutdownInitiated;
    public void MarkShutdown() => _shutdownInitiated = true;
}





