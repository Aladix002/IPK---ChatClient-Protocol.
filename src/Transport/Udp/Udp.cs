#nullable enable
using System;
using System.Net;
using System.Net.Sockets;
using System.Buffers.Binary;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text;
using System.Linq;
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
    private readonly ConcurrentDictionary<ushort, Outstanding> _awaiting = new();

    private string _displayName = "?";

    private class Outstanding
    {
        public byte[] Datagram = Array.Empty<byte>();
        public DateTime SentAt;
        public int RetriesLeft;
    }

    public Udp(Arguments args, IPAddress ip)
    {
        _args = args;
        _serverIp = ip;
    }

    public async Task Run()
    {
        // https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.udpclient.-ctor?view=net-8.0#system-net-sockets-udpclient-ctor
        _client = new UdpClient(0);
        _client.Client.Blocking = false;

        _serverEp = new IPEndPoint(_serverIp, _args.Port);

        Console.CancelKeyPress += (_, e) => { e.Cancel = true; _ = Stop(); };

        _ = Task.Run(ListenForServerMessagesAsync);       
        _ = Task.Run(RetryUnconfirmedMessagesAsync);    

        await CommandLoop(); 
        Cleanup();
    }


    public Task Stop()
    {
        //bye pri ukonceni 
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

        // ukoncenie celej aplikacie
        Environment.Exit(0);
        return Task.CompletedTask;
    }

    //citanie prikazov uzivatela
    private async Task CommandLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            var line = Console.ReadLine();
            if (line == null) break;

            switch (_state)
            {
                case State.start:
                    if (line.StartsWith("/auth ")) await HandleAuth(line);
                    else if (line == "/help") TcpCommandHandler.HandleHelp();
                    else Console.WriteLine("ERROR: You must authenticate first");
                    break;
                case State.auth:
                    break;
                case State.open:
                    await HandleOpen(line);
                    break;
            }
        }
    }

    private Task HandleAuth(string line)
    {
        var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length != 4)
        {
            Console.WriteLine("Usage: /auth <username> <secret> <displayName>");
            return Task.CompletedTask;
        }

        var auth = new UdpMessage
        {
            Type = MessageType.AUTH,
            Username = p[1],
            Secret = p[2],
            DisplayName = p[3]
        };

        _displayName = auth.DisplayName;
        var dgram = auth.ToBytes(GetNextMessageId());
        SendReliable(dgram);
        Console.WriteLine($"[Sent] AUTH {p[1]} AS {p[3]} USING {p[2]}");

        _state = State.auth;
        return Task.CompletedTask;
    }


    // spracovanie prikazov v open stave
    private Task HandleOpen(string line)
    {
        if (line.StartsWith("/"))
        {
            var command = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (command.Length == 0)
            {
                Console.WriteLine("ERROR: Invalid command");
                return Task.CompletedTask;
            }

            var commandName = command[0];

            if (commandName == "/help")
            {
                TcpCommandHandler.HandleHelp();
            }
            else if (commandName == "/join")
            {
                if (command.Length < 2)
                {
                    TcpCommandHandler.HandleHelp();
                }
                else
                {
                    var join = new UdpMessage
                    {
                        Type = MessageType.JOIN,
                        ChannelId = command[1],
                        DisplayName = _displayName
                    };
                    SendReliable(join.ToBytes(GetNextMessageId()));
                    Console.WriteLine($"Server: JOIN {command[1]} AS {_displayName}");
                }
            }
            else if (commandName == "/rename")
            {
                if (command.Length >= 2)
                {
                    _displayName = command[1];
                    Console.WriteLine($"Renamed to {_displayName}");
                }
                else
                {
                    TcpCommandHandler.HandleHelp();
                }
            }
            else if (commandName == "/bye")
            {
                _ = Stop();
            }
            else
            {
                Console.WriteLine("ERROR: Unknown command");
            }
        }
        else
        {
            var msg = new UdpMessage
            {
                Type = MessageType.MSG,
                DisplayName = _displayName,
                MessageContents = line
            };
            SendReliable(msg.ToBytes(GetNextMessageId()));
            Console.WriteLine($"[Sent] MSG FROM {_displayName} IS {line}");
        }

        return Task.CompletedTask;
    }


    //kontrola confirm, retry a timeout
    private void SendReliable(byte[] datagram)
    {
        ushort messageId = BinaryPrimitives.ReadUInt16BigEndian(datagram.AsSpan(1, 2));

        var retryEntry = new Outstanding
        {
            Datagram = datagram,
            SentAt = DateTime.UtcNow,
            RetriesLeft = _args.MaxRetries
        };

        lock (_sendLock)
        {
            _client!.Send(datagram, datagram.Length, _serverEp!);

            _awaiting[messageId] = retryEntry;

            _nextId++;
        }
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

                foreach (var (messageId, msg) in expired)
                {
                    if (msg.RetriesLeft <= 0)
                    {
                        Console.WriteLine("ERROR: Missing Confirm");
                        _ = Stop(); // ukonci klienta ak sa retry vycerpal
                        continue;
                    }

                    _client!.Send(msg.Datagram, msg.Datagram.Length, _serverEp!);
                    msg.SentAt = now;
                    msg.RetriesLeft--;
                }
            }
        }
        catch (TaskCanceledException) { }
    }


    // prijem sprav zo servera
    private async Task ListenForServerMessagesAsync()
    {
        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                var result = await _client!.ReceiveAsync(_cts.Token);
                var data = result.Buffer;
                var sender = result.RemoteEndPoint;

                if (data.Length < 3)
                    continue;

                var msg = UdpMessage.ParseUdp(data);

                if (msg.Type == MessageType.CONFIRM)
                {
                    _awaiting.TryRemove(msg.MessageId, out _);
                    continue;
                }

                // potvrdenie spravy (okrem CONFIRM)
                var confirm = new byte[] { 0x00, data[1], data[2] };
                _client.Send(confirm, 3, sender);

                if (!_seenIds.Add(msg.MessageId))
                    continue;

                switch (msg.Type)
                {
                    case MessageType.REPLY:
                        HandleReply(msg, sender);
                        break;
                    case MessageType.MSG:
                        HandleServerMsg(msg);
                        break;
                    case MessageType.BYE:
                        Console.WriteLine("Server: BYE");
                        await Stop();
                        break;
                    case MessageType.ERR:
                        HandleServerErr(msg);
                        break;
                    default:
                        Console.WriteLine($"[WARN] Unknown message type: {msg.Type}");
                        break;
                }
            }
        }
        catch (OperationCanceledException) { }
    }


    private void HandleReply(UdpMessage msg, IPEndPoint ep)
    {
        Console.WriteLine(msg.Result == true
            ? $"Action Success: {msg.MessageContents}"
            : $"Action Failure: {msg.MessageContents}");

        if (_state == State.auth)
        {
            if (msg.Result == true)
            {
                _dynamicEp = ep;
                _state = State.open;
            }
            else
            {
                _state = State.start;
            }
        }
    }

    private void HandleServerMsg(UdpMessage msg)
    {
        Console.WriteLine($"{msg.DisplayName}: {msg.MessageContents}");
    }

    private void HandleServerErr(UdpMessage msg)
    {
        Console.WriteLine($"ERROR FROM {msg.DisplayName}: {msg.MessageContents}");
        _ = Stop();
    }

    private ushort GetNextMessageId() => _nextId;

    private void Cleanup()
    {
        _cts.Cancel();
        try { _client?.Close(); } catch { }
    }
}
