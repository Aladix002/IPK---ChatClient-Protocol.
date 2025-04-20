using System;
using System.Net;
using System.Net.Sockets;
using System.Buffers.Binary;
using System.Threading.Tasks;
using Message;

namespace Transport
{
    public static class UdpConfirmHelper
    {
        // Posle spravu a caka na CONFIRM s rovnakym ID
        public static async Task<bool> SendWithConfirm(UdpClient client, byte[] message, IPEndPoint target, ushort msgId, Arguments args)
        {
            for (int attempt = 1; attempt <= args.MaxRetries; attempt++)
            {
                await client.SendAsync(message, message.Length, target);
                var start = DateTime.UtcNow;

                while ((DateTime.UtcNow - start).TotalMilliseconds < args.UdpTimeout)
                {
                    if (client.Available > 0)
                    {
                        var result = await client.ReceiveAsync();
                        var data = result.Buffer;
                        if (data.Length >= 3 && data[0] == (byte)MessageType.CONFIRM)
                        {
                            var confirm = Confirm.FromBytes(data);
                            if (confirm.RefMessageId == msgId)
                                return true;
                        }
                    }
                    await Task.Delay(10);
                }
            }
            return false;// neuspesne po vsetkych pokusoch
        }

        public static async Task SendConfirm(UdpClient client, ushort refId, IPEndPoint target)
        {
            var confirm = new Confirm { RefMessageId = refId };
            var confirmBytes = confirm.ToBytes(0);
            await client.SendAsync(confirmBytes, confirmBytes.Length, target);
        }
    }
}

