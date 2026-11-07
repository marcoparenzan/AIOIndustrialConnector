// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

namespace AioIndustrialConnector.S7.Simulator;

/// <summary>
/// A small simulated process cell. The memory map below is the one used by the sample assets in <c>deploy/s7/</c>
/// and documented in <c>docs/s7/LOCAL-SIMULATION.md</c>. It mirrors the Modbus simulator's process.
/// </summary>
/// <remarks>
/// <code>
/// DB1 (64 bytes)
///   DB1.DBD0    temperature     REAL                    20..30 °C, sine with 60 s period
///   DB1.DBW4    humidityTenths  INT (scale 0.1)         40.0..60.0 %
///   DB1.DBD6    cycleCounter    DWORD                   +1 per update
///   DB1.DBW10   pressureDelta   INT                     -500..500
///   DB1.DBX12.0 running         BOOL
///   DB1.DBX12.1 alarm           BOOL                    when temperature &gt; 29
///   DB1.DBB20   serialNumber    STRING[16]              "AIO-SIM-0001"
///   DB1.DBB40   totalEnergy     LREAL                   kWh, increases
/// Memory
///   MW0         tankLevelRaw    WORD ADC 0..4095
/// Inputs
///   I0.0        doorClosed      always true
///   I0.1        emergencyStop   always false
/// Outputs
///   Q0.0        pumpOn          toggles every 10 s
///   Q0.1        valveOpen       follows pumpOn
/// </code>
/// </remarks>
public static class SimulatedProcess
{
    public const string SerialNumber = "AIO-SIM-0001";

    /// <summary>Creates the memory the process writes into.</summary>
    public static S7Memory CreateMemory() => new S7Memory().CreateDataBlock(1, 64);

    public static void Update(S7Memory m, TimeSpan elapsed, uint cycle)
    {
        double t = elapsed.TotalSeconds;
        float temperature = (float)(25.0 + 5.0 * Math.Sin(2 * Math.PI * t / 60.0));
        short humidityTenths = (short)Math.Round(500 + 100 * Math.Sin(2 * Math.PI * t / 90.0));
        short pressureDelta = (short)Math.Round(500 * Math.Sin(2 * Math.PI * t / 30.0));
        bool pumpOn = ((int)(t / 10)) % 2 == 0;
        bool alarm = temperature > 29.0f;
        ushort tankRaw = (ushort)Math.Round(2047.5 + 2047.5 * Math.Sin(2 * Math.PI * t / 120.0));

        m.SetFloat(S7Memory.DataBlock, 1, 0, temperature);
        m.SetInt16(S7Memory.DataBlock, 1, 4, humidityTenths);
        m.SetUInt32(S7Memory.DataBlock, 1, 6, cycle);
        m.SetInt16(S7Memory.DataBlock, 1, 10, pressureDelta);
        m.SetBit(S7Memory.DataBlock, 1, 12, 0, pumpOn);
        m.SetBit(S7Memory.DataBlock, 1, 12, 1, alarm);
        m.SetString(S7Memory.DataBlock, 1, 20, 16, SerialNumber);
        m.SetDouble(S7Memory.DataBlock, 1, 40, 1000.0 + t * 0.01);

        m.SetUInt16(S7Memory.Memory, 0, 0, tankRaw);
        m.SetBit(S7Memory.Input, 0, 0, 0, true);
        m.SetBit(S7Memory.Input, 0, 0, 1, false);
        m.SetBit(S7Memory.Output, 0, 0, 0, pumpOn);
        m.SetBit(S7Memory.Output, 0, 0, 1, pumpOn);
    }
}
