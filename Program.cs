using CommandLine;
using System;
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

        return await new Parser(with =>
        {
            with.HelpWriter = Console.Out;
        })
        .ParseArguments<Arguments>(args)
        .MapResult(
            async args =>
            {
                if (args.Protocol.ToLower() == "tcp")
                {
                    await Tcp.RunClientSession(args);
                }
                else
                {
                    Console.WriteLine("Only TCP is supported at the moment.");
                }
                return 0;
            },
            errs =>
            {
                Console.Error.WriteLine("Invalid arguments. Use -h for help.");
                return Task.FromResult(1);
            });
    }
} 
