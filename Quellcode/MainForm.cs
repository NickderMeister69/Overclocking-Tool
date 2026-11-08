using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace Tweaker;

public sealed class MainForm : Form
{
    const int TestSeconds = 180;
    public const string Version = "1.2";

    GpuTuner _gpu;
    CpuTuner _cpu;
    AppConfig _cfg;
    readonly CpuLoad _cpuLoad = new();
    readonly GpuLoad _gpuLoad = new();
    readonly System.Windows.Forms.Timer _tick = new() { Interval = 1000 };
    readonly System.Windows.Forms.Timer _anim = new() { Interval = 33 };
    Thread _poller;
    volatile bool _alive = true;

    GpuSettings _activeGpu = new();
    CpuSettings _activeCpu = new();
    bool _cpuUsePlan, _loading;

    bool _testing;
    DateTime _testStart;
    GpuSettings _testGpu; CpuSettings _testCpu;
    float _maxGpuT, _maxCpuT;

    // ---- Oberfläche ----
    Panel _side, _content;
    readonly Dictionary<string, Panel> _pages = new();
    readonly Dictionary<string, NavItem> _nav = new();
    string _page;
    TextLabel _hTitle, _hSub, _status;
    Toggle _sideAuto;
    readonly bool _startMinimized, _bootApplied;
    System.Windows.Forms.Timer _bootTimer;
    NotifyIcon _tray;
    bool _exiting;
    PillButton _bApply, _bTest;

    GaugeTile gCpuT, gCpuC, gGpuT, gGpuC;
    StatTile sCpuP, sCpuL, sGpuP, sGpuL, sTestGpu, sTestCpu;
    TextLabel _profGpu, _profCpu, _testInfo, _testResult, _testDesc;
    Badge _profBadge;
    PresetTile[] _gpuPresets = Array.Empty<PresetTile>(), _cpuPresets = Array.Empty<PresetTile>();
    SliderTile tPower, tCore, tMem, tCap, tCpuMax, tCpuMin;
    Segmented _turbo;
    TextLabel _turboHint;
    Toggle _auto;
    TestRing _ring;
    PillButton _bTestStart;
    static readonly int[] BoostByIndex = { 0, 3, 1, 2 };
    static readonly string[] TurboHints =
    {
        "Kein Turbo: deutlich kühler und leiser, aber spürbar langsamer.",
        "Turbo nur, wenn er sich lohnt: kühler, im Alltag kaum langsamer.",
        "Windows-Standard.",
        "Taktet schneller und öfter hoch: etwas flotter, aber wärmer."
    };

    public MainForm(bool startMinimized = false, bool bootApplied = false)
    {
        _startMinimized = startMinimized; _bootApplied = bootApplied;
        if (startMinimized) Opacity = 0;
        Text = "Tweaker";
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Theme.Bg; ForeColor = Theme.Txt; Font = Theme.F(10f);
        ClientSize = new Size(1180, 740);
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath); } catch { }

        var loading = new LoadingView { Dock = DockStyle.Fill };
        Controls.Add(loading);
        Shown += async (_, _) =>
        {
            await Task.Run(() =>
            {
                _cfg = AppConfig.Load();
                _gpu = new GpuTuner();
                _cpu = new CpuTuner();
                _cpu.ProbeMaxClock();
            });
            SuspendLayout();
            Controls.Remove(loading);
            loading.Dispose();
            BuildUi();
            float f = DeviceDpi / 96f;
            if (Math.Abs(f - 1f) > 0.01f) Scale(new SizeF(f, f));
            var wa = Screen.FromControl(this).WorkingArea;
            if (Height > wa.Height || Width > wa.Width) Size = new Size(Math.Min(Width, wa.Width), Math.Min(Height, wa.Height));
            ResumeLayout();
            CenterToScreen();
            Startup();
            if (_startMinimized) { Hide(); Opacity = 1; }
        };
        FormClosing += OnClosing;
    }

    protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; cp.Style |= 0x20000; return cp; } } // Schatten + Minimieren per Taskleiste
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Native.RoundCorners(Handle); }
    void DragWindow(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Native.ReleaseCapture(); Native.SendMessage(Handle, 0xA1, 2, 0); } }

    // =====================================================================
    //  Aufbau
    // =====================================================================
    void BuildUi()
    {
        // ---- Seitenleiste ----
        _side = new SidePanel { Bounds = new Rectangle(0, 0, 220, 740) };
        _side.MouseDown += DragWindow;
        Controls.Add(_side);
        int ny = 96;
        foreach (var (key, text, icon) in new[] { ("home", "Übersicht", "grid"), ("gpu", "Grafikkarte", "gpu"), ("cpu", "Prozessor", "cpu"), ("test", "Stabilitätstest", "pulse"), ("settings", "Einstellungen", "gear") })
        {
            var n = new NavItem(text, icon) { Location = new Point(16, ny) };
            n.Click += (_, _) => ShowPage(key);
            _side.Controls.Add(n); _nav[key] = n;
            ny += 48;
        }
        _side.Controls.Add(new TextLabel { Text = "Mit Windows starten", TextFont = Theme.F(9.5f, true), Wrap = false, Bounds = new Rectangle(20, 662, 130, 26) , Align = TextFormatFlags.Left | TextFormatFlags.VerticalCenter });
        _sideAuto = new Toggle { Bounds = new Rectangle(154, 663, 46, 24) };
        _sideAuto.CheckedChanged += (_, _) => SetAutostart(_sideAuto.Checked);
        _side.Controls.Add(_sideAuto);
        _side.Controls.Add(new TextLabel { Text = "Version " + Version, ForeColor = Theme.Dim, TextFont = Theme.F(8.5f), Wrap = false, Bounds = new Rectangle(20, 700, 180, 20) });

        // ---- Inhalt ----
        _content = new Panel { Bounds = new Rectangle(220, 0, 960, 740), BackColor = Theme.Bg };
        _content.MouseDown += DragWindow;
        Controls.Add(_content);
        _hTitle = new TextLabel { TextFont = Theme.Num(19f), Wrap = false, Bounds = new Rectangle(28, 22, 700, 34) };
        _hSub = new TextLabel { ForeColor = Theme.Mut, Wrap = false, Bounds = new Rectangle(29, 56, 700, 20) };
        _hTitle.MouseDown += DragWindow; _hSub.MouseDown += DragWindow;
        var bMin = new WinButton("min", false) { Location = new Point(868, 0) };
        var bClose = new WinButton("close", true) { Location = new Point(914, 0) };
        bMin.Click += (_, _) => WindowState = FormWindowState.Minimized;
        bClose.Click += (_, _) => Close();   // schließt ins Infobereich-Symbol (siehe OnClosing)
        _content.Controls.AddRange(new Control[] { _hTitle, _hSub, bMin, bClose });

        // Aktionsleiste
        _status = new TextLabel { ForeColor = Theme.Mut, Bounds = new Rectangle(28, 684, 560, 40), Align = TextFormatFlags.Left | TextFormatFlags.VerticalCenter };
        _bTest = new PillButton("Testen", false) { Bounds = new Rectangle(626, 682, 140, 42), IconName = "pulse" };
        _bApply = new PillButton("Anwenden", true) { Bounds = new Rectangle(778, 682, 154, 42) };
        _bTest.Click += (_, _) => { ShowPage("test"); if (!_testing) StartTest(); };
        _bApply.Click += (_, _) => DoApply();
        _content.Controls.AddRange(new Control[] { _status, _bTest, _bApply });

        BuildHome(); BuildGpu(); BuildCpu(); BuildTest(); BuildSettings();
        ShowPage("home");
        BuildTray();

        _tick.Tick += (_, _) => OnTick();
        _tick.Start();
        _anim.Tick += (_, _) =>
        {
            if (_page != "home" || !Visible || WindowState == FormWindowState.Minimized) return;
            foreach (var g in new[] { gCpuT, gCpuC, gGpuT, gGpuC }) if (g.Animate()) g.Invalidate();
        };
        _anim.Start();
        _poller = new Thread(() => { while (_alive) { try { _cpu.Poll(); _gpu.Poll(); } catch { } Thread.Sleep(1000); } }) { IsBackground = true, Name = "Sensoren" };
        _poller.Start();
    }

    Panel Page(string key)
    {
        var p = new Panel { Bounds = new Rectangle(0, 92, 960, 580), BackColor = Theme.Bg, Visible = false };
        _content.Controls.Add(p); _pages[key] = p;
        return p;
    }

    static T Add<T>(Control parent, T c) where T : Control { parent.Controls.Add(c); return c; }

    void ShowPage(string key)
    {
        _page = key;
        foreach (var (k, p) in _pages) p.Visible = k == key;
        foreach (var (k, n) in _nav) n.Selected = k == key;
        (_hTitle.Text, _hSub.Text) = key switch
        {
            "gpu" => ("Grafikkarte", _gpu.Available ? $"{_gpu.Name}  ·  {_gpu.Vram}  ·  Treiber {_gpu.Driver}" : "Keine NVIDIA-Grafikkarte erkannt"),
            "cpu" => ("Prozessor", $"{_cpu.Name}  ·  {_cpu.Cores} Kerne / {_cpu.Threads} Threads"),
            "test" => ("Stabilitätstest", "Prüft die eingestellten Werte unter Volllast"),
            "settings" => ("Einstellungen", "Autostart, Zurücksetzen und Sicherheit"),
            _ => ("Übersicht", "Live-Werte deines Systems")
        };
        bool tuning = key is "gpu" or "cpu";
        _bApply.Visible = _bTest.Visible = tuning;
        _status.Width = tuning ? 580 : 904;
    }

    // ---------------- Übersicht ----------------
    void BuildHome()
    {
        var p = Page("home");
        int x(int i) => 28 + i * 230;
        gCpuT = Add(p, new GaugeTile { Label = "CPU-Temperatur", Unit = "°C", Max = _cpu.TjMax, ColorOf = Theme.TempColor, Bounds = new Rectangle(x(0), 0, 214, 204), Sub = $"Grenze {_cpu.TjMax} °C" });
        gCpuC = Add(p, new GaugeTile { Label = "CPU-Takt", Unit = "MHz", Max = Math.Max(1000, _cpu.TurboClock * 1.05f), ColorOf = _ => Theme.Accent, Bounds = new Rectangle(x(1), 0, 214, 204), Sub = $"Basis {_cpu.BaseClock} MHz" });
        gGpuT = Add(p, new GaugeTile { Label = "GPU-Temperatur", Unit = "°C", Max = 100, ColorOf = Theme.TempColor, Bounds = new Rectangle(x(2), 0, 214, 204), Sub = _gpu.Available ? "" : "Keine NVIDIA-GPU" });
        gGpuC = Add(p, new GaugeTile { Label = "GPU-Takt", Unit = "MHz", Max = Math.Max(1000, _gpu.CapMax), ColorOf = _ => Theme.Accent, Bounds = new Rectangle(x(3), 0, 214, 204), Sub = _gpu.Available ? "" : "Keine NVIDIA-GPU" });

        sCpuP = Add(p, new StatTile { Label = "CPU-Leistung", Unit = "W", ScaleMax = 65, Accent = Theme.Accent, Bounds = new Rectangle(x(0), 220, 214, 124) });
        sCpuL = Add(p, new StatTile { Label = "CPU-Auslastung", Unit = "%", ScaleMax = 100, Accent = Theme.Cool, Bounds = new Rectangle(x(1), 220, 214, 124) });
        sGpuP = Add(p, new StatTile { Label = "GPU-Leistung", Unit = "W", ScaleMax = Math.Max(50, _gpu.PowerDefaultW), Accent = Theme.Accent, Bounds = new Rectangle(x(2), 220, 214, 124) });
        sGpuL = Add(p, new StatTile { Label = "GPU-Auslastung", Unit = "%", ScaleMax = 100, Accent = Theme.Cool, Bounds = new Rectangle(x(3), 220, 214, 124) });

        // Hardware
        var hw = Add(p, new Tile { Title = "Hardware", IconName = "cpu", Bounds = new Rectangle(28, 360, 444, 216) });
        void Row(Tile t, int y, string k, string v, string sub)
        {
            t.Controls.Add(new TextLabel { Text = k, ForeColor = Theme.Mut, TextFont = Theme.F(8.5f), Wrap = false, Bounds = new Rectangle(18, y, 400, 18) });
            t.Controls.Add(new TextLabel { Text = v, TextFont = Theme.F(11f, true), Wrap = false, Bounds = new Rectangle(18, y + 18, 408, 24) });
            t.Controls.Add(new TextLabel { Text = sub, ForeColor = Theme.Mut, TextFont = Theme.F(8.75f), Wrap = false, Bounds = new Rectangle(18, y + 42, 408, 18) });
        }
        Row(hw, 48, "PROZESSOR", _cpu.Name, $"{_cpu.Cores} Kerne · {_cpu.Threads} Threads · {_cpu.BaseClock}" + (_cpu.MaxSeen > _cpu.BaseClock ? $"–{_cpu.MaxSeen}" : "") + " MHz");
        Row(hw, 128, "GRAFIKKARTE", _gpu.Available ? _gpu.Name : "Keine NVIDIA-Grafikkarte",
            _gpu.Available ? $"{_gpu.Vram} · {_gpu.PowerDefaultW} W · Boost {_gpu.BoostClock} MHz · Treiber {_gpu.Driver}" : "Tuning nur für NVIDIA-Karten");

        // Profil
        var pr = Add(p, new Tile { Title = "Aktives Profil", IconName = "shield", Bounds = new Rectangle(488, 360, 444, 216) });
        _profBadge = Add(pr, new Badge { Bounds = new Rectangle(296, 12, 130, 24) });
        pr.Controls.Add(new TextLabel { Text = "GRAFIKKARTE", ForeColor = Theme.Mut, TextFont = Theme.F(8.5f), Wrap = false, Bounds = new Rectangle(18, 48, 400, 18) });
        _profGpu = Add(pr, new TextLabel { TextFont = Theme.F(10f, true), Wrap = false, Bounds = new Rectangle(18, 66, 408, 22) });
        pr.Controls.Add(new TextLabel { Text = "PROZESSOR", ForeColor = Theme.Mut, TextFont = Theme.F(8.5f), Wrap = false, Bounds = new Rectangle(18, 98, 400, 18) });
        _profCpu = Add(pr, new TextLabel { TextFont = Theme.F(10f, true), Wrap = false, Bounds = new Rectangle(18, 116, 408, 22) });
        var bAdj = Add(pr, new PillButton("Anpassen", true) { Bounds = new Rectangle(18, 156, 130, 40) });
        var bRes = Add(pr, new PillButton("Zurücksetzen", false) { Bounds = new Rectangle(158, 156, 140, 40), IconName = "reset" });
        bAdj.Click += (_, _) => ShowPage(_gpu.Available ? "gpu" : "cpu");
        bRes.Click += (_, _) => DoReset();
    }

    // ---------------- Grafikkarte ----------------
    void BuildGpu()
    {
        var p = Page("gpu");
        if (!_gpu.Available)
        {
            var t = Add(p, new Tile { Title = "Keine NVIDIA-Grafikkarte", IconName = "gpu", Bounds = new Rectangle(28, 0, 904, 200) });
            t.Controls.Add(new TextLabel
            {
                ForeColor = Theme.Mut, Bounds = new Rectangle(18, 50, 860, 130),
                Text = "Das Grafikkarten-Tuning funktioniert mit NVIDIA-Karten.\n\nAMD: Übertakten und Undervolten geht in „AMD Software: Adrenalin“ unter Leistung → Optimierung.\nIntel Arc: „Intel Graphics Software“ unter Leistung.\n\nDie Prozessor-Einstellungen kannst du trotzdem nutzen."
            });
            return;
        }

        var defs = new (string t, string s, string i, Color c)[]
        {
            ("Standard", "Werkseinstellung", "reset", Theme.Mut),
            ("Undervolt", "Kühler und leiser", "leaf", Theme.Cool),
            ("Leichtes OC", "Etwas mehr Leistung", "up", Theme.Accent),
            ("Max OC", "Oberes Ende, sicherer Bereich", "up2", Theme.Accent)
        };
        _gpuPresets = new PresetTile[4];
        for (int i = 0; i < 4; i++)
        {
            int idx = i;
            _gpuPresets[i] = Add(p, new PresetTile { Title = defs[i].t, Sub = defs[i].s, IconName = defs[i].i, Accent = defs[i].c, Bounds = new Rectangle(28 + i * 230, 0, 214, 84) });
            _gpuPresets[i].Click += (_, _) => PickGpuPreset(idx);
        }

        tPower = Add(p, new SliderTile { Title = "Leistungslimit", IconName = "power", Bounds = new Rectangle(28, 100, 444, 146),
            Hint = !_gpu.PowerAdjustable ? "Bei dieser Karte vom Hersteller fest vorgegeben."
                 : _gpu.HighPower ? "Nur Absenken möglich. Das schützt den Stromstecker dieser sehr starken Karte."
                 : "Weniger heißt kühler und leiser. Mehr bringt etwas FPS, wenn die Karte es zulässt." });
        tPower.Slider.SetRange(_gpu.PowerMinPct, _gpu.PowerMaxPct); tPower.Slider.Zero = 100; tPower.Slider.Enabled = _gpu.PowerAdjustable;

        tCore = Add(p, new SliderTile { Title = "Kern-Takt", IconName = "up", Bounds = new Rectangle(488, 100, 444, 146),
            Hint = _gpu.OffsetsSupported ? $"Mehr Takt bei gleicher Spannung. Für deine Karte freigegeben: bis +{_gpu.CoreMax} MHz." : "Der Treiber gibt das unter Windows nicht frei. Leistungslimit und Takt-Obergrenze gehen trotzdem." });
        tCore.Slider.Step = 5; tCore.Slider.SetRange(_gpu.CoreMin, Math.Max(_gpu.CoreMin, _gpu.CoreMax)); tCore.Slider.Zero = 0; tCore.Slider.Enabled = _gpu.OffsetsSupported;

        tMem = Add(p, new SliderTile { Title = "Speicher-Takt", IconName = "grid", Bounds = new Rectangle(28, 262, 444, 146),
            Hint = _gpu.OffsetsSupported && _gpu.MemMax > 0 ? $"Schnellerer Grafikspeicher. Freigegeben: bis +{_gpu.MemMax} MHz." : "Für diese Karte nicht verfügbar." });
        tMem.Slider.Step = 25; tMem.Slider.SetRange(_gpu.MemMin, Math.Max(_gpu.MemMin, _gpu.MemMax)); tMem.Slider.Zero = 0; tMem.Slider.Enabled = _gpu.OffsetsSupported && _gpu.MemMax > 0;

        tCap = Add(p, new SliderTile { Title = "Takt-Obergrenze", IconName = "leaf", Bounds = new Rectangle(488, 262, 444, 146),
            Hint = "Deckelt den Höchsttakt, die Karte braucht dann weniger Spannung. Zusammen mit Kern-Takt ergibt das ein Undervolt." });
        tCap.Slider.Step = 15; tCap.Slider.SetRange(_gpu.CapMin, _gpu.CapMax + 15); tCap.Slider.Enabled = _gpu.ClockLockSupported;

        foreach (var t in new[] { tPower, tCore, tMem, tCap }) t.Slider.ValueChanged += (_, _) => { if (!_loading) MarkDirty(); UpdateLabels(); };

        var info = Add(p, new Tile { Title = "Erkannte Grenzen", IconName = "shield", Bounds = new Rectangle(28, 424, 904, 152) });
        var facts = new (string k, string v)[]
        {
            ("GENERATION", _gpu.Family + (_gpu.Laptop ? " · Laptop" : "")),
            ("LEISTUNGSLIMIT", _gpu.PowerAdjustable ? $"{_gpu.PowerMinPct}–{_gpu.PowerMaxPct} %" : "fest"),
            ("KERN-TAKT", _gpu.OffsetsSupported ? $"{_gpu.CoreMin} bis +{_gpu.CoreMax} MHz" : "gesperrt"),
            ("SPEICHER-TAKT", _gpu.OffsetsSupported && _gpu.MemMax > 0 ? $"bis +{_gpu.MemMax} MHz" : "–"),
            ("TEST-ABBRUCH AB", $"{_gpu.SafetyTemp} °C"),
        };
        for (int i = 0; i < facts.Length; i++)
        {
            int fx = 18 + i * 176;
            info.Controls.Add(new TextLabel { Text = facts[i].k, ForeColor = Theme.Mut, TextFont = Theme.F(8f), Wrap = false, Bounds = new Rectangle(fx, 50, 170, 18) });
            info.Controls.Add(new TextLabel { Text = facts[i].v, TextFont = Theme.F(10f, true), Wrap = false, Bounds = new Rectangle(fx, 68, 170, 22) });
        }
        info.Controls.Add(new TextLabel { ForeColor = Theme.Mut, TextFont = Theme.F(8.75f), Bounds = new Rectangle(18, 104, 860, 36),
            Text = "Die Grenzen richten sich nach Modell und Generation der Karte und nach den Werten, die der NVIDIA-Treiber selbst erlaubt. Es gilt jeweils der vorsichtigere Wert." });
    }

    // ---------------- Prozessor ----------------
    void BuildCpu()
    {
        var p = Page("cpu");
        var defs = new (string t, string s, string i, Color c)[]
        {
            ("Windows", "Standard-Energieplan", "reset", Theme.Mut),
            ("Effizient", "Kühler, kaum langsamer", "leaf", Theme.Cool),
            ("Ausgewogen", "Guter Mittelweg", "scale", Theme.Accent),
            ("Leistung", "Taktet schneller hoch", "bolt", Theme.Accent)
        };
        _cpuPresets = new PresetTile[4];
        for (int i = 0; i < 4; i++)
        {
            int idx = i;
            _cpuPresets[i] = Add(p, new PresetTile { Title = defs[i].t, Sub = defs[i].s, IconName = defs[i].i, Accent = defs[i].c, Bounds = new Rectangle(28 + i * 230, 0, 214, 84) });
            _cpuPresets[i].Click += (_, _) => PickCpuPreset(idx);
        }

        var tt = Add(p, new Tile { Title = "Turbo-Verhalten", IconName = "bolt", Bounds = new Rectangle(28, 100, 904, 146) });
        _turbo = Add(tt, new Segmented { Items = new[] { "Aus", "Effizient", "Normal", "Aggressiv" }, Bounds = new Rectangle(18, 50, 868, 42) });
        _turboHint = Add(tt, new TextLabel { ForeColor = Theme.Mut, TextFont = Theme.F(8.75f), Wrap = false, Bounds = new Rectangle(18, 104, 868, 22) });
        _turbo.SelectedChanged += (_, _) => { if (!_loading) { _cpuUsePlan = true; MarkDirty(); } UpdateLabels(); };

        tCpuMax = Add(p, new SliderTile { Title = "Maximaler Takt", IconName = "cpu", Bounds = new Rectangle(28, 262, 444, 146),
            Hint = "Weniger Takt heißt weniger Spannung, Wärme und Lüfterlärm. Ganz rechts: kein Limit." });
        tCpuMax.Slider.Step = 100; tCpuMax.Slider.SetRange(_cpu.CapMin, _cpu.CapMax + 100);
        tCpuMin = Add(p, new SliderTile { Title = "Minimaler Prozessorzustand", IconName = "pulse", Bounds = new Rectangle(488, 262, 444, 146),
            Hint = "Höher reagiert schneller, braucht im Leerlauf aber mehr Strom. Standard ist 5 %." });
        tCpuMin.Slider.Step = 5; tCpuMin.Slider.SetRange(5, 100);
        foreach (var t in new[] { tCpuMax, tCpuMin }) t.Slider.ValueChanged += (_, _) => { if (!_loading) { _cpuUsePlan = true; MarkDirty(); } UpdateLabels(); };

        var info = Add(p, new Tile { Title = _cpu.Unlocked ? "Übertaktbarer Prozessor" : "Dein Prozessor", IconName = "shield", Bounds = new Rectangle(28, 424, 904, 152) });
        info.Controls.Add(new TextLabel { Text = _cpu.OcInfo, ForeColor = Theme.Mut, TextFont = Theme.F(9f), Bounds = new Rectangle(18, 48, 860, 64) });
        info.Controls.Add(new TextLabel { Text = $"Gespeichert als eigener Energieplan „{CpuTuner.PlanName}“. Zurücksetzen stellt deinen vorherigen Plan wieder her.", ForeColor = Theme.Dim, TextFont = Theme.F(8.5f), Wrap = false, Bounds = new Rectangle(18, 118, 860, 20) });
    }

    // ---------------- Test ----------------
    void BuildTest()
    {
        var p = Page("test");
        var left = Add(p, new Tile { Title = "Volllast-Test", IconName = "pulse", Bounds = new Rectangle(28, 0, 444, 418) });
        _ring = Add(left, new TestRing { Bounds = new Rectangle(102, 44, 240, 220), Total = TestSeconds });
        _testDesc = Add(left, new TextLabel { ForeColor = Theme.Mut, TextFont = Theme.F(8.75f), Bounds = new Rectangle(24, 272, 396, 64), Align = TextFormatFlags.HorizontalCenter,
            Text = "Drei Minuten Volllast auf Prozessor und Grafikkarte. Geprüft wird, ob sich die Grafikkarte verrechnet, ob der Treiber abstürzt und ob es zu heiß wird. Bei Problemen wird sofort zurückgesetzt." });
        _bTestStart = Add(left, new PillButton("Test starten", true) { Bounds = new Rectangle(132, 350, 180, 44), IconName = "pulse" });
        _bTestStart.Click += (_, _) => { if (_testing) EndTest(false, "Test abgebrochen.", userAbort: true); else StartTest(); };

        sTestGpu = Add(p, new StatTile { Label = "GPU-Temperatur", Unit = "°C", ScaleMax = 100, Accent = Theme.Accent, Bounds = new Rectangle(488, 0, 444, 128) });
        sTestCpu = Add(p, new StatTile { Label = "CPU-Temperatur", Unit = "°C", ScaleMax = 100, Accent = Theme.Cool, Bounds = new Rectangle(488, 144, 444, 128) });
        var chk = Add(p, new Tile { Title = "Prüfung", IconName = "shield", Bounds = new Rectangle(488, 288, 444, 130) });
        _testInfo = Add(chk, new TextLabel { ForeColor = Theme.Mut, TextFont = Theme.F(9f), Bounds = new Rectangle(18, 46, 408, 76) });

        var res = Add(p, new Tile { Title = "Ergebnis", IconName = "grid", Bounds = new Rectangle(28, 434, 904, 142) });
        _testResult = Add(res, new TextLabel { ForeColor = Theme.Mut, TextFont = Theme.F(10f), Bounds = new Rectangle(18, 48, 868, 80), Text = "Noch kein Test gelaufen." });
    }

    // ---------------- Einstellungen ----------------
    void BuildSettings()
    {
        var p = Page("settings");
        var a = Add(p, new Tile { Title = "Autostart", IconName = "power", Bounds = new Rectangle(28, 0, 904, 96) });
        a.Controls.Add(new TextLabel { Text = "Tweaker beim Anmelden starten und deine Werte übernehmen", TextFont = Theme.F(10f, true), Wrap = false, Bounds = new Rectangle(18, 44, 700, 22) });
        a.Controls.Add(new TextLabel { Text = "Startet minimiert. Stürzt der PC kurz nach dem Hochfahren ab, werden die Werte nicht erneut übernommen.", ForeColor = Theme.Mut, TextFont = Theme.F(8.75f), Wrap = false, Bounds = new Rectangle(18, 66, 780, 20) });
        _auto = Add(a, new Toggle { Bounds = new Rectangle(834, 44, 48, 26) });
        _auto.CheckedChanged += (_, _) => SetAutostart(_auto.Checked);

        var r = Add(p, new Tile { Title = "Werkseinstellung", IconName = "reset", Bounds = new Rectangle(28, 112, 904, 96) });
        r.Controls.Add(new TextLabel { Text = "Grafikkarte und Prozessor zurück auf Standard", TextFont = Theme.F(10f, true), Wrap = false, Bounds = new Rectangle(18, 44, 640, 22) });
        r.Controls.Add(new TextLabel { Text = "Stellt auch deinen vorherigen Windows-Energieplan wieder her.", ForeColor = Theme.Mut, TextFont = Theme.F(8.75f), Wrap = false, Bounds = new Rectangle(18, 66, 640, 20) });
        var br = Add(r, new PillButton("Alles zurücksetzen", false) { Bounds = new Rectangle(710, 40, 176, 40), IconName = "reset" });
        br.Click += (_, _) => DoReset();

        var s = Add(p, new Tile { Title = "Sicherheitsnetz", IconName = "shield", Bounds = new Rectangle(28, 224, 904, 214) });
        var items = new[]
        {
            ("Grenzen pro Karte", "Regler gehen nur so weit, wie es für dein Modell als sicher gilt."),
            ("15-Sekunden-Abfrage", "Nach dem Anwenden ohne Bestätigung geht es automatisch zurück."),
            ("Absturzschutz", "Friert der PC ein, wird beim nächsten Start alles zurückgesetzt."),
            ("Autostart-Wächter", "Stürzt der PC kurz nach dem Hochfahren ab, schaltet sich der Autostart ab."),
            ("Temperatur-Stopp", $"Der Test bricht ab bei GPU {_gpu.SafetyTemp} °C oder CPU {_cpu.SafetyTemp} °C."),
        };
        for (int i = 0; i < items.Length; i++)
        {
            int y = 46 + i * 32;
            s.Controls.Add(new Dot { Bounds = new Rectangle(20, y + 6, 8, 8) });
            s.Controls.Add(new TextLabel { Text = items[i].Item1, TextFont = Theme.F(9.5f, true), Wrap = false, Bounds = new Rectangle(38, y, 200, 20) });
            s.Controls.Add(new TextLabel { Text = items[i].Item2, ForeColor = Theme.Mut, TextFont = Theme.F(9f), Wrap = false, Bounds = new Rectangle(240, y, 640, 20) });
        }

        var ab = Add(p, new Tile { Title = "Über", IconName = "gear", Bounds = new Rectangle(28, 454, 904, 96) });
        ab.Controls.Add(new TextLabel { Text = $"Tweaker {Version}", TextFont = Theme.F(10f, true), Wrap = false, Bounds = new Rectangle(18, 44, 640, 22) });
        ab.Controls.Add(new TextLabel { Text = "Übertakten geschieht auf eigene Verantwortung.", ForeColor = Theme.Mut, TextFont = Theme.F(8.75f), Wrap = false, Bounds = new Rectangle(18, 66, 640, 20) });
        var bo = Add(ab, new PillButton("Einstellungsordner", false) { Bounds = new Rectangle(710, 40, 176, 40) });
        bo.Click += (_, _) =>
        {
            try { var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NickTweaker"); Directory.CreateDirectory(d); Process.Start("explorer.exe", d); } catch { }
        };
    }

    // =====================================================================
    //  Start: gespeicherte Werte laden, Sicherheitsprüfungen
    // =====================================================================
    string HwId => _gpu.Name + "|" + _cpu.Name;

    void Startup()
    {
        if (_cfg.HardwareId != null && _cfg.HardwareId != HwId)
        {
            _cfg.Gpu = new(); _cfg.Cpu = new(); _cfg.Stable = false;
            _cfg.Notice = "Es wurde andere Hardware erkannt. Die gespeicherten Werte wurden verworfen – bitte neu einstellen und testen.";
        }
        _cfg.HardwareId = HwId;

        if (_cfg.ApplyPending)
        {
            _gpu.Reset(); _cpu.Reset(_cfg);
            _cfg.Gpu = new(); _cfg.Cpu = new(); _cfg.Stable = false; _cfg.ApplyPending = false;
            _cfg.Notice = "Beim letzten Mal wurden neue Werte angewendet, aber nicht bestätigt. Vermutlich ist der PC hängen geblieben.\n\nZur Sicherheit läuft wieder alles auf Werkseinstellung. Probier es mit kleineren Schritten.";
        }

        var now = _gpu.ReadCurrent();
        var saved = _gpu.Clamp(_cfg.Gpu);
        _activeGpu = now.PowerPct == saved.PowerPct && now.CoreOffset == saved.CoreOffset && now.MemOffset == saved.MemOffset ? saved : now;
        _activeCpu = _cfg.Cpu.Clone();
        _cfg.Save();

        LoadIntoUi(_activeGpu.IsDefault && !saved.IsDefault ? saved : _activeGpu, _cfg.Cpu);
        _loading = true; _auto.Checked = _sideAuto.Checked = _cfg.Autostart; _loading = false;
        if (_cfg.Autostart) Autostart.Set(true);   // Pfad aktuell halten, falls die exe verschoben wurde
        if (_bootApplied)
        {
            _bootTimer = new System.Windows.Forms.Timer { Interval = 90_000 };
            _bootTimer.Tick += (_, _) => ClearBootPending();
            _bootTimer.Start();
        }
        bool dirty = !SameGpu(CurGpu(), _activeGpu);
        RefreshActive();
        Status(dirty ? "Gespeicherte Werte geladen. Mit „Anwenden“ aktivieren." : "Bereit.", dirty ? Theme.Warn : Theme.Mut);

        if (_cfg.Notice != null)
        {
            string n = _cfg.Notice; _cfg.Notice = null; _cfg.Save();
            NoticeDialog.Show(this, "Sicherheitshinweis", n, Theme.Warn);
        }
    }

    void LoadIntoUi(GpuSettings g, CpuSettings c)
    {
        _loading = true;
        if (_gpu.Available && tPower != null)
        {
            g = _gpu.Clamp(g);
            tPower.Slider.Value = g.PowerPct; tCore.Slider.Value = g.CoreOffset; tMem.Slider.Value = g.MemOffset;
            tCap.Slider.Value = g.ClockCap == 0 ? tCap.Slider.Maximum : g.ClockCap;
        }
        _cpuUsePlan = c.UsePlan;
        _turbo.SelectedIndex = Math.Max(0, Array.IndexOf(BoostByIndex, c.UsePlan ? c.Boost : 1));
        tCpuMax.Slider.Value = !c.UsePlan || c.MaxFreq <= 0 ? tCpuMax.Slider.Maximum : c.MaxFreq;
        tCpuMin.Slider.Value = c.UsePlan ? c.MinState : 5;
        _loading = false;
        UpdateLabels();
    }

    GpuSettings CurGpu() => !_gpu.Available || tPower == null ? new() : _gpu.Clamp(new GpuSettings
    {
        PowerPct = tPower.Slider.Value, CoreOffset = tCore.Slider.Value, MemOffset = tMem.Slider.Value,
        ClockCap = tCap.Slider.Value >= tCap.Slider.Maximum ? 0 : tCap.Slider.Value
    });

    CpuSettings CurCpu() => !_cpuUsePlan ? new CpuSettings { UsePlan = false } : new CpuSettings
    {
        UsePlan = true, Boost = BoostByIndex[Math.Max(0, _turbo.SelectedIndex)],
        MaxFreq = tCpuMax.Slider.Value >= tCpuMax.Slider.Maximum ? 0 : tCpuMax.Slider.Value, MinState = tCpuMin.Slider.Value
    };

    static bool SameGpu(GpuSettings a, GpuSettings b) => a.PowerPct == b.PowerPct && a.CoreOffset == b.CoreOffset && a.MemOffset == b.MemOffset && a.ClockCap == b.ClockCap;
    static bool SameCpu(CpuSettings a, CpuSettings b) => a.UsePlan == b.UsePlan && (!a.UsePlan || (a.Boost == b.Boost && a.MaxFreq == b.MaxFreq && a.MinState == b.MinState));

    // =====================================================================
    //  Voreinstellungen
    // =====================================================================
    void PickGpuPreset(int i)
    {
        if (_testing) return;
        var g = i switch { 1 => _gpu.PresetUndervolt(), 2 => _gpu.PresetLightOc(), 3 => _gpu.PresetMaxOc(), _ => new GpuSettings() };
        LoadIntoUi(g, CurCpu());
        MarkDirty();
        Status(i switch
        {
            1 => "Undervolt: gleiche Leistung mit weniger Spannung. Danach testen.",
            2 => "Leichtes OC: ein paar Prozent mehr, sehr sicher. Danach testen.",
            3 => "Max OC: höchste freigegebene Werte. Unbedingt testen.",
            _ => "Werkseinstellung der Grafikkarte."
        }, Theme.Warn);
    }

    void PickCpuPreset(int i)
    {
        if (_testing) return;
        var c = i switch { 1 => _cpu.PresetEfficient(), 2 => _cpu.PresetBalanced(), 3 => _cpu.PresetPerformance(), _ => _cpu.PresetDefault() };
        LoadIntoUi(CurGpu(), c);
        MarkDirty();
    }

    void MarkDirty()
    {
        bool dirty = !SameGpu(CurGpu(), _activeGpu) || !SameCpu(CurCpu(), _activeCpu);
        if (dirty) Status("Geändert, noch nicht aktiv.", Theme.Warn);
        else Status("Entspricht den aktiven Werten.", Theme.Mut);
    }

    void UpdateLabels()
    {
        if (_gpu.Available && tPower != null)
        {
            tPower.ValueText = $"{tPower.Slider.Value} %  ·  {_gpu.PowerDefaultW * tPower.Slider.Value / 100} W";
            tCore.ValueText = $"{tCore.Slider.Value:+0;-0;0} MHz";
            tMem.ValueText = $"{tMem.Slider.Value:+0;-0;0} MHz";
            tCap.ValueText = tCap.Slider.Value >= tCap.Slider.Maximum ? "Kein Limit" : $"{tCap.Slider.Value} MHz";
            var g = CurGpu();
            var presets = new[] { new GpuSettings(), _gpu.PresetUndervolt(), _gpu.PresetLightOc(), _gpu.PresetMaxOc() };
            int hit = Array.FindIndex(presets, x => SameGpu(x, g));
            for (int i = 0; i < _gpuPresets.Length; i++) _gpuPresets[i].Selected = i == hit;
        }
        if (_turbo != null)
        {
            bool on = _cpuUsePlan;
            tCpuMax.ValueText = !on ? "Windows" : tCpuMax.Slider.Value >= tCpuMax.Slider.Maximum ? "Kein Limit" : $"{tCpuMax.Slider.Value} MHz";
            tCpuMin.ValueText = !on ? "Windows" : $"{tCpuMin.Slider.Value} %";
            _turboHint.Text = !on ? "Windows entscheidet selbst (normaler Energieplan)." : TurboHints[Math.Clamp(_turbo.SelectedIndex, 0, 3)];
            var c = CurCpu();
            var presets = new[] { _cpu.PresetDefault(), _cpu.PresetEfficient(), _cpu.PresetBalanced(), _cpu.PresetPerformance() };
            int hit = Array.FindIndex(presets, x => SameCpu(x, c));
            for (int i = 0; i < _cpuPresets.Length; i++) _cpuPresets[i].Selected = i == hit;
        }
    }

    // =====================================================================
    //  Anwenden / Zurücksetzen
    // =====================================================================
    List<string> ApplyAll(GpuSettings g, CpuSettings c)
    {
        var msg = new List<string>();
        msg.AddRange(_gpu.Apply(g));
        if (!SameCpu(c, _activeCpu) || c.UsePlan) msg.AddRange(_cpu.Apply(c, _cfg));
        return msg;
    }

    void DoApply()
    {
        if (_testing) return;
        var g = CurGpu(); var c = CurCpu();
        var prevG = _activeGpu; var prevC = _activeCpu;
        _cfg.ApplyPending = true; _cfg.Save();
        var msg = ApplyAll(g, c);
        _activeGpu = g; _activeCpu = c;

        using var dlg = new ConfirmDialog();
        if (dlg.ShowDialog(this) == DialogResult.Yes)
        {
            bool changedFromStable = !(_cfg.Stable && SameGpu(g, _cfg.Gpu) && SameCpu(c, _cfg.Cpu));
            _cfg.Gpu = g; _cfg.Cpu = c; _cfg.ApplyPending = false;
            if (changedFromStable) _cfg.Stable = g.IsDefault && !c.UsePlan;
            _cfg.Save();
            if (msg.Count > 0) Status("Teilweise angewendet: " + string.Join(" ", msg), Theme.Warn);
            else if (_cfg.Stable) Status("Aktiv.", Theme.Good);
            else Status("Aktiv. Tipp: Mit „Testen“ prüfen, ob alles stabil läuft.", Theme.Good);
        }
        else
        {
            ApplyAll(prevG, prevC);
            _activeGpu = prevG; _activeCpu = prevC;
            _cfg.ApplyPending = false; _cfg.Save();
            Status("Zurückgesetzt auf die vorherigen Werte.", Theme.Warn);
        }
        RefreshActive();
    }

    void DoReset()
    {
        if (_testing) return;
        var msg = _gpu.Reset();
        msg.AddRange(_cpu.Reset(_cfg));
        _activeGpu = new(); _activeCpu = new();
        _cfg.Gpu = new(); _cfg.Cpu = new(); _cfg.Stable = false; _cfg.ApplyPending = false;
        _cfg.Save();
        LoadIntoUi(new GpuSettings(), new CpuSettings());
        RefreshActive();
        Status(msg.Count > 0 ? string.Join(" ", msg) : "Alles auf Werkseinstellung.", msg.Count > 0 ? Theme.Warn : Theme.Good);
    }

    // =====================================================================
    //  Stabilitätstest
    // =====================================================================
    void StartTest()
    {
        _testGpu = CurGpu(); _testCpu = CurCpu();
        if (_testGpu.IsDefault && !_testCpu.UsePlan)
        {
            _testResult.Text = "Es ist nur die Werkseinstellung gewählt, da gibt es nichts zu testen. Stell zuerst unter Grafikkarte oder Prozessor etwas ein.";
            _testResult.ForeColor = Theme.Mut;
            return;
        }
        _cfg.ApplyPending = true; _cfg.Save();
        var msg = ApplyAll(_testGpu, _testCpu);
        if (msg.Count > 0) Status(string.Join(" ", msg), Theme.Warn);

        if (_gpu.Available) _gpuLoad.Start();
        _cpuLoad.Start();
        _testing = true; _testStart = DateTime.Now; _maxGpuT = _maxCpuT = -1;
        SetTestUi(true);
        _testResult.Text = "Läuft. Den PC am besten in Ruhe lassen.";
        _testResult.ForeColor = Theme.Warn;
        Status("Stabilitätstest läuft …", Theme.Warn);
    }

    void TestTick()
    {
        int el = (int)(DateTime.Now - _testStart).TotalSeconds;
        _ring.Elapsed = Math.Clamp(el, 0, TestSeconds);
        if (_gpu.Temp > _maxGpuT) _maxGpuT = _gpu.Temp;
        if (_cpu.Temp > _maxCpuT) _maxCpuT = _cpu.Temp;

        string tip = (_testGpu.CoreOffset > 0 ? "Kern-Takt um 30–50 MHz senken" : "") +
                     (_testGpu.MemOffset > 0 ? (_testGpu.CoreOffset > 0 ? " oder " : "") + "Speicher-Takt um 100–200 MHz senken" : "");
        if (tip == "") tip = "Leistungslimit senken";

        if (_gpuLoad.DriverCrashed || _gpu.Lost) { EndTest(false, $"Der Grafiktreiber ist abgestürzt, die Werte sind zu hoch. Tipp: {tip}."); return; }
        if (_gpuLoad.Error != null) { EndTest(false, "Der GPU-Test konnte nicht laufen: " + _gpuLoad.Error); return; }
        if (_gpuLoad.Errors > 0) { EndTest(false, $"Die Grafikkarte hat sich {_gpuLoad.Errors}× verrechnet und ist damit nicht stabil. Tipp: {tip}."); return; }
        if (_cpuLoad.Errors > 0) { EndTest(false, "Der Prozessor hat sich verrechnet. Bitte die BIOS-Einstellungen (Spannung, XMP) prüfen."); return; }
        if (_gpu.Available && _gpu.Temp >= _gpu.SafetyTemp) { EndTest(false, $"Die Grafikkarte wurde zu heiß ({_gpu.Temp:0} °C). Tipp: Leistungslimit senken oder Undervolt nutzen."); return; }
        if (_cpu.Temp >= _cpu.SafetyTemp) { EndTest(false, $"Der Prozessor wurde zu heiß ({_cpu.Temp:0} °C). Tipp: Effizient wählen oder die Kühlung prüfen."); return; }

        _testInfo.Text = (_gpu.Available ? $"Rechenprüfungen der Grafikkarte: {_gpuLoad.Checks}, alle fehlerfrei\n" : "") +
                         $"GPU: {_testGpu}\nCPU: {_testCpu}";
        if (el >= TestSeconds) EndTest(true, null);
    }

    void EndTest(bool ok, string reason, bool userAbort = false)
    {
        _gpuLoad.Stop(); _cpuLoad.Stop();
        _testing = false;
        SetTestUi(false);
        string temps = (_maxGpuT > 0 ? $"  Höchstwerte: GPU {_maxGpuT:0} °C" : "") + (_maxCpuT > 0 ? $", CPU {_maxCpuT:0} °C." : "");

        if (ok)
        {
            _activeGpu = _testGpu; _activeCpu = _testCpu;
            _cfg.Gpu = _testGpu; _cfg.Cpu = _testCpu; _cfg.Stable = true; _cfg.ApplyPending = false; _cfg.Save();
            _ring.State = TestRing.Mode.Passed;
            _testResult.Text = "Bestanden. Die Werte sind aktiv und gespeichert." + temps + (_cfg.Autostart ? "\nSie werden beim Windows-Start übernommen." : "\nTipp: Mit „Mit Windows starten“ unten links werden sie beim Anmelden automatisch übernommen.");
            _testResult.ForeColor = Theme.Good;
            Status("Test bestanden.", Theme.Good);
        }
        else if (userAbort)
        {
            try { ApplyAll(_activeGpu, _activeCpu); } catch { }
            _cfg.ApplyPending = false; _cfg.Save();
            _ring.State = TestRing.Mode.Idle;
            _testResult.Text = "Abgebrochen. Die vorherigen Werte sind wieder aktiv.";
            _testResult.ForeColor = Theme.Mut;
            Status("Test abgebrochen.", Theme.Mut);
        }
        else
        {
            try { _gpu.Reset(); } catch { }
            try { _cpu.Reset(_cfg); } catch { }
            _activeGpu = new(); _activeCpu = new();
            _cfg.Gpu = new(); _cfg.Cpu = new(); _cfg.Stable = false; _cfg.ApplyPending = false; _cfg.Save();
            _ring.State = TestRing.Mode.Failed;
            _testResult.Text = "Nicht bestanden. " + reason + "\nAlles läuft wieder auf Werkseinstellung. Deine Regler stehen noch auf den getesteten Werten, du kannst sie direkt etwas senken.";
            _testResult.ForeColor = Theme.Hot;
            Status("Test nicht bestanden, Werkseinstellung aktiv.", Theme.Hot);
        }
        RefreshActive();
    }

    void SetTestUi(bool on)
    {
        _bApply.Enabled = !on;
        _bTest.Enabled = !on;
        _bTestStart.Text = on ? "Abbrechen" : "Test starten";
        _bTestStart.Primary = !on;
        _bTestStart.Invalidate();
        foreach (var t in new[] { tPower, tCore, tMem, tCap, tCpuMax, tCpuMin }) if (t != null) t.Slider.Enabled = !on && IsAdjustable(t);
        _turbo.Enabled = !on;
        foreach (var pt in _gpuPresets.Concat(_cpuPresets)) pt.Enabled = !on;
        if (on) { _ring.State = TestRing.Mode.Running; _ring.Elapsed = 0; }
    }

    bool IsAdjustable(SliderTile t) =>
        t == tPower ? _gpu.PowerAdjustable :
        t == tCore ? _gpu.OffsetsSupported :
        t == tMem ? _gpu.OffsetsSupported && _gpu.MemMax > 0 :
        t == tCap ? _gpu.ClockLockSupported : true;

    // =====================================================================
    //  Laufende Anzeige
    // =====================================================================
    void OnTick()
    {
        if (_tray != null)
        {
            string tip = "Tweaker" + (_cpu.Temp > 0 ? $" · CPU {_cpu.Temp:0} °C" : "") + (_gpu.Available && _gpu.Temp > 0 ? $" · GPU {_gpu.Temp:0} °C" : "");
            _tray.Text = tip.Length > 63 ? tip[..63] : tip;
        }
        gCpuT.Target = _cpu.Temp; gCpuC.Target = _cpu.Clock;
        gGpuT.Target = _gpu.Available ? _gpu.Temp : -1; gGpuC.Target = _gpu.Available ? _gpu.ClockGr : -1;
        if (_gpu.Available)
        {
            gGpuT.Sub = _gpu.Fan >= 0 ? $"Lüfter {_gpu.Fan:0} %" : "";
            gGpuC.Sub = _gpu.Throttle != "" && _gpu.Util > 50 ? $"Gebremst: {_gpu.Throttle}" : _gpu.ClockMem >= 0 ? $"Speicher {_gpu.ClockMem:0} MHz" : "";
        }
        if (!_cpu.SensorsOk) gCpuT.Sub = gCpuC.Sub = "Sensor nicht verfügbar";
        sCpuP.Push(_cpu.PowerW); sCpuL.Push(_cpu.Load);
        sGpuP.Push(_gpu.Available ? _gpu.PowerW : -1); sGpuL.Push(_gpu.Available ? _gpu.Util : -1);
        sTestGpu.Push(_gpu.Available ? _gpu.Temp : -1); sTestCpu.Push(_cpu.Temp);
        if (_testing) TestTick();
    }

    void RefreshActive()
    {
        _profGpu.Text = _gpu.Available ? _activeGpu.ToString() : "–";
        _profCpu.Text = _activeCpu.ToString();
        bool def = _activeGpu.IsDefault && !_activeCpu.UsePlan;
        _profBadge.Text = def ? "Werkseinstellung" : _cfg.Stable ? "Getestet" : "Aktiv";
        _profBadge.Accent = def ? Theme.Mut : Theme.Good;
        _profBadge.Invalidate();
        UpdateLabels();
    }

    void SetAutostart(bool on)
    {
        if (_loading || _cfg == null) return;
        _loading = true; _auto.Checked = on; _sideAuto.Checked = on; _loading = false;
        _cfg.Autostart = on;
        _cfg.Save();
        if (!Autostart.Set(on)) Status("Autostart konnte nicht eingerichtet werden.", Theme.Hot);
        else Status(on ? "Tweaker startet ab jetzt beim Anmelden und übernimmt deine Werte." : "Autostart ist aus.", Theme.Mut);
    }

    void Status(string text, Color col) { if (_status == null) return; _status.Text = text; _status.ForeColor = col; }

    // =====================================================================
    //  Symbol im Infobereich der Taskleiste
    // =====================================================================
    void BuildTray()
    {
        var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), ShowImageMargin = false, Font = Theme.F(9.5f) };
        var open = new ToolStripMenuItem("Tweaker öffnen") { Font = Theme.F(9.5f, true) };
        open.Click += (_, _) => ShowFromTray();
        var test = new ToolStripMenuItem("Stabilitätstest");
        test.Click += (_, _) => { ShowFromTray(); ShowPage("test"); };
        var reset = new ToolStripMenuItem("Alles zurücksetzen");
        reset.Click += (_, _) => DoReset();
        var quit = new ToolStripMenuItem("Beenden");
        quit.Click += (_, _) => ExitApp();
        menu.Items.AddRange(new ToolStripItem[] { open, test, new ToolStripSeparator(), reset, new ToolStripSeparator(), quit });
        foreach (ToolStripItem i in menu.Items) i.ForeColor = Theme.Txt;

        _tray = new NotifyIcon { Icon = Icon ?? SystemIcons.Application, Text = "Tweaker", ContextMenuStrip = menu, Visible = true };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowFromTray(); };
    }

    public void ShowFromTray()
    {
        if (InvokeRequired) { BeginInvoke(ShowFromTray); return; }
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    void ExitApp()
    {
        _exiting = true;
        Close();
    }

    void ClearBootPending()
    {
        _bootTimer?.Stop(); _bootTimer?.Dispose(); _bootTimer = null;
        if (_cfg == null) return;
        _cfg.BootPending = false; _cfg.Save();
    }

    void OnClosing(object sender, FormClosingEventArgs e)
    {
        // X-Knopf: nicht beenden, sondern ins Infobereich-Symbol verschwinden
        if (!_exiting && _tray != null && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            if (!_cfg.TrayHintShown)
            {
                _cfg.TrayHintShown = true; _cfg.Save();
                _tray.ShowBalloonTip(4000, "Tweaker läuft weiter", "Deine Einstellungen bleiben aktiv. Zum Öffnen auf das Symbol klicken, Beenden über Rechtsklick.", ToolTipIcon.None);
            }
            return;
        }
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        if (_bootTimer != null) ClearBootPending();
        if (_testing) EndTest(false, "Test abgebrochen.", userAbort: true);
        _alive = false;
        _tick.Stop(); _anim.Stop();
        _poller?.Join(1500);
        _cpu?.Dispose(); _gpu?.Dispose();
    }
}

// =========================================================================
//  Seiten-spezifische Zeichen-Controls
// =========================================================================
sealed class SidePanel : Panel
{
    public SidePanel()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Side;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Side);
        float s = Theme.Px(this, 30);
        Icons.Draw(g, "logo", new RectangleF(Theme.Px(this, 22), Theme.Px(this, 26), s, s), Theme.Accent);
        using (var f = Theme.Num(15f)) Theme.Text(g, "TWEAKER", f, Theme.Txt, new Rectangle((int)Theme.Px(this, 62), (int)Theme.Px(this, 22), Width, (int)Theme.Px(this, 24)));
        using (var f = Theme.F(8f)) Theme.Text(g, "CPU & GPU Tuning", f, Theme.Mut, new Rectangle((int)Theme.Px(this, 63), (int)Theme.Px(this, 44), Width, (int)Theme.Px(this, 18)));
        using var pen = new Pen(Theme.Line);
        g.DrawLine(pen, Width - 1, 0, Width - 1, Height);
    }
}

sealed class Dot : Painted
{
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prep(e);
        using var b = new SolidBrush(Theme.Accent);
        g.FillEllipse(b, 0, 0, Width - 1, Height - 1);
    }
}

sealed class LoadingView : Painted
{
    int _phase;
    readonly System.Windows.Forms.Timer _t = new() { Interval = 40 };
    public LoadingView() { BackColor = Theme.Bg; _t.Tick += (_, _) => { _phase = (_phase + 6) % 360; Invalidate(); }; _t.Start(); }
    protected override void Dispose(bool d) { if (d) _t.Dispose(); base.Dispose(d); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Theme.Bg);
        float s = S(56), cx = Width / 2f, cy = Height / 2f - S(30);
        Icons.Draw(g, "logo", new RectangleF(cx - s / 2, cy - s / 2, s, s), Theme.Accent);
        using (var p = new Pen(Theme.Accent, S(3)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawArc(p, cx - s * 0.95f, cy - s * 0.95f, s * 1.9f, s * 1.9f, _phase, 80);
        using var f = Theme.F(11f);
        Theme.Text(g, "Hardware wird erkannt …", f, Theme.Mut, new Rectangle(0, (int)(cy + s * 1.2f), Width, (int)S(30)), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

sealed class TestRing : Painted
{
    public enum Mode { Idle, Running, Passed, Failed }
    public int Total = 180;
    int _el; Mode _m;
    public int Elapsed { get => _el; set { _el = value; Invalidate(); } }
    public Mode State { get => _m; set { _m = value; Invalidate(); } }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prep(e);
        float th = S(12), d = Math.Min(Width, Height) - th - S(4);
        var r = new RectangleF((Width - d) / 2, (Height - d) / 2, d, d);
        using (var pt = new Pen(Theme.Track, th)) g.DrawEllipse(pt, r);
        var col = _m switch { Mode.Passed => Theme.Good, Mode.Failed => Theme.Hot, _ => Theme.Accent };
        float frac = _m switch { Mode.Passed => 1, Mode.Failed => 1, Mode.Running => _el / (float)Total, _ => 0 };
        if (frac > 0) using (var pv = new Pen(col, th) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(pv, r, -90, Math.Max(1, frac * 360));
        int left = Math.Max(0, Total - _el);
        string big = _m switch { Mode.Passed => "OK", Mode.Failed => "Fehler", Mode.Running => $"{left / 60}:{left % 60:00}", _ => "3:00" };
        string small = _m switch { Mode.Passed => "bestanden", Mode.Failed => "nicht bestanden", Mode.Running => "verbleibend", _ => "bereit" };
        using (var fb = Theme.Num(30f)) Theme.Text(g, big, fb, _m == Mode.Idle ? Theme.Txt : _m == Mode.Running ? Theme.Txt : col, new Rectangle(0, (int)(Height / 2 - S(30)), Width, (int)S(44)), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        using (var fs = Theme.F(9f)) Theme.Text(g, small, fs, Theme.Mut, new Rectangle(0, (int)(Height / 2 + S(14)), Width, (int)S(20)), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new DarkColors()) { RoundedEdges = false; }
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var r = new Rectangle(2, 0, e.Item.Width - 4, e.Item.Height);
        using var b = new SolidBrush(e.Item.Selected ? Theme.TileHi : Theme.Tile);
        e.Graphics.FillRectangle(b, r);
    }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Text == "Beenden" && e.Item.Selected ? Theme.Hot : Theme.Txt;
        base.OnRenderItemText(e);
    }
    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var p = new Pen(Theme.Line);
        int y = e.Item.Height / 2;
        e.Graphics.DrawLine(p, 8, y, e.Item.Width - 8, y);
    }

    sealed class DarkColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.Tile;
        public override Color MenuBorder => Theme.Line;
        public override Color MenuItemBorder => Theme.TileHi;
        public override Color MenuItemSelected => Theme.TileHi;
        public override Color ImageMarginGradientBegin => Theme.Tile;
        public override Color ImageMarginGradientMiddle => Theme.Tile;
        public override Color ImageMarginGradientEnd => Theme.Tile;
    }
}
