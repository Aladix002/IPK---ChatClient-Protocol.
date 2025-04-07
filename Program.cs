using CommandLine;

return Parser.Default
    .ParseArguments<Arguments>(args)
    .MapResult(
        RunClient,
        errs => 1
    );

static int RunClient(Arguments opts)
{
    Console.WriteLine($"Protocol: {opts.Protocol}");
    Console.WriteLine($"Server: {opts.Ip}:{opts.Port}");
    Console.WriteLine($"UDP timeout: {opts.UdpTimeout}ms, Retries: {opts.MaxRetries}");


    return 0;
}

