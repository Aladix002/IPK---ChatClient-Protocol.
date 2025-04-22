using System;
using System.Threading.Tasks;
using Message;

namespace Transport;

public class UdpCommandHandler
{
    private readonly Udp _udp;

    public UdpCommandHandler(Udp udp)
    {
        _udp = udp;
    }

    public async Task CommandLoop()
    {
        while (!_udp.CancellationToken.IsCancellationRequested)
        {
            var line = Console.ReadLine();
            if (line == null) break;

            switch (_udp.CurrentState)
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

        _udp.SetDisplayName(auth.DisplayName);
        var dgram = auth.ToBytes(_udp.GetNextMessageId());
        _udp.SendReliable(dgram);
        Console.WriteLine($"Server: AUTH {p[1]} AS {p[3]} USING {p[2]}");

        _udp.SetState(State.auth);
        return Task.CompletedTask;
    }

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
                        DisplayName = _udp.DisplayName
                    };
                    _udp.SendReliable(join.ToBytes(_udp.GetNextMessageId()));
                    Console.WriteLine($"Server: JOIN {command[1]} AS {_udp.DisplayName}");
                }
            }
            else if (commandName == "/rename")
            {
                if (command.Length >= 2)
                {
                    _udp.SetDisplayName(command[1]);
                    Console.WriteLine($"Renamed to {_udp.DisplayName}");
                }
                else
                {
                    TcpCommandHandler.HandleHelp();
                }
            }
            else if (commandName == "/bye")
            {
                _ = _udp.Stop();
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
                DisplayName = _udp.DisplayName,
                MessageContents = line
            };
            _udp.SendReliable(msg.ToBytes(_udp.GetNextMessageId()));
            Console.WriteLine($"Server: MSG FROM {_udp.DisplayName} IS {line}");
        }

        return Task.CompletedTask;
    }
}