// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using AioIndustrialConnector.Core;

namespace AioIndustrialConnector.Modbus;

/// <summary>
/// Reads Modbus tables from one server. Implementations are safe for concurrent use.
/// </summary>
public interface IModbusClient
{
    /// <summary>Reads holding or input registers (function code 3 or 4).</summary>
    /// <exception cref="ModbusDeviceException">The device answered with a Modbus exception response.</exception>
    /// <exception cref="ModbusCommunicationException">The connection failed or the device did not answer in time.</exception>
    Task<ushort[]> ReadRegistersAsync(byte unitId, ModbusTable table, ushort start, ushort count, CancellationToken cancellationToken);

    /// <summary>Reads coils or discrete inputs (function code 1 or 2).</summary>
    /// <exception cref="ModbusDeviceException">The device answered with a Modbus exception response.</exception>
    /// <exception cref="ModbusCommunicationException">The connection failed or the device did not answer in time.</exception>
    Task<bool[]> ReadBitsAsync(byte unitId, ModbusTable table, ushort start, ushort count, CancellationToken cancellationToken);
}

/// <summary>
/// The device answered with a Modbus exception response (illegal address, device busy, ...). The connection is still usable.
/// </summary>
public sealed class ModbusDeviceException : DeviceRejectedException
{
    public ModbusDeviceException(byte exceptionCode, string message, Exception? inner = null) : base(message, inner)
    {
        ExceptionCode = exceptionCode;
    }

    /// <summary>Modbus exception code (1 = illegal function, 2 = illegal data address, 3 = illegal data value, 4 = device failure, ...).</summary>
    public byte ExceptionCode { get; }
}

/// <summary>
/// The connection to the Modbus server failed, was lost, or a response timed out. The connection is reset.
/// </summary>
public sealed class ModbusCommunicationException : DeviceCommunicationException
{
    public ModbusCommunicationException(string message, Exception? inner = null) : base(message, inner) { }
}
