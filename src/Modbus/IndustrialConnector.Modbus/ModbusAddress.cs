// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using System.Globalization;

namespace AioIndustrialConnector.Modbus;

/// <summary>
/// A zero-based protocol address inside one Modbus table.
/// </summary>
/// <remarks>
/// Parsed from the <c>dataSource</c> of an asset data point. Accepted forms:
/// <list type="bullet">
///   <item><c>hr:0</c>, <c>holding:0</c>, <c>4x:0</c> – holding register, zero-based protocol address.</item>
///   <item><c>ir:0</c>, <c>input:0</c>, <c>3x:0</c> – input register.</item>
///   <item><c>coil:0</c>, <c>co:0</c>, <c>0x:0</c> – coil.</item>
///   <item><c>di:0</c>, <c>discrete:0</c>, <c>1x:0</c> – discrete input.</item>
///   <item>Modicon notation, one-based: <c>40001</c> or <c>400001</c> (holding 0), <c>30001</c> / <c>300001</c> (input 0),
///         <c>10001</c> / <c>100001</c> (discrete input 0), <c>00001</c> / <c>000001</c> (coil 0).</item>
/// </list>
/// Prefixed forms accept decimal or hexadecimal (<c>0x</c> prefix) offsets, e.g. <c>hr:0x10</c>.
/// </remarks>
public readonly record struct ModbusAddress(ModbusTable Table, ushort Offset)
{
    public override string ToString() => $"{Prefix(Table)}:{Offset}";

    private static string Prefix(ModbusTable table) => table switch
    {
        ModbusTable.Coil => "coil",
        ModbusTable.DiscreteInput => "di",
        ModbusTable.InputRegister => "ir",
        ModbusTable.HoldingRegister => "hr",
        _ => throw new ArgumentOutOfRangeException(nameof(table)),
    };

    public static ModbusAddress Parse(string text)
    {
        if (TryParse(text, out var address, out var error))
        {
            return address;
        }

        throw new FormatException(error);
    }

    public static bool TryParse(string? text, out ModbusAddress address, out string? error)
    {
        address = default;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Modbus address is empty.";
            return false;
        }

        string s = text.Trim();
        int colon = s.IndexOf(':');
        if (colon >= 0)
        {
            string prefix = s[..colon].Trim().ToLowerInvariant();
            string number = s[(colon + 1)..].Trim();

            ModbusTable? table = prefix switch
            {
                "hr" or "holding" or "holdingregister" or "4x" => ModbusTable.HoldingRegister,
                "ir" or "input" or "inputregister" or "3x" => ModbusTable.InputRegister,
                "coil" or "co" or "0x" => ModbusTable.Coil,
                "di" or "discrete" or "discreteinput" or "1x" => ModbusTable.DiscreteInput,
                _ => null,
            };

            if (table is null)
            {
                error = $"Unknown Modbus table prefix '{prefix}' in address '{text}'. Use hr, ir, coil or di.";
                return false;
            }

            if (!TryParseOffset(number, out ushort offset))
            {
                error = $"Invalid offset '{number}' in Modbus address '{text}'. Expected 0..65535.";
                return false;
            }

            address = new ModbusAddress(table.Value, offset);
            return true;
        }

        // Modicon notation: first digit selects the table, the rest is a one-based register number.
        if (s.Length is 5 or 6 && s.All(char.IsAsciiDigit))
        {
            ModbusTable? table = s[0] switch
            {
                '0' => ModbusTable.Coil,
                '1' => ModbusTable.DiscreteInput,
                '3' => ModbusTable.InputRegister,
                '4' => ModbusTable.HoldingRegister,
                _ => null,
            };

            int oneBased = int.Parse(s[1..], CultureInfo.InvariantCulture);
            int max = s.Length == 5 ? 9999 : 65536;
            if (table is null || oneBased < 1 || oneBased > max)
            {
                error = $"Invalid Modicon address '{text}'.";
                return false;
            }

            address = new ModbusAddress(table.Value, (ushort)(oneBased - 1));
            return true;
        }

        error = $"Unrecognized Modbus address '{text}'. Use e.g. 'hr:0', 'ir:10', 'coil:3', 'di:7' or Modicon '40001'.";
        return false;
    }

    private static bool TryParseOffset(string number, out ushort offset)
    {
        if (number.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return ushort.TryParse(number[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out offset);
        }

        return ushort.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out offset);
    }
}
