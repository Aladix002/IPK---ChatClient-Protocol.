using System;
using System.Net;
using System.Net.Sockets;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using Message;

namespace Transport;

public class Udp : IChatClient
{
    private State _state = State.start;
    private readonly Arguments _args;
    private readonly IPAddress _serverIp;

    private UdpClient? _client;
    private IPEndPoint? _serverEp;    //odpoved zo servera prvy confirm
    private IPEndPoint? _dynamicEp;   //odpoved z noveho portu po auth reply
    private readonly CancellationTokenSource _cts = new();

    private ushort _nextId = 0;
    private readonly object _sendLock = new();
    private readonly HashSet<ushort> _seenIds = new();
    private readonly ConcurrentDictionary<ushort, (byte[] Datagram, DateTime SentAt, int RetriesLeft)> _awaiting = new();

    private string _displayName = "?";
    private readonly UdpReceiver _receiver;
    private readonly UdpCommandHandler _commandHandler;

    public Udp(Arguments args, IPAddress ip)
    {
        _args = args;
        _serverIp = ip;
        _receiver = new UdpReceiver(this);
        _commandHandler = new UdpCommandHandler(this);
    }

    public async Task Run()
    {
        // https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.udpclient.-ctor?view=net-8.0#system-net-sockets-udpclient-ctor
        _client = new UdpClient(0);
        _client.Client.Blocking = false;
        _serverEp = new IPEndPoint(_serverIp, _args.Port);

        Console.CancelKeyPress += (_, e) => { e.Cancel = true; _ = Stop(); };

        _ = Task.Run(_receiver.ListenForServerMessagesAsync);
        _ = Task.Run(RetryUnconfirmedMessagesAsync);

        await _commandHandler.CommandLoop();
        Cleanup();
    }

    public Task Stop()
    {
        if (_client != null && _state == State.open && _dynamicEp != null)
        {
            var bye = new UdpMessage
            {
                Type = MessageType.BYE,
                DisplayName = _displayName
            };
            var d = bye.ToBytes(GetNextMessageId());
            _client.Send(d, d.Length, _dynamicEp);
        }

        _cts.Cancel();
        _client?.Close();
        _state = State.end;

        Environment.Exit(0);
        return Task.CompletedTask;
    }

    // kontrola casu a retry nedorucenych sprav
    private async Task RetryUnconfirmedMessagesAsync()
    {
        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                await Task.Delay(_args.UdpTimeout / 2);
                var now = DateTime.UtcNow;

                var expired = _awaiting
                    .Where(kv => (now - kv.Value.SentAt).TotalMilliseconds >= _args.UdpTimeout)
                    .ToList();

                foreach (var (messageId, entry) in expired)
                {
                    if (entry.RetriesLeft <= 0)
                    {
                        Console.WriteLine("ERROR: Missing Confirm");
                        _ = Stop(); //ukonci klienta ak sa retry vycerpal
                        continue;
                    }

                    _client!.Send(entry.Datagram, entry.Datagram.Length, _serverEp!);
                    _awaiting[messageId] = (entry.Datagram, now, entry.RetriesLeft - 1);
                }
            }
        }
        catch (TaskCanceledException) { }
    }

     //kontrola confirm, retry a timeout
    public void SendReliable(byte[] datagram)
    {
        ushort messageId = BinaryPrimitives.ReadUInt16BigEndian(datagram.AsSpan(1, 2));

        lock (_sendLock)
        {
            _client!.Send(datagram, datagram.Length, _serverEp!);
            _awaiting[messageId] = (datagram, DateTime.UtcNow, _args.MaxRetries);
            _nextId++;
        }
    }

    public ushort GetNextMessageId() => _nextId;
    public HashSet<ushort> SeenIds => _seenIds;
    public ConcurrentDictionary<ushort, (byte[] Datagram, DateTime SentAt, int RetriesLeft)> Awaiting => _awaiting;
    public CancellationToken CancellationToken => _cts.Token;
    public UdpClient Client => _client!;
    public State CurrentState => _state;
    public void SetState(State newState) => _state = newState;
    public string DisplayName => _displayName;
    public void SetDisplayName(string name) => _displayName = name;
    public IPEndPoint ServerEndpoint => _serverEp!;
    public void SetDynamicEndpoint(IPEndPoint ep) => _dynamicEp = ep;

    private void Cleanup()
    {
        _cts.Cancel();
        try { _client?.Close(); } catch { }
    }
}


