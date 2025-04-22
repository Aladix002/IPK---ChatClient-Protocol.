using System;
using System.Net.Sockets;
using System.Text;
using Message;

namespace Transport;

public class TcpReceiver
{
    private readonly Tcp _tcp;

    public TcpReceiver(Tcp tcp)
    {
        _tcp = tcp;
    }

    //pocuvanie servera
    public async Task ListenForMessages()
    {
        var buffer = new byte[2048];//prijimaci buffer data
	    var sb = new StringBuilder();//medzi‑buffer na cast ramca

        while (true)
        {
            int count = await _tcp.Socket.ReceiveAsync(buffer, SocketFlags.None);
            if (count == 0) break;

            sb.Append(Encoding.UTF8.GetString(buffer, 0, count));
            var data = sb.ToString();

            int idx;
            // caka na cele ramce
            while ((idx = data.IndexOf("\r\n", StringComparison.Ordinal)) != -1)
            {
                var line = data[..idx];//kompletna sprava
                data = data[(idx + 2)..];//prazdny alebo nedokoncený frame

                TcpMessage msg;
                try
                {
                    msg = TcpMessage.ParseTcp(line);
                }
                catch
                {
                    Console.WriteLine("ERROR: Malformed message received."); //sprava sa nesparsovala spravne - malformed
                    await SendErrorAndExit("Malformed message received");
                    return;
                }

                switch (msg.Type)
                {
                    case MessageType.REPLY:
                        Console.WriteLine(msg.Result
                            ? $"Action Success: {msg.MessageContents}"
                            : $"Action Failure: {msg.MessageContents}");
                        if (msg.Result) _tcp.SetState(State.open);
                        break;

                    case MessageType.MSG:
                        Console.WriteLine($"{msg.DisplayName}: {msg.MessageContents}");
                        break;

                    case MessageType.ERR:
                        Console.WriteLine($"ERROR FROM {msg.DisplayName}: {msg.MessageContents}");
                        await _tcp.Stop();
                        return;

                    case MessageType.BYE:
                        Console.WriteLine("Received BYE, exiting...");
                        await _tcp.Stop();
                        return;
                }
            }

            sb.Clear();
            sb.Append(data);//nekompletny frame do dalsieho cyklu
        }
    }

    private async Task SendErrorAndExit(string message)
    {
        try
        {
            var err = new TcpMessage
            {
                Type = MessageType.ERR,
                DisplayName = _tcp.DisplayName ?? "client",
                MessageContents = message
            };
            await _tcp.Socket.SendAsync(Encoding.ASCII.GetBytes(err.ToTcpString()), SocketFlags.None);
        }
        catch { }

        await _tcp.Stop();
        Environment.Exit(1);
    }
}


