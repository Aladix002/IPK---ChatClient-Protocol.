using CommandLine;
using System.Net.Sockets;
using System.Text;
using Message;
using Client.Messages;

return Parser.Default
    .ParseArguments<Arguments>(args)
    .MapResult(
        RunClient,
        _ => 1
    );

static int RunClient(Arguments opts)
{
    Console.WriteLine($"Protocol: {opts.Protocol}");
    Console.WriteLine($"Connecting to: {opts.Ip}:{opts.Port}");

    if (opts.Protocol.ToLower() != "tcp")
    {
        Console.WriteLine("Only TCP is implemented for now.");
        return 0;
    }

    try
    {
        using var client = new TcpClient(opts.Ip, opts.Port);
        using var stream = client.GetStream();

        string? displayName = string.Empty;
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            var bye = new Bye();
            var byeBytes = Encoding.ASCII.GetBytes(bye.ToTcpString());
            stream.Write(byeBytes, 0, byeBytes.Length);
            stream.Close();
            client.Close();
            Environment.Exit(0);
        };

        while (true)
        {
            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input)) continue;

            if (input.StartsWith("/"))
            {
                var parts = input[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;

                switch (parts[0])
                {
                    case "auth" when parts.Length == 4:
                        try
                        {
                            var auth = new Auth
                            {
                                Username = parts[1],
                                Secret = parts[2],
                                DisplayName = parts[3]
                            };
                            displayName = parts[3];
                            var bytes = Encoding.ASCII.GetBytes(auth.ToTcpString());
                            stream.Write(bytes, 0, bytes.Length);
                            HandleReply(stream);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"ERROR: {e.Message}");
                        }
                        break;

                    case "join" when parts.Length == 2:
                        if (string.IsNullOrEmpty(displayName))
                        {
                            Console.WriteLine("ERROR: You must authenticate first.");
                            break;
                        }
                        try
                        {
                            var join = new Join
                            {
                                ChannelId = parts[1],
                                DisplayName = displayName
                            };
                            var bytes = Encoding.ASCII.GetBytes(join.ToTcpString());
                            stream.Write(bytes, 0, bytes.Length);
                            HandleReply(stream);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"ERROR: {e.Message}");
                        }
                        break;

                    case "rename" when parts.Length == 2:
                        displayName = parts[1];
                        Console.WriteLine($"Renamed to {displayName}");
                        break;

                    case "help":
                        Console.WriteLine("/auth <username> <secret> <displayName> — authenticate with server");
                        Console.WriteLine("/join <channelId> — join a specific channel");
                        Console.WriteLine("/rename <displayName> — change your display name");
                        Console.WriteLine("/help — show this help text");
                        break;

                    default:
                        Console.WriteLine("ERROR: Unknown or malformed command");
                        break;
                }
            }
            else
            {
                if (string.IsNullOrEmpty(displayName))
                {
                    Console.WriteLine("ERROR: You must authenticate before sending messages");
                    continue;
                }
                var msg = new Msg
                {
                    DisplayName = displayName,
                    MessageContents = input
                };
                var bytes = Encoding.ASCII.GetBytes(msg.ToString());
                stream.Write(bytes, 0, bytes.Length);
            }
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine("Connection error: " + ex.Message);
        return 1;
    }
}

static void HandleReply(NetworkStream stream)
{
    byte[] buffer = new byte[1024];
    int bytes = stream.Read(buffer, 0, buffer.Length);
    string response = Encoding.ASCII.GetString(buffer, 0, bytes);

    foreach (var line in response.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
    {
        try
        {
            var reply = Reply.FromTcpString(line);
            Console.WriteLine(reply.Result
                ? $"Action Success: {reply.MessageContent}"
                : $"Action Failure: {reply.MessageContent}");
        }
        catch
        {
            Console.WriteLine($"Unknown server response: {line}");
        }
    }
}
