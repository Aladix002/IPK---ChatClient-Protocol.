using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using Message;

namespace Transport
{
public class UdpReceiver
{
    private readonly UdpClient _client;
    private readonly Arguments _args;

    public UdpReceiver(UdpClient client, Arguments args)
    {
        _client = client;
        _args = args;
    }

    public async Task ReceiveLoopAsync()
    {
        while (UdpStateManager.GetState() != State.end)
        {
            try
            {
                var incoming = await _client.ReceiveAsync();
                var buffer = incoming.Buffer;
                ushort id = UdpConfirmHelper.ReadMessageId(buffer);
                UdpStateManager.UpdateMessageIdIfNeeded(id);

                await UdpConfirmHelper.SendConfirmIfNeeded(_client, buffer, incoming.RemoteEndPoint);

                if (UdpStateManager.IsDuplicate(id)) continue;

                MessageType type = (MessageType)buffer[0];

                if (type == MessageType.MSG)
                {
                    var msg = Msg.FromBytes(buffer);
                    Console.WriteLine($"{msg.DisplayName}: {msg.MessageContents}");
                }
            }
            catch { break; }
        }
    }
}
}
