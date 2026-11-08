using System.Diagnostics;
using System.Text.Json;

namespace Tweaker;

/// <summary>Gespeicherte Einstellungen + Sicherheits-Merker (in %AppData%\NickTweaker\config.json).</summary>
public sealed class AppConfig
{
    public GpuSettings Gpu { get; set; } = new();
    public CpuSettings Cpu { get; set; } = new();
    public string HardwareId { get; set; }      // GPU+CPU-Name – passen die nicht, werden Werte verworfen
    public bool Stable { get; set; }             // erst nach bestandenem Test bzw. Bestätigung
    public bool Autostart { get; set; }
    public bool ApplyPending { get; set; }       // gesetzt beim Anwenden, gelöscht nach Bestätigung
    public bool BootPending { get; set; }        // gesetzt beim Autostart-Anwenden, gelöscht nach 90 s
    public string OriginalScheme { get; set; }
    public string TweakScheme { get; set; }
    public string Notice { get; set; }
    public bool TrayHintShown { get; set; }

    static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NickTweaker");
    static readonly string FilePath = Path.Combine(Dir, "config.json");
    static readonly JsonSerializerOptions Opt = new() { WriteIndented = true };

    public static AppConfig Load()
    {
        try { if (File.Exists(FilePath)) return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath)) ?? new(); }
        catch { }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Opt));
            File.Move(tmp, FilePath, true);
        }
        catch { }
    }
}

/// <summary>Autostart über die Windows-Aufgabenplanung: startet Tweaker beim Anmelden (mit Admin-Rechten, ohne UAC-Abfrage).</summary>
public static class Autostart
{
    const string Task = "NickTweaker-Apply";

    public static bool Set(bool on)
    {
        string exe = Environment.ProcessPath ?? Application.ExecutablePath;
        string xmlPath = Path.Combine(Path.GetTempPath(), "tweaker-task.xml");
        try
        {
            string args;
            if (on)
            {
                string user = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
                string xml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo><Description>Startet Tweaker beim Anmelden.</Description></RegistrationInfo>
  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{Esc(user)}</UserId><Delay>PT15S</Delay></LogonTrigger></Triggers>
  <Principals><Principal id=""Author""><UserId>{Esc(user)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>false</StartWhenAvailable>
    <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>
    <Enabled>true</Enabled>
    <Priority>5</Priority>
  </Settings>
  <Actions Context=""Author""><Exec><Command>{Esc(exe)}</Command><Arguments>--autostart</Arguments></Exec></Actions>
</Task>";
                File.WriteAllText(xmlPath, xml, System.Text.Encoding.Unicode);
                args = $"/Create /F /TN \"{Task}\" /XML \"{xmlPath}\"";
            }
            else args = $"/Delete /F /TN \"{Task}\"";

            var psi = new ProcessStartInfo("schtasks.exe", args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = Process.Start(psi);
            p.WaitForExit(10000);
            return p.ExitCode == 0 || !on;
        }
        catch { return false; }
        finally { try { File.Delete(xmlPath); } catch { } }
    }

    static string Esc(string v) => System.Security.SecurityElement.Escape(v);
}
