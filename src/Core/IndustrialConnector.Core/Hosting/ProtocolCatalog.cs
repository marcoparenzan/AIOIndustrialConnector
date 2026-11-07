// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using Azure.Iot.Operations.Connector;

namespace AioIndustrialConnector.Core.Hosting;

/// <summary>
/// Registry of the available protocol modules and the rule that picks the one an instance runs.
/// An instance runs exactly one protocol: <c>--protocol &lt;name&gt;</c> wins over the <c>CONNECTOR_PROTOCOL</c> environment variable.
/// </summary>
public sealed class ProtocolCatalog
{
    public const string EnvironmentVariable = "CONNECTOR_PROTOCOL";
    public const string CommandLineOption = "--protocol";

    private readonly IReadOnlyList<IProtocolModule> _modules;

    public ProtocolCatalog(IEnumerable<IProtocolModule> modules) => _modules = modules.ToArray();

    public IReadOnlyList<IProtocolModule> Modules => _modules;

    /// <summary>Returns the module requested by the arguments or the environment.</summary>
    /// <exception cref="ConfigurationException">No protocol was requested, or the name is unknown.</exception>
    public IProtocolModule Select(IReadOnlyList<string> args, Func<string, string?> getEnvironmentVariable)
    {
        string? requested = null;
        for (int i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], CommandLineOption, StringComparison.OrdinalIgnoreCase))
            {
                requested = args[i + 1];
            }
        }

        requested ??= getEnvironmentVariable(EnvironmentVariable);
        string available = string.Join(", ", _modules.Select(m => m.Name));

        if (string.IsNullOrWhiteSpace(requested))
        {
            throw new ConfigurationException($"No protocol selected. Set {EnvironmentVariable} or pass {CommandLineOption} <name>. Available: {available}.");
        }

        string name = requested.Trim().ToLowerInvariant();
        return _modules.FirstOrDefault(m => m.Name == name || m.Aliases.Contains(name))
            ?? throw new ConfigurationException($"Unknown protocol '{requested}'. Available: {available}.");
    }
}

/// <summary>
/// Leader election settings: when the connector template runs more than one replica, only the leader samples,
/// so a device is never polled twice and never sees more connections than one replica opens.
/// </summary>
public sealed class LeaderElectionConfigurationProvider : IConnectorLeaderElectionConfigurationProvider
{
    private readonly string _lockName;

    public LeaderElectionConfigurationProvider(string lockName) => _lockName = lockName;

    public ConnectorLeaderElectionConfiguration GetLeaderElectionConfiguration() =>
        new(_lockName, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(9));
}
