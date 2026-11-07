// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using NModbus;

namespace AioIndustrialConnector.Modbus.Simulator;

/// <summary>
/// A small simulated process cell. The register map below is the one used by the sample assets in <c>deploy/</c>
/// and documented in <c>docs/SIMULATOR.md</c>. All addresses are zero-based protocol addresses.
/// </summary>
/// <remarks>
/// <code>
/// Holding registers (FC 3)
///   hr:0-1   temperature     Float32, big endian           20..30 °C, sine with 60 s period
///   hr:2     humidity        UInt16, scale 0.1             40.0..60.0 %
///   hr:3-4   cycleCounter    UInt32, big endian            +1 per update
///   hr:5     pressureDelta   Int16                         -500..500
///   hr:6     statusWord      bit 0 running, bit 1 alarm (alarm when temperature > 29)
///   hr:10-17 serialNumber    String, 8 registers           "AIO-SIM-0001"
///   hr:20-23 totalEnergy     Float64, big endian           kWh, increases
/// Input registers (FC 4)
///   ir:0-1   flowRate        Float32, word swapped (low word first)   0..12 m³/h
///   ir:2     tankLevelRaw    UInt16 ADC 0..4095 (use scale/offset)
///   ir:3     byteSwapped     UInt16 1234, byte swapped (reads as 0xD204 with default byte order)
/// Coils (FC 1)
///   coil:0   pumpOn          toggles every 10 s
///   coil:1   valveOpen       follows pumpOn
/// Discrete inputs (FC 2)
///   di:0     doorClosed      always true
///   di:1     emergencyStop   always false
/// </code>
/// </remarks>
public static class SimulatedProcess
{
    public const string SerialNumber = "AIO-SIM-0001";

    /// <summary>Writes the process state at <paramref name="elapsed"/> time since start into <paramref name="store"/>.</summary>
    public static void Update(ISlaveDataStore store, TimeSpan elapsed, uint cycle)
    {
        double t = elapsed.TotalSeconds;
        float temperature = (float)(25.0 + 5.0 * Math.Sin(2 * Math.PI * t / 60.0));
        ushort humidityTenths = (ushort)Math.Round(500 + 100 * Math.Sin(2 * Math.PI * t / 90.0));
        short pressureDelta = (short)Math.Round(500 * Math.Sin(2 * Math.PI * t / 30.0));
        bool pumpOn = ((int)(t / 10)) % 2 == 0;
        bool alarm = temperature > 29.0f;
        ushort status = (ushort)((pumpOn ? 1 : 0) | (alarm ? 2 : 0));
        float flow = pumpOn ? (float)(10.0 + 2.0 * Math.Sin(t)) : 0f;
        ushort tankRaw = (ushort)Math.Round(2047.5 + 2047.5 * Math.Sin(2 * Math.PI * t / 120.0));
        double energy = 1000.0 + t * 0.01;

        store.WriteHolding(0, RegisterWriter.Float32(temperature));
        store.WriteHolding(2, humidityTenths);
        store.WriteHolding(3, RegisterWriter.UInt32(cycle));
        store.WriteHolding(5, unchecked((ushort)pressureDelta));
        store.WriteHolding(6, status);
        store.WriteHolding(10, RegisterWriter.Ascii(SerialNumber, 8));
        store.WriteHolding(20, RegisterWriter.Float64(energy));

        store.WriteInput(0, RegisterWriter.Float32(flow, wordSwap: true));
        store.WriteInput(2, tankRaw);
        store.WriteInput(3, RegisterWriter.ByteSwap([1234]));

        store.WriteCoils(0, pumpOn, pumpOn);
        store.WriteDiscreteInputs(0, true, false);
    }
}
