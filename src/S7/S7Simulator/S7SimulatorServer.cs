// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace AioIndustrialConnector.S7.Simulator;

/// <summary>
/// An in-process S7 PLC simulator: ISO-on-TCP (TPKT + COTP), S7comm "setup communication" and "read variable".
/// Write is not implemented (the connector is read-only).
/// </summary>
/// <remarks>
/// Written from the S7comm wire format independently of the S7netplus client, so tests exercise a real protocol
/// round trip. Any rack/slot (TSAP) is accepted. Used by the unit tests (start on port 0 for a free port) and by the
/// simulator console host.
/// </remarks>
public sealed class S7SimulatorServer : IAsyncDisposable
{
    private const byte ReturnOk = 0xFF, ReturnObjectDoesNotExist = 0x0A, ReturnAddressOutOfRange = 0x05;

    private readonly TcpListener _listener;
    private readonly S7Memory _memory;
    private readonly object _sync = new();
    private readonly ushort _pduSize;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Task> _connections = new();
    private readonly Task _acceptTask;
    private int _disposed;

    private S7SimulatorServer(TcpListener listener, S7Memory memory, ushort pduSize)
    {
        _listener = listener;
        _memory = memory;
        _pduSize = pduSize;
        _acceptTask = Task.Run(AcceptLoopAsync);
    }

    /// <summary>The TCP port the server listens on (the actual one when started on port 0).</summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Number of client connections accepted so far (lets tests assert that a connection is shared).</summary>
    public int ConnectionsAccepted { get; private set; }

    /// <summary>Starts listening. <paramref name="pduSize"/> is the PDU length offered in the setup response (240 like an S7-300).</summary>
    public static S7SimulatorServer Start(IPAddress address, int port, S7Memory memory, ushort pduSize = 240)
    {
        var listener = new TcpListener(address, port);
        listener.Start();
        return new S7SimulatorServer(listener, memory, pduSize);
    }

    /// <summary>Changes the PLC memory while no client request is being answered.</summary>
    public void Update(Action<S7Memory> change)
    {
        lock (_sync)
        {
            change(_memory);
        }
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
                client.NoDelay = true;
                lock (_connections)
                {
                    ConnectionsAccepted++;
                    _connections.Add(Task.Run(() => ServeAsync(client)));
                }
            }
        }
        catch (Exception)
        {
            // Listener stopped.
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            NetworkStream stream = client.GetStream();
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    byte[] header = new byte[4];
                    await stream.ReadExactlyAsync(header, _stop.Token).ConfigureAwait(false);
                    int total = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2));
                    byte[] payload = new byte[total - 4];
                    await stream.ReadExactlyAsync(payload, _stop.Token).ConfigureAwait(false);

                    byte[]? response = Handle(payload);
                    if (response is not null)
                    {
                        await stream.WriteAsync(response, _stop.Token).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception)
            {
                // Client closed the connection or the server is stopping.
            }
        }
    }

    private byte[]? Handle(byte[] cotp)
    {
        if (cotp.Length < 2)
        {
            return null;
        }

        // COTP connection request -> connection confirm (same references swapped, TPDU size 1024, any TSAP).
        if (cotp[1] == 0xE0)
        {
            byte[] cc = [0x11, 0xD0, cotp[4], cotp[5], 0x00, 0x01, 0x00, 0xC0, 0x01, 0x0A, 0xC1, 0x02, 0x01, 0x00, 0xC2, 0x02, 0x01, 0x02];
            return Tpkt(cc);
        }

        // COTP data: 02 F0 80, then S7comm.
        if (cotp[1] != 0xF0 || cotp.Length < 3 + 10 || cotp[3] != 0x32)
        {
            return null;
        }

        ReadOnlySpan<byte> s7 = cotp.AsSpan(3);
        ushort pduRef = BinaryPrimitives.ReadUInt16BigEndian(s7[4..]);
        int paramLength = BinaryPrimitives.ReadUInt16BigEndian(s7[6..]);
        ReadOnlySpan<byte> param = s7.Slice(10, paramLength);

        return param[0] switch
        {
            0xF0 => AckData(pduRef, [0xF0, 0x00, 0x00, 0x01, 0x00, 0x01, (byte)(_pduSize >> 8), (byte)_pduSize], []),
            0x04 => ReadVariable(pduRef, param),
            _ => AckData(pduRef, [param[0]], [], errorClass: 0x81, errorCode: 0x04), // function not supported
        };
    }

    private byte[] ReadVariable(ushort pduRef, ReadOnlySpan<byte> param)
    {
        int itemCount = param[1];
        using var data = new MemoryStream();
        lock (_sync)
        {
            for (int i = 0; i < itemCount; i++)
            {
                ReadOnlySpan<byte> item = param.Slice(2 + i * 12, 12);
                int count = BinaryPrimitives.ReadUInt16BigEndian(item[4..]);
                int db = BinaryPrimitives.ReadUInt16BigEndian(item[6..]);
                byte area = item[8];
                int bitAddress = (item[9] << 16) | (item[10] << 8) | item[11];
                int offset = bitAddress >> 3;

                byte[]? memory = _memory.Find(area, db);
                if (memory is null)
                {
                    data.Write([ReturnObjectDoesNotExist, 0x00, 0x00, 0x00]);
                }
                else if (offset + count > memory.Length)
                {
                    data.Write([ReturnAddressOutOfRange, 0x00, 0x00, 0x00]);
                }
                else
                {
                    data.Write([ReturnOk, 0x04, (byte)((count * 8) >> 8), (byte)(count * 8)]);
                    data.Write(memory, offset, count);
                    if (count % 2 == 1 && i < itemCount - 1)
                    {
                        data.WriteByte(0); // items are padded to an even length, except the last one
                    }
                }
            }
        }

        return AckData(pduRef, [0x04, (byte)itemCount], data.ToArray());
    }

    private static byte[] AckData(ushort pduRef, byte[] param, byte[] data, byte errorClass = 0, byte errorCode = 0)
    {
        var s7 = new byte[12 + param.Length + data.Length];
        s7[0] = 0x32;
        s7[1] = 0x03; // ack data
        BinaryPrimitives.WriteUInt16BigEndian(s7.AsSpan(4), pduRef);
        BinaryPrimitives.WriteUInt16BigEndian(s7.AsSpan(6), (ushort)param.Length);
        BinaryPrimitives.WriteUInt16BigEndian(s7.AsSpan(8), (ushort)data.Length);
        s7[10] = errorClass;
        s7[11] = errorCode;
        param.CopyTo(s7, 12);
        data.CopyTo(s7, 12 + param.Length);

        byte[] cotp = [0x02, 0xF0, 0x80];
        return Tpkt([.. cotp, .. s7]);
    }

    private static byte[] Tpkt(byte[] payload)
    {
        var frame = new byte[4 + payload.Length];
        frame[0] = 0x03;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(2), (ushort)frame.Length);
        payload.CopyTo(frame, 4);
        return frame;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        await _stop.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        Task[] connections;
        lock (_connections)
        {
            connections = [.. _connections];
        }

        try
        {
            await Task.WhenAll([_acceptTask, .. connections]).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Connections end with exceptions when cancelled; nothing to report.
        }

        _stop.Dispose();
    }
}
