// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using AioIndustrialConnector.Core;
using AioIndustrialConnector.Core.Configuration;
using AioIndustrialConnector.Core.Connectivity;
using Microsoft.Extensions.Logging;

namespace AioIndustrialConnector.S7;

/// <summary>
/// One <see cref="IS7Client"/> (one ISO-on-TCP connection) per PLC, shared by all datasets and assets.
/// S7 CPUs have a small number of connection resources, so sharing matters even more than for Modbus.
/// </summary>
public class S7ClientPool : ClientPool<S7ClientPool.ClientKey, IS7Client>
{
    public const int DefaultTimeoutMs = 3000;
    public const int DefaultConnectTimeoutMs = 5000;
    public const S7CpuType DefaultCpuType = S7CpuType.S71500;

    private readonly ILoggerFactory _loggerFactory;

    public S7ClientPool(ILoggerFactory loggerFactory) => _loggerFactory = loggerFactory;

    /// <summary>Slot used when none is configured: 2 for S7-300, 1 for the others.</summary>
    public static short DefaultSlot(S7CpuType cpu) => cpu == S7CpuType.S7300 ? (short)2 : (short)1;

    /// <summary>Returns the shared client for this PLC and connection settings, creating it on first use.</summary>
    public virtual IS7Client GetClient(TcpEndpoint endpoint, S7Settings endpointSettings)
    {
        S7CpuType cpu = endpointSettings.CpuType ?? DefaultCpuType;
        short rack = endpointSettings.Rack ?? 0;
        short slot = endpointSettings.Slot ?? DefaultSlot(cpu);
        int timeoutMs = endpointSettings.TimeoutMs ?? DefaultTimeoutMs;
        int connectTimeoutMs = endpointSettings.ConnectTimeoutMs ?? DefaultConnectTimeoutMs;
        if (timeoutMs <= 0 || connectTimeoutMs <= 0)
        {
            throw new ConfigurationException($"timeoutMs and connectTimeoutMs must be positive (got {timeoutMs} and {connectTimeoutMs}).");
        }

        if (rack is < 0 or > 7 || slot is < 0 or > 31)
        {
            throw new ConfigurationException($"rack must be 0..7 and slot 0..31 (got rack {rack}, slot {slot}).");
        }

        return GetOrCreate(new ClientKey(endpoint, cpu, rack, slot, timeoutMs, connectTimeoutMs), k => new S7NetClient(
            k.Endpoint, k.Cpu, k.Rack, k.Slot,
            TimeSpan.FromMilliseconds(k.ConnectTimeoutMs),
            TimeSpan.FromMilliseconds(k.TimeoutMs),
            _loggerFactory.CreateLogger<S7NetClient>()));
    }

    public readonly record struct ClientKey(TcpEndpoint Endpoint, S7CpuType Cpu, short Rack, short Slot, int TimeoutMs, int ConnectTimeoutMs);
}
