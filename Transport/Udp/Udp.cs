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
        private IPEndPoint? _dynamicServerEP; //auth reply z noveho portu
        private bool _running = true;

        public Udp(Arguments args, IPAddress serverIp)
        {
            _args = args;
            _serverIp = serverIp;
        }

        public async Task Run()
        {
            _client = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
            var serverEP = new IPEndPoint(_serverIp, _args.Port);
            //prijimanie sprav na pozadi
            var receiver = new UdpReceiver(_client, _args);
            _ = Task.Run(() => receiver.ReceiveLoopAsync());

             //nacita prikazy z konzoly
            while (_running)
            {
                var input = Console.ReadLine();
                if (input == null) break;
                if (string.IsNullOrWhiteSpace(input)) continue;

                var tokens = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 0) continue;

                var currentState = UdpStateManager.GetState();
                var command = tokens[0];

                if (command == "/help")
                {
                    HandleHelp();
                    continue;
                }
                else if (currentState == State.start && command != "/auth")
                {
                    Console.WriteLine("ERROR: You must authenticate first");
                    HandleHelp();
                    continue;
                }

                switch (command)
                {
                    //auth cowritten with https://chatgpt.com/ according to specification
                    case "/auth" when currentState == State.start:
                        if (tokens.Length != 4)
                        {
                            Console.Error.WriteLine("ERR: Usage: /auth <username> <secret> <displayName>");
                            break;
                        }

                        var auth = new Auth
                        {
                            Username = tokens[1],
                            Secret = tokens[2],
                            DisplayName = tokens[3]
                        };

                        UdpStateManager.UserDisplayName = auth.DisplayName;
                        ushort authId = UdpStateManager.GetNextMessageId();
                        byte[] authBytes = auth.ToBytes(authId);

                        //cakam na confirm authu
                        if (!await UdpConfirmHelper.SendWithConfirm(_client, authBytes, serverEP, authId, _args))
                        {
                            Console.WriteLine("ERROR: No confirm for AUTH.");
                            break;
                        }
                        // cakam na reply a msg
                        while (true)
                        {
                            var replyRes = await _client.ReceiveAsync();
                            var buffer = replyRes.Buffer;
                            if ((MessageType)buffer[0] != MessageType.REPLY) continue;

                            var reply = Reply.FromBytes(buffer);
                            if (!reply.Result)
                            {
                                Console.WriteLine($"Action Failure: {reply.MessageContent}");
                                break;
                            }

                            Console.WriteLine($"Action Success: {reply.MessageContent}");
                            var confirm = new Confirm { RefMessageId = reply.MessageId };
                            await _client.SendAsync(confirm.ToBytes(0), 3, replyRes.RemoteEndPoint);
                            //uvodna msg
                            while (true)
                            {
                                var msgRes = await _client.ReceiveAsync();
                                var msgBuf = msgRes.Buffer;
                                if ((MessageType)msgBuf[0] == MessageType.MSG)
                                {
                                    var msg = Msg.FromBytes(msgBuf);
                                    Console.WriteLine($"{msg.DisplayName}: {msg.MessageContents}");
                                    await _client.SendAsync(new Confirm { RefMessageId = msg.MessageId }.ToBytes(0), 3, msgRes.RemoteEndPoint);
                                    break;
                                }
                            }

                            _dynamicServerEP = replyRes.RemoteEndPoint;
                            UdpStateManager.SetState(State.open);
                            break;
                        }
                        break;

                    case "/auth":
                        Console.WriteLine("INFO: Already authenticated or invalid state.");
                        break;

                    case "/join" when currentState == State.open:
                        if (tokens.Length != 2 || UdpStateManager.UserDisplayName == null)
                        {
                            Console.Error.WriteLine("ERR: Usage: /join <channelId>");
                            break;
                        }

                        var join = new Join
                        {
                            ChannelId = tokens[1],
                            DisplayName = UdpStateManager.UserDisplayName
                        };
                        ushort joinId = UdpStateManager.GetNextMessageId();
                        byte[] joinBytes = join.ToBytes(joinId);
                        await UdpConfirmHelper.SendWithConfirm(_client, joinBytes, _dynamicServerEP!, joinId, _args);
                        break;

                    case "/rename" when currentState == State.open:
                        if (tokens.Length != 2)
                        {
                            Console.Error.WriteLine("ERR: Usage: /rename <displayName>");
                            break;
                        }

                        UdpStateManager.UserDisplayName = tokens[1];
                        Console.WriteLine($"Renamed to {UdpStateManager.UserDisplayName}");
                        break;

                    default:
                        if (command.StartsWith("/"))
                        {
                            Console.Error.WriteLine("ERR: Unknown or disallowed command");
                            if (currentState == State.start)
                            {
                                Console.WriteLine("ERROR: You must authenticate first");
                            }
                        }
                        //msg spravy 
                        else if (currentState == State.open)
                        {
                            var msg = new Msg
                            {
                                DisplayName = UdpStateManager.UserDisplayName ?? "?",
                                MessageContents = input
                            };
                            ushort msgId = UdpStateManager.GetNextMessageId();
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
        
        //bye a koniec klienta
        public async Task Stop()
        {
            if (_dynamicServerEP != null && _client != null && UdpStateManager.GetState() == State.open)
            {
                ushort byeId = UdpStateManager.GetNextMessageId();
                var bye = new Bye
                {
                    DisplayName = UdpStateManager.UserDisplayName ?? "?"
                };
                byte[] byeBytes = bye.ToBytes(byeId);
                await _client.SendAsync(byeBytes, byeBytes.Length, _dynamicServerEP);
            }

            _running = false;
            UdpStateManager.SetState(State.end);
            _client?.Close();
        }

        public static void HandleHelp()
        {
            Console.WriteLine("Available commands:");
            Console.WriteLine("/auth <username> <secret> <displayName>");
            Console.WriteLine("/join <channelId>");
            Console.WriteLine("/rename <displayName>");
            Console.WriteLine("/help");
        }
    }
}





