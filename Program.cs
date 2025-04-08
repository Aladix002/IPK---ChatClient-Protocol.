using CommandLine;
using System;
using System.Net;
using System.Threading.Tasks;

namespace Chat25;

class Program
{
    static async Task<int> Main(string[] args)
    {
        if (args.Length == 1 && (args[0] == "-h" || args[0] == "--help"))
        {
            new Parser(with => with.HelpWriter = Console.Out)
                .ParseArguments<Arguments>(new[] { "--help" });
            return 0;
        }

        return await new Parser(with => with.HelpWriter = Console.Out)
            .ParseArguments<Arguments>(args)
            .MapResult(
                async parsedArgs =>
                {
                    switch (parsedArgs.Protocol.ToLower())
                    {
                        case "tcp":
                            await Tcp.RunClientSession(parsedArgs);
                            break;

                        case "udp":
                            try
                            {
                                var hostEntry = await Dns.GetHostEntryAsync(parsedArgs.Ip);
                                var ipv4 = Array.Find(hostEntry.AddressList, ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                                if (ipv4 == null)
                                {
                                    Console.Error.WriteLine("ERR: No IPv4 address found for the provided hostname.");
                                    return 1;
                                }

                                await Udp.RunClientSession(parsedArgs, ipv4);
                            }
                            catch (Exception ex)
                            {
                                Console.Error.WriteLine($"ERR: Failed to resolve host '{parsedArgs.Ip}': {ex.Message}");
                                return 1;
                            }
                            break;

                        default:
                            Console.Error.WriteLine("ERR: Unsupported protocol. Use 'tcp' or 'udp'.");
                            return 1;
                    }

                    return 0;
                },
                errs =>
                {
                    Console.Error.WriteLine("ERR: Invalid arguments. Use -h for help.");
                    return Task.FromResult(1);
                });
    }
}


