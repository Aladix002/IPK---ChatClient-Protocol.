using CommandLine;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Transport;

class Program
{
    static async Task<int> Main(string[] args)
    {
        IChatClient? client = null;

        // Ctrl+C shutdown hook
        Console.CancelKeyPress += async (sender, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("\nShutting down...");
            if (client != null)
                await client.DisconnectAsync();
            Environment.Exit(0);
        };

        // Show help if only -h or --help is passed
        if (args.Length == 1 && (args[0] == "-h" || args[0] == "--help"))
        {
            new Parser(with => with.HelpWriter = Console.Out)
                .ParseArguments<Arguments>(new[] { "--help" });
            return 0;
        }

        // Parse and run client based on protocol
        return await new Parser(with => with.HelpWriter = Console.Out)
            .ParseArguments<Arguments>(args)
            .MapResult(
                async parsedArgs =>
                {
                    try
                    {
                        switch (parsedArgs.Protocol.ToLower())
                        {
                            case "tcp":
                                client = new Tcp(parsedArgs);
                                break;

                            case "udp":
                                var hostEntry = await Dns.GetHostEntryAsync(parsedArgs.Ip);
                                var ipv4 = Array.Find(
                                    hostEntry.AddressList,
                                    ip => ip.AddressFamily == AddressFamily.InterNetwork);

                                if (ipv4 == null)
                                {
                                    Console.Error.WriteLine("ERR: Could not resolve IPv4 address.");
                                    return 1;
                                }

                                client = new Udp(parsedArgs, ipv4);
                                break;

                            default:
                                Console.Error.WriteLine("ERR: Unsupported protocol. Use 'tcp' or 'udp'.");
                                return 1;
                        }

                        await client.RunAsync();
                        return 0;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"ERR: {ex.Message}");
                        return 1;
                    }
                },
                errs => Task.FromResult(1));
    }
}







