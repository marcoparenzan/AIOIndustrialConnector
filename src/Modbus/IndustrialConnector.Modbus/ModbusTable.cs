// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

namespace AioIndustrialConnector.Modbus;

/// <summary>
/// The four Modbus data tables (object types).
/// </summary>
public enum ModbusTable
{
    /// <summary>Read/write single bits, function code 1 (read).</summary>
    Coil,

    /// <summary>Read-only single bits, function code 2.</summary>
    DiscreteInput,

    /// <summary>Read-only 16-bit registers, function code 4.</summary>
    InputRegister,

    /// <summary>Read/write 16-bit registers, function code 3 (read).</summary>
    HoldingRegister,
}

public static class ModbusTableExtensions
{
    /// <summary>True for the tables that hold 16-bit registers, false for the bit tables.</summary>
    public static bool IsRegisterTable(this ModbusTable table) =>
        table is ModbusTable.InputRegister or ModbusTable.HoldingRegister;
}
