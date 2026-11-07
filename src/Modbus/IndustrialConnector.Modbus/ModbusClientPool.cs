// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using AioIndustrialConnector.Core;
using AioIndustrialConnector.Core.Configuration;
using AioIndustrialConnector.Core.Connectivity;
using Microsoft.Extensions.Logging;

namespace AioIndustrialConnector.Modbus;

/// <summary>
/// One <see cref="IModbusClient"/> (one TCP connection) per Modbus server, shared by all datasets and assets.
/// </summary>
public class ModbusClientPool : ClientPool<ModbusClientPool.ClientKey, IModbusClient>
{
    public const int DefaultTimeoutMs = 3000;
    public const int DefaultConnectTimeoutMs = 5000;

    private readonly ILoggerFactory _loggerFactory;

    public ModbusClientPool(ILoggerFactory loggerFactory) => _loggerFactory = loggerFactory;

    /// <summary>Returns the shared client for this endpoint and timeout combination, creating it on first use.</summary>
    public virtual IModbusClient GetClient(TcpEndpoint endpoint, ModbusSettings endpointSettings)
    {
        int timeoutMs = endpointSettings.TimeoutMs ?? DefaultTimeoutMs;
        int connectTimeoutMs = endpointSettings.ConnectTimeoutMs ?? DefaultConnectTimeoutMs;
        if (timeoutMs <= 0 || connectTimeoutMs <= 0)
        {
            throw new ConfigurationException($"timeoutMs and connectTimeoutMs must be positive (got {timeoutMs} and {connectTimeoutMs}).");
        }

        return GetOrCreate(new ClientKey(endpoint, timeoutMs, connectTimeoutMs), k => new NModbusTcpClient(
            k.Endpoint,
            TimeSpan.FromMilliseconds(k.ConnectTimeoutMs),
            TimeSpan.FromMilliseconds(k.TimeoutMs),
            _loggerFactory.CreateLogger<NModbusTcpClient>()));
    }

    public readonly record struct ClientKey(TcpEndpoint Endpoint, int TimeoutMs, int ConnectTimeoutMs);
}
