using CommandLine;

/*
 * Minimal argument definition
 * Adapted from https://github.com/commandlineparser/commandline
 * Record holds necessary CLI parameters for the chat client and their default values
 */
public record Arguments
{
    [Option('t', Required = true, HelpText = "Transport protocol (tcp or udp)")]
    public string Protocol { get; init; } = "";

    [Option('s', Required = true, HelpText = "Server IP or hostname")]
    public string Ip { get; init; } = "";

    [Option('p', Default = 4567, HelpText = "Server port")]
    public int Port { get; init; }

    [Option('d', Default = 250, HelpText = "UDP confirm timeout (ms)")]
    public int UdpTimeout { get; init; }

    [Option('r', Default = 3, HelpText = "UDP max retries")]
    public int MaxRetries { get; init; }
}
