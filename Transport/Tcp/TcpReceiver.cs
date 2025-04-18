using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Message;

namespace Transport
{
public class TcpReceiver
{
    private readonly TcpStateManager _stateManager;

    public TcpReceiver(TcpStateManager stateManager)
    {
        _stateManager = stateManager;
    }

    public async Task ListenForServerMessages(Socket socket)
    {
        var buffer = new byte[2048];
        var sb = new StringBuilder();

        while (true)
        {
            int count = await socket.ReceiveAsync(buffer, SocketFlags.None);
            if (count == 0) break;

            sb.Append(Encoding.UTF8.GetString(buffer, 0, count));
            var data = sb.ToString();

            int idx;
            while ((idx = data.IndexOf("\r\n", StringComparison.Ordinal)) != -1)
            {
                var line = data[..idx];
                data = data[(idx + 2)..];

                TcpMessage msg;
                try
                {
                    msg = TcpMessage.ParseTcp(line);
                }
                catch
                {
                    Console.WriteLine("ERROR: Malformed message received.");
                    await _stateManager.SendErrorAndExit(socket, "Malformed message received");
                    return;
                }

                switch (msg.Type)
                {
                    case MessageType.REPLY:
                        Console.WriteLine(msg.Result
                            ? $"Action Success: {msg.MessageContents}"
                            : $"Action Failure: {msg.MessageContents}");
                        if (msg.Result)
                        {
                            _stateManager.SetState(State.open);
                        }
                        break;

                    case MessageType.MSG:
                        Console.WriteLine($"{msg.DisplayName}: {msg.MessageContents}");
                        break;

                    case MessageType.ERR:
                        Console.WriteLine($"ERROR FROM {msg.DisplayName}: {msg.MessageContents}");
                        await _stateManager.DisconnectAsync(socket);
                        return;

                    case MessageType.BYE:
                        Console.WriteLine("Received BYE, exiting...");
                        await _stateManager.DisconnectAsync(socket);
                        Environment.Exit(0);
                        return;
                }
            }

            sb.Clear();
            sb.Append(data);
        }
    }
}
}
