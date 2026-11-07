// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using System.Globalization;
using System.Text.RegularExpressions;

namespace AioIndustrialConnector.S7;

/// <summary>The S7 memory areas the connector can read.</summary>
public enum S7Area
{
    /// <summary>Data blocks (DB), the usual place for process data.</summary>
    DataBlock,

    /// <summary>Memory / flag bytes (M).</summary>
    Memory,

    /// <summary>Process image of the inputs (I, German E).</summary>
    Input,

    /// <summary>Process image of the outputs (Q, German A).</summary>
    Output,
}

/// <summary>The access width written in an S7 address (X, B, W, D).</summary>
public enum S7Width
{
    /// <summary>A single bit (<c>DBX</c>, or <c>M0.1</c>).</summary>
    Bit,

    /// <summary>One byte (<c>DBB</c>, <c>MB</c>); also the "untyped byte offset" for 64-bit values and strings.</summary>
    Byte,

    /// <summary>Two bytes (<c>DBW</c>, <c>MW</c>).</summary>
    Word,

    /// <summary>Four bytes (<c>DBD</c>, <c>MD</c>).</summary>
    DWord,
}

/// <summary>
/// A zero-based byte address inside one S7 memory area, parsed from the <c>dataSource</c> of an asset data point.
/// </summary>
/// <remarks>
/// Siemens absolute notation (TIA Portal / STEP 7), case-insensitive:
/// <list type="bullet">
///   <item><c>DB1.DBX0.3</c> (bit 3 of byte 0), <c>DB1.DBB4</c> (byte), <c>DB1.DBW2</c> (word), <c>DB1.DBD8</c> (double word) in data block 1.</item>
///   <item><c>M0.1</c>, <c>MB0</c>, <c>MW0</c>, <c>MD0</c> – memory (flags).</item>
///   <item><c>I0.1</c>, <c>IB0</c>, <c>IW0</c>, <c>ID0</c> – inputs (German <c>E</c> is accepted).</item>
///   <item><c>Q0.1</c>, <c>QB0</c>, <c>QW0</c>, <c>QD0</c> – outputs (German <c>A</c> is accepted).</item>
/// </list>
/// The width letter only fixes how many bytes the address covers by default; the data point's <c>dataType</c> must fit it.
/// </remarks>
public readonly partial record struct S7Address(S7Area Area, int DbNumber, int ByteOffset, int BitOffset, S7Width Width)
{
    [GeneratedRegex(@"^DB(?<db>\d{1,5})\.DB(?<w>[XBWD])(?<byte>\d{1,5})(?:\.(?<bit>[0-7]))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DataBlockPattern();

    [GeneratedRegex(@"^(?<a>[MIQEA])(?<w>[BWD])?(?<byte>\d{1,5})(?:\.(?<bit>[0-7]))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AreaPattern();

    /// <summary>Number of bytes the width covers when read (a bit lives in one byte).</summary>
    public int WidthBytes => Width switch
    {
        S7Width.Bit or S7Width.Byte => 1,
        S7Width.Word => 2,
        _ => 4,
    };

    public override string ToString()
    {
        string area = Area switch
        {
            S7Area.DataBlock => $"DB{DbNumber}.DB",
            S7Area.Memory => "M",
            S7Area.Input => "I",
            _ => "Q",
        };

        string width = Width switch
        {
            S7Width.Bit => Area == S7Area.DataBlock ? "X" : "",
            S7Width.Byte => "B",
            S7Width.Word => "W",
            _ => "D",
        };

        return Width == S7Width.Bit ? $"{area}{width}{ByteOffset}.{BitOffset}" : $"{area}{width}{ByteOffset}";
    }

    public static S7Address Parse(string text)
    {
        if (TryParse(text, out var address, out var error))
        {
            return address;
        }

        throw new FormatException(error);
    }

    public static bool TryParse(string? text, out S7Address address, out string? error)
    {
        address = default;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "S7 address is empty.";
            return false;
        }

        string s = text.Trim();
        Match m = DataBlockPattern().Match(s);
        S7Area area;
        int db = 0;
        if (m.Success)
        {
            area = S7Area.DataBlock;
            db = int.Parse(m.Groups["db"].Value, CultureInfo.InvariantCulture);
            if (db is < 1 or > 65535)
            {
                error = $"Data block number must be 1..65535 in S7 address '{text}'.";
                return false;
            }
        }
        else if ((m = AreaPattern().Match(s)).Success)
        {
            area = char.ToUpperInvariant(m.Groups["a"].Value[0]) switch
            {
                'M' => S7Area.Memory,
                'I' or 'E' => S7Area.Input,
                _ => S7Area.Output,
            };
        }
        else
        {
            error = $"Unrecognized S7 address '{text}'. Use e.g. 'DB1.DBX0.0', 'DB1.DBW2', 'DB1.DBD4', 'M0.1', 'MW10', 'IB0' or 'QD4'.";
            return false;
        }

        S7Width width;
        string letter = m.Groups["w"].Success ? m.Groups["w"].Value.ToUpperInvariant() : "";
        bool hasBit = m.Groups["bit"].Success;
        if (area == S7Area.DataBlock)
        {
            width = letter switch { "X" => S7Width.Bit, "B" => S7Width.Byte, "W" => S7Width.Word, _ => S7Width.DWord };
        }
        else
        {
            // M0.1 has no width letter and means a bit; MB0 / MW0 / MD0 carry one.
            width = letter switch { "" => S7Width.Bit, "B" => S7Width.Byte, "W" => S7Width.Word, _ => S7Width.DWord };
        }

        if (width == S7Width.Bit && !hasBit)
        {
            error = $"S7 bit address '{text}' needs a bit number 0..7, e.g. 'DB1.DBX0.3'.";
            return false;
        }

        if (width != S7Width.Bit && hasBit)
        {
            error = $"S7 address '{text}' has a bit number but is not a bit address; use the X form (DBX) or the 'bit' setting.";
            return false;
        }

        int byteOffset = int.Parse(m.Groups["byte"].Value, CultureInfo.InvariantCulture);
        int bitOffset = hasBit ? int.Parse(m.Groups["bit"].Value, CultureInfo.InvariantCulture) : 0;
        address = new S7Address(area, db, byteOffset, bitOffset, width);
        return true;
    }
}
