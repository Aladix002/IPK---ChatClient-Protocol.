using CommandLine;
using Message;
using System.Text;
using System.Net.Sockets;

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

    if (opts.Protocol.ToLower() == "tcp")
    {
        try
        {
            using var client = new TcpClient(opts.Ip, opts.Port);
            using var stream = client.GetStream();

            var auth = new Auth
            {
                Username = "xbotlo01", 
                DisplayName = "FiFo",    
                Secret = "862b61d6-5a34-4336-aa1c-5515819c69e3"
            };

            // Odoslanie AUTH správy
            string authString = auth.ToTcpString();
            byte[] request = Encoding.ASCII.GetBytes(authString);
            stream.Write(request, 0, request.Length);
            Console.WriteLine($"Sent AUTH: {authString.Trim()}");

            // Čakanie na odpoveď
            byte[] buffer = new byte[1024];
            int bytes = stream.Read(buffer, 0, buffer.Length);
            string response = Encoding.ASCII.GetString(buffer, 0, bytes);
            Console.WriteLine($"Raw response: {response.Trim()}");

            // Spracovanie odpovede ako REPLY
            try
            {
                var reply = Reply.FromTcpString(response);
                Console.WriteLine(reply.Result
                    ? $"✅ Success: {reply.MessageContent}"
                    : $"❌ Failure: {reply.MessageContent}");
            }
            catch (Exception parseEx)
            {
                Console.Error.WriteLine("Failed to parse REPLY message: " + parseEx.Message);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Connection error: " + ex.Message);
            return 1;
        }
    }
    else
    {
        Console.WriteLine("Only TCP is implemented for now.");
    }

    return 0;
}