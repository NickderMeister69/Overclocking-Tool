using System.Diagnostics;
using System.Runtime.InteropServices;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Tweaker;

/// <summary>CPU-Last für den Stabilitätstest (alle Threads, mit Selbstkontrolle der Rechenergebnisse).</summary>
public sealed class CpuLoad
{
    volatile bool _run;
    readonly List<Thread> _threads = new();
    int _errors;
    public bool Running => _run;
    public int Errors => Volatile.Read(ref _errors);

    static double Chunk(int seed)
    {
        double x = 1.0001 + seed * 0.01, y = 0.9999, z = 1.5;
        for (int i = 0; i < 1_000_000; i++)
        {
            x = Math.Sqrt(x * y + 1.25);
            y = Math.Sin(x) + 2.0;
            z = (z * x + y) % 97.0 + 1.0;
        }
        return x + y + z;
    }

    public void Start()
    {
        Stop();
        _run = true; _errors = 0;
        for (int t = 0; t < Environment.ProcessorCount; t++)
        {
            int seed = t;
            var th = new Thread(() =>
            {
                double reference = Chunk(seed);
                while (_run)
                    if (BitConverter.DoubleToInt64Bits(Chunk(seed)) != BitConverter.DoubleToInt64Bits(reference))
                        Interlocked.Increment(ref _errors);
            }) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "CPU-Test " + t };
            _threads.Add(th);
            th.Start();
        }
    }

    public void Stop()
    {
        _run = false;
        foreach (var t in _threads) t.Join(2000);
        _threads.Clear();
    }
}

/// <summary>
/// GPU-Last per Direct3D-11-Compute-Shader. Prüft regelmäßig, ob die GPU bei gleicher Rechnung
/// immer exakt das gleiche Ergebnis liefert – Abweichungen = Übertaktung instabil.
/// </summary>
public sealed class GpuLoad
{
    const int WG = 256, GROUPS = 8192, N = WG * GROUPS;
    const int VGROUPS = 256, VN = WG * VGROUPS, VITERS = 96;
    const int PER_BATCH = 4;
    const double TARGET_MS = 30; // kurze Dispatches -> kein Treiber-Timeout

    volatile bool _run;
    Thread _thread;
    int _errors, _checks;
    public bool Running => _run;
    public string Error { get; private set; }
    public bool DriverCrashed { get; private set; }
    public int Errors => Volatile.Read(ref _errors);
    public int Checks => Volatile.Read(ref _checks);

    const string HLSL = @"
cbuffer P : register(b0) { uint iters; uint3 pad; };
RWStructuredBuffer<float4> buf : register(u0);
[numthreads(256,1,1)]
void main(uint3 id : SV_DispatchThreadID)
{
    float f = id.x * 0.000001;
    float4 a = float4(f, f+0.1, f+0.2, f+0.3);
    float4 b = float4(0.5, 0.25, 0.75, 0.125);
    float4 c = float4(0.3, 0.6, 0.9, 0.1);
    float4 d = float4(0.7, 0.2, 0.4, 0.8);
    [loop] for (uint i = 0; i < iters; i++)
    {
        a = frac(mad(a, b, c));
        b = frac(mad(b, c, d));
        c = frac(mad(c, d, a));
        d = frac(mad(d, a, b));
    }
    buf[id.x] = a + b + c + d;
}";

    public void Start()
    {
        Stop();
        Error = null; DriverCrashed = false; _errors = 0; _checks = 0;
        _run = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "GPU-Test" };
        _thread.Start();
    }

    public void Stop()
    {
        _run = false;
        _thread?.Join(4000);
        _thread = null;
    }

    static IDXGIAdapter1 PickAdapter()
    {
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        IDXGIAdapter1 best = null; ulong bestMem = 0;
        for (uint i = 0; factory.EnumAdapters1(i, out IDXGIAdapter1 a).Success; i++)
        {
            var d = a.Description1;
            if ((d.Flags & AdapterFlags.Software) == 0 && (ulong)d.DedicatedVideoMemory > bestMem)
            { best?.Dispose(); best = a; bestMem = (ulong)d.DedicatedVideoMemory; }
            else a.Dispose();
        }
        return best;
    }

    void Loop()
    {
        ID3D11Device device = null; ID3D11DeviceContext ctx = null;
        try
        {
            using var adapter = PickAdapter();
            D3D11.D3D11CreateDevice(adapter, adapter == null ? DriverType.Hardware : DriverType.Unknown,
                DeviceCreationFlags.None, new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 }, out device, out ctx).CheckError();

            var bytecode = Compiler.Compile(HLSL, "main", "test.hlsl", "cs_5_0", ShaderFlags.OptimizationLevel3);
            using var cs = device.CreateComputeShader(bytecode.Span);

            using var buf = device.CreateBuffer(new BufferDescription((uint)(N * 16), BindFlags.UnorderedAccess,
                ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 16));
            using var uav = device.CreateUnorderedAccessView(buf, new UnorderedAccessViewDescription(buf, Format.Unknown, 0, (uint)N));
            using var vbuf = device.CreateBuffer(new BufferDescription((uint)(VN * 16), BindFlags.UnorderedAccess,
                ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 16));
            using var vuav = device.CreateUnorderedAccessView(vbuf, new UnorderedAccessViewDescription(vbuf, Format.Unknown, 0, (uint)VN));
            using var staging = device.CreateBuffer(new BufferDescription((uint)(VN * 16), BindFlags.None,
                ResourceUsage.Staging, CpuAccessFlags.Read, ResourceOptionFlags.None, 0));
            using var cb = device.CreateBuffer(new BufferDescription(16, BindFlags.ConstantBuffer));
            using var vcb = device.CreateBuffer(new BufferDescription(16, BindFlags.ConstantBuffer));
            ctx.UpdateSubresource(new uint[] { VITERS, 0, 0, 0 }, vcb);

            var queries = new[] { device.CreateQuery(new QueryDescription(QueryType.Event)), device.CreateQuery(new QueryDescription(QueryType.Event)) };
            bool[] inFlight = new bool[2];
            ctx.CSSetShader(cs);

            byte[] reference = null, current = new byte[VN * 16];
            uint iters = 32;
            var sw = Stopwatch.StartNew();
            double tPrev = 0, lastCheck = 0;
            int slot = 0;

            byte[] Verify()
            {
                ctx.CSSetConstantBuffer(0, vcb);
                ctx.CSSetUnorderedAccessView(0, vuav);
                ctx.Dispatch(VGROUPS, 1, 1);
                ctx.CopyResource(staging, vbuf);
                var map = ctx.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                var arr = new byte[VN * 16];
                Marshal.Copy(map.DataPointer, arr, 0, arr.Length);
                ctx.Unmap(staging, 0);
                return arr;
            }

            reference = Verify();
            if (!Verify().AsSpan().SequenceEqual(reference)) Interlocked.Increment(ref _errors);

            while (_run)
            {
                ctx.CSSetConstantBuffer(0, cb);
                ctx.CSSetUnorderedAccessView(0, uav);
                ctx.UpdateSubresource(new uint[] { iters, 0, 0, 0 }, cb);
                for (int k = 0; k < PER_BATCH; k++) ctx.Dispatch(GROUPS, 1, 1);
                ctx.End(queries[slot]);
                inFlight[slot] = true;
                ctx.Flush();

                int prev = slot ^ 1;
                if (inFlight[prev])
                {
                    var wait = Stopwatch.StartNew();
                    while (!ctx.IsDataAvailable(queries[prev]) && _run)
                    {
                        Thread.Sleep(0);
                        if (wait.ElapsedMilliseconds > 5000) throw new TimeoutException("GPU antwortet nicht mehr");
                    }
                    inFlight[prev] = false;
                    double now = sw.Elapsed.TotalMilliseconds;
                    double per = (now - tPrev) / PER_BATCH;
                    tPrev = now;
                    if (per < TARGET_MS * 0.7) iters = Math.Min(iters * 3 / 2 + 1, 1u << 16);
                    else if (per > TARGET_MS * 1.4) iters = Math.Max(iters * 7 / 10, 8u);

                    if (now - lastCheck > 1000)
                    {
                        lastCheck = now;
                        if (!Verify().AsSpan().SequenceEqual(reference)) Interlocked.Increment(ref _errors);
                        Interlocked.Increment(ref _checks);
                    }
                }
                else tPrev = sw.Elapsed.TotalMilliseconds;
                slot = prev;
            }
            foreach (var q in queries) q.Dispose();
            ctx.ClearState();
        }
        catch (Exception ex)
        {
            try { DriverCrashed = device != null && device.DeviceRemovedReason.Failure; } catch { DriverCrashed = true; }
            if (ex is TimeoutException) DriverCrashed = true;
            Error = DriverCrashed ? "Der Grafiktreiber ist abgestürzt bzw. hat sich zurückgesetzt." : ex.Message;
            _run = false;
        }
        finally
        {
            ctx?.Dispose();
            device?.Dispose();
        }
    }
}
