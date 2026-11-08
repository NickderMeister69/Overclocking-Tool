namespace Tweaker;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        bool auto = args.Contains("--autostart") || args.Contains("--apply");

        using var mutex = new Mutex(true, "NickTweaker-Single", out bool first);
        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "NickTweaker-Show");
        if (!first)
        {
            if (!auto) showSignal.Set();   // läuft schon (evtl. im Infobereich) -> Fenster nach vorne holen
            return;
        }
        ApplicationConfiguration.Initialize();
        bool applied = auto && BootApply();
        var form = new MainForm(auto, applied);
        var listener = new Thread(() =>
        {
            while (true)
            {
                showSignal.WaitOne();
                try { if (form.IsHandleCreated) form.ShowFromTray(); } catch { }
            }
        }) { IsBackground = true, Name = "Show-Signal" };
        listener.Start();
        Application.Run(form);
    }

    /// <summary>
    /// Beim Anmelden: gespeicherte (bestätigte) Grafikkarten-Werte übernehmen.
    /// Ist der PC nach dem letzten automatischen Anwenden innerhalb von 90 Sekunden abgestürzt,
    /// wird nichts angewendet und der Autostart der Werte abgeschaltet.
    /// Der CPU-Energieplan bleibt ohnehin in Windows gespeichert.
    /// </summary>
    static bool BootApply()
    {
        var cfg = AppConfig.Load();
        if (cfg.BootPending)
        {
            cfg.BootPending = false; cfg.Gpu = new(); cfg.Stable = false;
            cfg.Notice = "Nach dem letzten automatischen Anwenden beim Windows-Start ist der PC innerhalb von 90 Sekunden ausgegangen oder abgestürzt.\n\nZur Sicherheit läuft die Grafikkarte jetzt auf Werkseinstellung. Bitte die Werte etwas senken und neu testen.";
            cfg.Save();
            return false;
        }
        if (cfg.Gpu.IsDefault || cfg.ApplyPending) return false;

        using var gpu = new GpuTuner();
        if (!gpu.Available || cfg.HardwareId == null || !cfg.HardwareId.StartsWith(gpu.Name + "|")) return false;
        cfg.BootPending = true; cfg.Save();
        gpu.Apply(cfg.Gpu);
        return true;
    }
}
