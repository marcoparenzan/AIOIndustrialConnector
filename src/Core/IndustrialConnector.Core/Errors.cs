// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

namespace AioIndustrialConnector.Core;

/// <summary>The device, asset, dataset or data point configuration cannot be used by the connector.</summary>
public sealed class ConfigurationException : Exception
{
    public ConfigurationException(string message) : base(message) { }

    public ConfigurationException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// The device answered, but rejected one read request (Modbus exception response, S7 "invalid address", ...).
/// The connection is still usable; the points of that request are published as <c>null</c>.
/// </summary>
public class DeviceRejectedException : Exception
{
    public DeviceRejectedException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// The connection failed, was lost, or a response timed out. The connection has been reset;
/// the sampler retries once on a fresh connection before reporting the error.
/// </summary>
public class DeviceCommunicationException : Exception
{
    public DeviceCommunicationException(string message, Exception? inner = null) : base(message, inner) { }
}
