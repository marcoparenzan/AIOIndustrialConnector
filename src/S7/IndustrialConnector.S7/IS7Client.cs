// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using AioIndustrialConnector.Core;

namespace AioIndustrialConnector.S7;

/// <summary>
/// Reads byte ranges from one S7 PLC. Implementations are safe for concurrent use.
/// </summary>
public interface IS7Client
{
    /// <summary>Reads <paramref name="count"/> bytes starting at <paramref name="start"/> in the given area (and data block).</summary>
    /// <exception cref="S7DeviceException">The PLC rejected the request (data block missing, address out of range, access protected).</exception>
    /// <exception cref="S7CommunicationException">The connection failed or the PLC did not answer in time.</exception>
    Task<byte[]> ReadBytesAsync(S7Area area, int dbNumber, int start, int count, CancellationToken cancellationToken);
}

/// <summary>The PLC answered, but with an error for this request. The connection is still usable.</summary>
public sealed class S7DeviceException : DeviceRejectedException
{
    public S7DeviceException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>The connection to the PLC failed, was lost, or a response timed out. The connection is reset.</summary>
public sealed class S7CommunicationException : DeviceCommunicationException
{
    public S7CommunicationException(string message, Exception? inner = null) : base(message, inner) { }
}
