using System.Diagnostics;
using System.Management;
using System.Text.RegularExpressions;
using LibreHardwareMonitor.Hardware;

namespace Tweaker;

public sealed class CpuSettings
{
    public int Boost { get; set; } = 1;        // Windows PERFBOOSTMODE (0 aus, 1 normal, 2 aggressiv, 3 effizient)
    public int MaxFreq { get; set; }           // MHz, 0 = kein Limit
    public int MinState { get; set; } = 5;     // % minimaler Prozessorzustand
    public bool UsePlan { get; set; }          // false = normaler Windows-Energieplan
    public CpuSettings Clone() => (CpuSettings)MemberwiseClone();
    public static string BoostName(int b) => b switch { 0 => "Turbo aus", 2 => "Aggressiv", 3 => "Effizient", _ => "Normal" };
    public override string ToString() => !UsePlan ? "Windows-Standard" :
        $"Turbo {BoostName(Boost)}" + (MaxFreq > 0 ? $", max. {MaxFreq} MHz" : "") + $", min. {MinState} %";
}

/// <summary>
/// CPU: Erkennung + sichere Anpassung über einen eigenen Windows-Energieplan.
/// Keine Spannungs-/Multiplikator-Änderung per Treiber – das bleibt dem BIOS vorbehalten.
/// </summary>
public sealed class CpuTuner : IDisposable
{
    const string SubProc = "54533251-82be-4824-96c1-47b60b740d00";
    const string BoostGuid = "be337238-0d82-4146-a960-4f3749d470c7";
    const string MaxStateGuid = "bc5038f7-23e0-4960-96da-33abaf5935ec";
    const string MinStateGuid = "893dee8e-2bef-41e0-89c6-b55d0929964c";
    const string FreqMax0 = "75b0ae3f-bce0-45a7-8c89-c9611c25e100";
    const string FreqMax1 = "75b0ae3f-bce0-45a7-8c89-c9611c25e101";
    public const string PlanName = "Nick Tweaker";

    public string Name { get; private set; } = "Unbekannte CPU";
    public int Cores { get; private set; }
    public int Threads { get; private set; }
    public int BaseClock { get; private set; }
    public int MaxSeen { get; private set; }
    public bool IsIntel { get; private set; }
    public bool IsAmd { get; private set; }
    public bool Hybrid { get; private set; }
    public bool Unlocked { get; private set; }
    public string OcInfo { get; private set; } = "";
    public int TjMax { get; private set; } = 100;
    public int SafetyTemp => TjMax - 5;

    Computer _pc;
    IHardware _cpu;
    public bool SensorsOk { get; private set; }
    public float Temp = -1, Clock = -1, PowerW = -1, Load = -1;

    public CpuTuner()
    {
        try
        {
            using var s = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor");
            foreach (ManagementObject o in s.Get())
            {
                Name = Regex.Replace(o["Name"]?.ToString() ?? Name, @"\s+", " ").Trim();
                Cores = Convert.ToInt32(o["NumberOfCores"]); Threads = Convert.ToInt32(o["NumberOfLogicalProcessors"]);
                BaseClock = Convert.ToInt32(o["MaxClockSpeed"]);
                break;
            }
        }
        catch { Threads = Environment.ProcessorCount; }
        if (Threads == 0) Threads = Environment.ProcessorCount;
        IsIntel = Name.Contains("Intel", StringComparison.OrdinalIgnoreCase);
        IsAmd = Name.Contains("AMD", StringComparison.OrdinalIgnoreCase);
        if (IsAmd) TjMax = 95;
        Classify();

        try
        {
            _pc = new Computer { IsCpuEnabled = true };
            _pc.Open();
            _cpu = _pc.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
            if (_cpu != null)
            {
                _cpu.Update();
                SensorsOk = true;
                Hybrid = _cpu.Sensors.Any(x => x.Name.StartsWith("E-Core", StringComparison.OrdinalIgnoreCase));
                var t = _cpu.Sensors.FirstOrDefault(x => x.SensorType == SensorType.Temperature && x.Name.StartsWith("P-Core #1") && !x.Name.Contains("Distance"))
                        ?? _cpu.Sensors.FirstOrDefault(x => x.SensorType == SensorType.Temperature && x.Name == "Core #1");
                var dist = _cpu.Sensors.FirstOrDefault(x => x.SensorType == SensorType.Temperature && x.Name.Contains("Distance to TjMax"));
                if (t?.Value is float tv && dist?.Value is float dv && tv + dv is > 80 and < 115) TjMax = (int)Math.Round(tv + dv);
            }
        }
        catch { SensorsOk = false; }
    }

    void Classify()
    {
        var n = Name.ToUpperInvariant();
        if (IsIntel)
        {
            var m = Regex.Match(n, @"I[3579]-(\d{4,5})([A-Z]*)");
            string suffix = m.Success ? m.Groups[2].Value : "";
            string model = m.Success ? m.Groups[1].Value : "";
            Unlocked = suffix.Contains('K') || suffix.Contains('X') || (n.Contains("ULTRA") && Regex.IsMatch(n, @"\d{3}K\b"));
            bool raptor = model.Length == 5 && (model.StartsWith("13") || model.StartsWith("14"));
            OcInfo = Unlocked
                ? "Freier Multiplikator (K-Modell): echtes Übertakten geht im BIOS oder mit Intel XTU. Hier: sichere Feinsteuerung über Windows."
                : "Gesperrtes Modell (kein „K“): Takt und Spannung lassen sich nicht erhöhen – das ist keine Einschränkung dieser App, sondern der CPU. Hier kannst du Turbo-Verhalten und Maximaltakt einstellen; ein niedrigerer Maximaltakt senkt automatisch die Spannung (Undervolt-Effekt).";
            if (raptor)
                OcInfo += " Tipp für 13./14. Gen: BIOS aktuell halten (Intel-Microcode 0x12B oder neuer) und Spannung nie erhöhen.";
        }
        else if (IsAmd)
        {
            Unlocked = n.Contains("RYZEN");
            OcInfo = "AMD Ryzen: echtes Undervolting geht über „Curve Optimizer“ (BIOS oder AMD Ryzen Master). Hier: sichere Feinsteuerung von Turbo und Maximaltakt über Windows – weniger Takt = weniger Spannung.";
        }
        else OcInfo = "Feinsteuerung von Turbo und Maximaltakt über Windows.";
    }

    public void Poll()
    {
        if (!SensorsOk) return;
        try
        {
            _cpu.Update();
            var temps = _cpu.Sensors.Where(x => x.SensorType == SensorType.Temperature && x.Value.HasValue && !x.Name.Contains("Distance")).ToList();
            var p = temps.Where(x => x.Name.StartsWith("P-Core #")).Select(x => x.Value.Value).ToList();
            if (p.Count == 0) p = temps.Where(x => x.Name.StartsWith("Core #") || x.Name.StartsWith("Core (Tctl")).Select(x => x.Value.Value).ToList();
            if (p.Count == 0) p = temps.Where(x => x.Name.Contains("Package") || x.Name.Contains("Average")).Select(x => x.Value.Value).ToList();
            Temp = p.Count > 0 ? p.Average() : -1;

            var clocks = _cpu.Sensors.Where(x => x.SensorType == SensorType.Clock && x.Value.HasValue && (x.Name.StartsWith("P-Core") || x.Name.StartsWith("Core #"))).Select(x => x.Value.Value).ToList();
            Clock = clocks.Count > 0 ? clocks.Max() : -1;
            if (Clock > MaxSeen && Clock < 7000) MaxSeen = (int)Clock;

            PowerW = _cpu.Sensors.FirstOrDefault(x => x.SensorType == SensorType.Power && x.Name.Contains("Package"))?.Value ?? -1;
            Load = _cpu.Sensors.FirstOrDefault(x => x.SensorType == SensorType.Load && x.Name == "CPU Total")?.Value ?? -1;
        }
        catch { }
    }

    /// <summary>Kurz eine Kernlast erzeugen, um den echten Turbo-Takt zu messen.</summary>
    public void ProbeMaxClock()
    {
        if (!SensorsOk) return;
        var stop = false;
        var th = new Thread(() => { double x = 1; while (!Volatile.Read(ref stop)) x = Math.Sqrt(x + 1.1); }) { IsBackground = true };
        th.Start();
        for (int i = 0; i < 8; i++) { Thread.Sleep(200); Poll(); }
        Volatile.Write(ref stop, true);
        th.Join(500);
    }

    public int TurboClock => Math.Max(MaxSeen, BaseClock);
    public int CapMin => Math.Max(800, BaseClock * 6 / 10 / 100 * 100);
    public int CapMax => Math.Max(CapMin + 100, (TurboClock + 99) / 100 * 100);

    // ---------- Vorschläge ----------
    public CpuSettings PresetDefault() => new() { UsePlan = false };
    public CpuSettings PresetEfficient()
    {
        int cap = Math.Max(BaseClock, (int)(TurboClock * 0.85)) / 100 * 100;
        return new() { UsePlan = true, Boost = 3, MaxFreq = cap >= CapMax ? 0 : cap, MinState = 5 };
    }
    public CpuSettings PresetBalanced() => new() { UsePlan = true, Boost = 1, MaxFreq = 0, MinState = 5 };
    public CpuSettings PresetPerformance() => new() { UsePlan = true, Boost = 2, MaxFreq = 0, MinState = 50 };

    // ---------- Energieplan ----------
    public List<string> Apply(CpuSettings s, AppConfig cfg)
    {
        var msg = new List<string>();
        if (!s.UsePlan) { msg.AddRange(Reset(cfg)); return msg; }

        string active = ActiveScheme();
        if (cfg.TweakScheme == null || !SchemeExists(cfg.TweakScheme))
        {
            if (active != null && !active.Equals(cfg.TweakScheme, StringComparison.OrdinalIgnoreCase)) cfg.OriginalScheme = active;
            string src = cfg.OriginalScheme ?? "381b4222-f694-41f0-9685-ff5bb260df2e"; // Ausbalanciert
            var m = Regex.Match(Run("/duplicatescheme " + src), @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
            if (!m.Success) { msg.Add("Energieplan konnte nicht angelegt werden."); return msg; }
            cfg.TweakScheme = m.Value;
            Run($"/changename {cfg.TweakScheme} \"{PlanName}\" \"Erstellt von Nick Tweaker\"");
        }
        else if (active != null && !active.Equals(cfg.TweakScheme, StringComparison.OrdinalIgnoreCase)) cfg.OriginalScheme = active;

        string g = cfg.TweakScheme;
        Set(g, BoostGuid, Math.Clamp(s.Boost, 0, 3));
        Set(g, MaxStateGuid, 100);
        Set(g, MinStateGuid, Math.Clamp(s.MinState, 5, 100));
        int f = s.MaxFreq <= 0 ? 0 : Math.Clamp(s.MaxFreq, CapMin, CapMax);
        Set(g, FreqMax0, f);
        Set(g, FreqMax1, f);
        Run("/setactive " + g);
        if (!g.Equals(ActiveScheme(), StringComparison.OrdinalIgnoreCase)) msg.Add("Energieplan konnte nicht aktiviert werden.");
        return msg;
    }

    public List<string> Reset(AppConfig cfg)
    {
        var msg = new List<string>();
        string back = cfg.OriginalScheme ?? "381b4222-f694-41f0-9685-ff5bb260df2e";
        if (!SchemeExists(back)) back = "381b4222-f694-41f0-9685-ff5bb260df2e";
        Run("/setactive " + back);
        if (cfg.TweakScheme != null) { Run("/delete " + cfg.TweakScheme); cfg.TweakScheme = null; }
        return msg;
    }

    static void Set(string scheme, string setting, int v)
    {
        Run($"/setacvalueindex {scheme} {SubProc} {setting} {v}");
        Run($"/setdcvalueindex {scheme} {SubProc} {setting} {v}");
    }

    static string ActiveScheme()
    {
        var m = Regex.Match(Run("/getactivescheme"), @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
        return m.Success ? m.Value.ToLowerInvariant() : null;
    }

    static bool SchemeExists(string guid) => guid != null && Run("/list").Contains(guid, StringComparison.OrdinalIgnoreCase);

    static string Run(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("powercfg.exe", args)
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi);
            string o = p.StandardOutput.ReadToEnd();
            p.WaitForExit(8000);
            return o;
        }
        catch { return ""; }
    }

    public void Dispose() { try { _pc?.Close(); } catch { } }
}
