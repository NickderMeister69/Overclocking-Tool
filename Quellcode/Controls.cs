using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace Tweaker;

// =========================================================================
//  Farben & Schriften
// =========================================================================
static class Theme
{
    public static readonly Color
        Bg = C(0x0D0F12), Side = C(0x111419), Tile = C(0x171A20), TileHi = C(0x1D2129), Line = C(0x252A33),
        Track = C(0x262B34), Txt = C(0xE8EAED), Mut = C(0x8A919E), Dim = C(0x5A616D),
        Accent = C(0xFF6A2B), Cool = C(0x33C7B0), Warn = C(0xFFB547), Hot = C(0xFF4D4F), Good = C(0x3DD68C);

    static Color C(int rgb) => Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
    public static Color Mix(Color a, Color b, float t) =>
        Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    static readonly HashSet<string> Fonts = new(new InstalledFontCollection().Families.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
    static string Pick(params string[] names) => names.FirstOrDefault(Fonts.Contains) ?? "Segoe UI";

    static readonly string UiR = Pick("Segoe UI Variable Text", "Segoe UI");
    static readonly string UiB = Pick("Segoe UI Variable Text Semibold", "Segoe UI Semibold", "Segoe UI");
    static readonly string NumB = Pick("Bahnschrift SemiBold", "Bahnschrift", "Segoe UI Semibold");
    static readonly string NumR = Pick("Bahnschrift", "Segoe UI");

    public static Font F(float size, bool bold = false) => new(bold ? UiB : UiR, size);
    public static Font Num(float size, bool bold = true) => new(bold ? NumB : NumR, size);

    public static Color TempColor(float t) => t < 0 ? Dim : t < 70 ? Cool : t < 83 ? Warn : Hot;

    public static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath(); float d = rad * 2;
        if (d <= 0) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }

    public static void Text(Graphics g, string s, Font f, Color c, Rectangle r, TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter)
        => TextRenderer.DrawText(g, s, f, r, c, flags | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

    public static float Px(Control c, float v) => v * c.DeviceDpi / 96f;
}

// =========================================================================
//  Vektor-Icons (selbst gezeichnet, skaliert sauber)
// =========================================================================
static class Icons
{
    public static void Draw(Graphics g, string name, RectangleF r, Color col, float w = 1.8f)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var p = new Pen(col, w) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var b = new SolidBrush(col);
        float x = r.X, y = r.Y, s = Math.Min(r.Width, r.Height);
        PointF P(float px, float py) => new(x + px * s, y + py * s);
        RectangleF R(float a, float bb, float c, float d) => new(x + a * s, y + bb * s, c * s, d * s);

        switch (name)
        {
            case "grid":
                foreach (var (a, c) in new[] { (0.1f, 0.1f), (0.55f, 0.1f), (0.1f, 0.55f), (0.55f, 0.55f) })
                    using (var path = Theme.Round(R(a, c, 0.35f, 0.35f), s * 0.07f)) g.DrawPath(p, path);
                break;
            case "gpu":
                using (var path = Theme.Round(R(0.06f, 0.24f, 0.88f, 0.5f), s * 0.07f)) g.DrawPath(p, path);
                g.DrawEllipse(p, R(0.5f, 0.32f, 0.34f, 0.34f));
                g.DrawLine(p, P(0.18f, 0.74f), P(0.18f, 0.86f)); g.DrawLine(p, P(0.34f, 0.74f), P(0.34f, 0.86f));
                g.DrawLine(p, P(0.18f, 0.4f), P(0.38f, 0.4f)); g.DrawLine(p, P(0.18f, 0.55f), P(0.32f, 0.55f));
                break;
            case "cpu":
                using (var path = Theme.Round(R(0.22f, 0.22f, 0.56f, 0.56f), s * 0.07f)) g.DrawPath(p, path);
                g.DrawRectangle(p, x + 0.38f * s, y + 0.38f * s, 0.24f * s, 0.24f * s);
                foreach (var t in new[] { 0.35f, 0.5f, 0.65f })
                {
                    g.DrawLine(p, P(t, 0.06f), P(t, 0.22f)); g.DrawLine(p, P(t, 0.78f), P(t, 0.94f));
                    g.DrawLine(p, P(0.06f, t), P(0.22f, t)); g.DrawLine(p, P(0.78f, t), P(0.94f, t));
                }
                break;
            case "pulse":
                g.DrawLines(p, new[] { P(0.04f, 0.55f), P(0.28f, 0.55f), P(0.38f, 0.25f), P(0.52f, 0.82f), P(0.63f, 0.45f), P(0.7f, 0.55f), P(0.96f, 0.55f) });
                break;
            case "gear":
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4;
                    g.DrawLine(new Pen(col, w * 1.9f) { StartCap = LineCap.Round, EndCap = LineCap.Round },
                        P(0.5f + 0.3f * (float)Math.Cos(a), 0.5f + 0.3f * (float)Math.Sin(a)), P(0.5f + 0.42f * (float)Math.Cos(a), 0.5f + 0.42f * (float)Math.Sin(a)));
                }
                g.DrawEllipse(p, R(0.22f, 0.22f, 0.56f, 0.56f));
                g.DrawEllipse(p, R(0.39f, 0.39f, 0.22f, 0.22f));
                break;
            case "reset":
                g.DrawArc(p, R(0.16f, 0.16f, 0.68f, 0.68f), -60, 300);
                g.FillPolygon(b, new[] { P(0.62f, 0.06f), P(0.86f, 0.2f), P(0.6f, 0.32f) });
                break;
            case "leaf":
                using (var path = new GraphicsPath())
                {
                    path.AddBezier(P(0.16f, 0.84f), P(0.1f, 0.3f), P(0.5f, 0.12f), P(0.88f, 0.12f));
                    path.AddBezier(P(0.88f, 0.12f), P(0.88f, 0.55f), P(0.62f, 0.9f), P(0.16f, 0.84f));
                    g.DrawPath(p, path);
                }
                g.DrawLine(p, P(0.16f, 0.84f), P(0.6f, 0.4f));
                break;
            case "up":
                g.DrawLines(p, new[] { P(0.2f, 0.66f), P(0.5f, 0.36f), P(0.8f, 0.66f) });
                break;
            case "up2":
                g.DrawLines(p, new[] { P(0.2f, 0.52f), P(0.5f, 0.22f), P(0.8f, 0.52f) });
                g.DrawLines(p, new[] { P(0.2f, 0.8f), P(0.5f, 0.5f), P(0.8f, 0.8f) });
                break;
            case "scale":
                g.DrawLine(p, P(0.5f, 0.14f), P(0.5f, 0.86f)); g.DrawLine(p, P(0.3f, 0.86f), P(0.7f, 0.86f));
                g.DrawLine(p, P(0.14f, 0.28f), P(0.86f, 0.28f));
                g.DrawArc(p, R(0.04f, 0.32f, 0.3f, 0.26f), 0, 180); g.DrawArc(p, R(0.66f, 0.32f, 0.3f, 0.26f), 0, 180);
                break;
            case "bolt":
                g.DrawPolygon(p, new[] { P(0.58f, 0.06f), P(0.2f, 0.56f), P(0.48f, 0.56f), P(0.4f, 0.94f), P(0.8f, 0.42f), P(0.52f, 0.42f) });
                break;
            case "shield":
                using (var path = new GraphicsPath())
                {
                    path.AddLines(new[] { P(0.5f, 0.06f), P(0.86f, 0.2f) });
                    path.AddBezier(P(0.86f, 0.2f), P(0.86f, 0.62f), P(0.7f, 0.82f), P(0.5f, 0.94f));
                    path.AddBezier(P(0.5f, 0.94f), P(0.3f, 0.82f), P(0.14f, 0.62f), P(0.14f, 0.2f));
                    path.CloseFigure(); g.DrawPath(p, path);
                }
                g.DrawLines(p, new[] { P(0.34f, 0.5f), P(0.46f, 0.62f), P(0.68f, 0.38f) });
                break;
            case "power":
                g.DrawArc(p, R(0.14f, 0.18f, 0.72f, 0.72f), -50, 280);
                g.DrawLine(p, P(0.5f, 0.06f), P(0.5f, 0.46f));
                break;
            case "close":
                g.DrawLine(p, P(0.25f, 0.25f), P(0.75f, 0.75f)); g.DrawLine(p, P(0.75f, 0.25f), P(0.25f, 0.75f));
                break;
            case "min":
                g.DrawLine(p, P(0.22f, 0.55f), P(0.78f, 0.55f));
                break;
            case "logo":
                var hex = Enumerable.Range(0, 6).Select(i => { double a = Math.PI / 3 * i - Math.PI / 2; return P(0.5f + 0.46f * (float)Math.Cos(a), 0.5f + 0.46f * (float)Math.Sin(a)); }).ToArray();
                g.FillPolygon(b, hex);
                using (var bb = new SolidBrush(Theme.Side))
                    g.FillPolygon(bb, new[] { P(0.56f, 0.18f), P(0.3f, 0.54f), P(0.48f, 0.54f), P(0.42f, 0.82f), P(0.7f, 0.44f), P(0.52f, 0.44f) });
                break;
        }
    }
}

// =========================================================================
//  Basis: selbst gezeichnetes Control mit Hover
// =========================================================================
abstract class Painted : Control
{
    protected bool Hover;
    protected Painted()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent; ForeColor = Theme.Txt;
    }
    protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { Hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected Color ParentBg => Parent is Tile ? Theme.Tile : Parent?.BackColor ?? Theme.Bg;
    protected float S(float v) => Theme.Px(this, v);
    protected Graphics Prep(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(ParentBg); return g;
    }
}

/// <summary>Kachel: abgerundete Fläche, optional mit Überschrift + Icon.</summary>
class Tile : Panel
{
    public string Title { get; set; }
    public string IconName { get; set; }
    public Color IconColor { get; set; } = Theme.Mut;
    public Color? Border { get; set; }
    public Tile()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Tile; ForeColor = Theme.Txt;
    }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        float r = Theme.Px(this, 12);
        using (var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), r))
        {
            using (var b = new SolidBrush(Theme.Tile)) g.FillPath(b, path);
            using (var pen = new Pen(Border ?? Theme.Line)) g.DrawPath(pen, path);
        }
        if (Title != null)
        {
            int x = (int)Theme.Px(this, 18);
            if (IconName != null)
            {
                float s = Theme.Px(this, 16);
                Icons.Draw(g, IconName, new RectangleF(x, Theme.Px(this, 15), s, s), IconColor, Theme.Px(this, 1.6f));
                x += (int)Theme.Px(this, 26);
            }
            using var f = Theme.F(8.5f, true);
            Theme.Text(g, Title.ToUpperInvariant(), f, Theme.Mut, new Rectangle(x, (int)Theme.Px(this, 12), Width - x - 10, (int)Theme.Px(this, 22)));
        }
    }
}

// =========================================================================
//  Ring-Anzeige mit weicher Animation
// =========================================================================
sealed class GaugeTile : Painted
{
    public string Label = "", Unit = "", Sub = "";
    public float Max = 100, Target = -1;
    public Func<float, Color> ColorOf = _ => Theme.Accent;
    public int Decimals;
    float _shown = -1;

    public bool Animate()
    {
        if (Target < 0) { if (_shown != -1) { _shown = -1; return true; } return false; }
        if (_shown < 0) { _shown = Target; return true; }
        float d = Target - _shown;
        if (Math.Abs(d) < Max * 0.001f) { if (_shown != Target) { _shown = Target; return true; } return false; }
        _shown += d * 0.18f;
        return true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prep(e);
        using (var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), S(12)))
        {
            using (var b = new SolidBrush(Theme.Tile)) g.FillPath(b, path);
            using (var pen = new Pen(Theme.Line)) g.DrawPath(pen, path);
        }
        using (var f = Theme.F(8.5f, true))
            Theme.Text(g, Label.ToUpperInvariant(), f, Theme.Mut, new Rectangle((int)S(18), (int)S(12), Width - (int)S(36), (int)S(22)));

        float th = S(10), d = Math.Min(Width - S(56), Height - S(80));
        var rc = new RectangleF((Width - d) / 2, S(42), d, d);
        var col = _shown < 0 ? Theme.Dim : ColorOf(_shown);
        using (var pt = new Pen(Theme.Track, th) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(pt, rc, 135, 270);
        if (_shown > 0)
        {
            float sweep = Math.Clamp(_shown / Max, 0.005f, 1f) * 270;
            using var pv = new Pen(col, th) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(pv, rc, 135, sweep);
        }
        string val = _shown < 0 ? "–" : _shown.ToString(Decimals == 0 ? "0" : "0.0");
        using (var fv = Theme.Num(22f))
            Theme.Text(g, val, fv, Theme.Txt, new Rectangle((int)rc.X, (int)(rc.Y + rc.Height * 0.30f), (int)rc.Width, (int)S(36)), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        using (var fu = Theme.F(8.5f))
            Theme.Text(g, Unit, fu, Theme.Mut, new Rectangle((int)rc.X, (int)(rc.Y + rc.Height * 0.30f + S(34)), (int)rc.Width, (int)S(18)), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        using (var fs = Theme.F(8.5f))
            Theme.Text(g, Sub, fs, Theme.Mut, new Rectangle((int)S(12), Height - (int)S(30), Width - (int)S(24), (int)S(20)), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>Kachel mit großer Zahl und Verlaufskurve der letzten 60 Sekunden.</summary>
sealed class StatTile : Painted
{
    public string Label = "", Unit = "", Value = "–";
    public Color Accent = Theme.Accent;
    public readonly List<float> History = new();
    public float ScaleMax = 100;

    public void Push(float v)
    {
        History.Add(Math.Max(0, v));
        if (History.Count > 60) History.RemoveAt(0);
        Value = v < 0 ? "–" : v.ToString("0");
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prep(e);
        using (var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), S(12)))
        {
            using (var b = new SolidBrush(Theme.Tile)) g.FillPath(b, path);
            using (var pen = new Pen(Theme.Line)) g.DrawPath(pen, path);
        }
        using (var f = Theme.F(8.5f, true))
            Theme.Text(g, Label.ToUpperInvariant(), f, Theme.Mut, new Rectangle((int)S(18), (int)S(12), Width - (int)S(36), (int)S(22)));
        using var fv = Theme.Num(20f);
        var sz = TextRenderer.MeasureText(g, Value, fv, Size.Empty, TextFormatFlags.NoPadding);
        Theme.Text(g, Value, fv, Theme.Txt, new Rectangle((int)S(16), (int)S(34), sz.Width + 4, (int)S(34)));
        using (var fu = Theme.F(9f))
            Theme.Text(g, Unit, fu, Theme.Mut, new Rectangle((int)S(20) + sz.Width, (int)S(44), (int)S(60), (int)S(22)));

        if (History.Count > 1)
        {
            float top = S(74), bottom = Height - S(10), left = S(10), right = Width - S(10);
            float mx = Math.Max(ScaleMax, History.Max());
            var pts = History.Select((v, i) => new PointF(right - (History.Count - 1 - i) * (right - left) / 59f, bottom - v / mx * (bottom - top))).ToArray();
            using var area = new GraphicsPath();
            area.AddLines(pts);
            area.AddLine(pts[^1], new PointF(pts[^1].X, bottom));
            area.AddLine(new PointF(pts[^1].X, bottom), new PointF(pts[0].X, bottom));
            area.CloseFigure();
            using (var lg = new LinearGradientBrush(new RectangleF(0, top - 1, 10, bottom - top + 2), Color.FromArgb(70, Accent), Color.FromArgb(0, Accent), 90f))
                g.FillPath(lg, area);
            using var pen = new Pen(Accent, S(1.6f)) { LineJoin = LineJoin.Round };
            g.DrawLines(pen, pts);
        }
    }
}

/// <summary>Auswahl-Kachel für Voreinstellungen.</summary>
sealed class PresetTile : Painted
{
    public string Title = "", Sub = "", IconName = "up";
    public Color Accent = Theme.Accent;
    bool _sel;
    public bool Selected { get => _sel; set { if (_sel != value) { _sel = value; Invalidate(); } } }
    public PresetTile() { Cursor = Cursors.Hand; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prep(e);
        var fill = !Enabled ? Theme.Tile : _sel ? Theme.Mix(Theme.Tile, Accent, 0.12f) : Hover ? Theme.TileHi : Theme.Tile;
        using (var path = Theme.Round(new RectangleF(0.75f, 0.75f, Width - 2f, Height - 2f), S(12)))
        {
            using (var b = new SolidBrush(fill)) g.FillPath(b, path);
            using (var pen = new Pen(_sel ? Accent : Theme.Line, _sel ? S(1.5f) : 1f)) g.DrawPath(pen, path);
        }
        float box = S(38);
        var ib = new RectangleF(S(14), (Height - box) / 2, box, box);
        using (var path = Theme.Round(ib, S(9)))
        using (var b = new SolidBrush(Theme.Mix(Theme.Tile, Accent, _sel ? 0.28f : 0.14f))) g.FillPath(b, path);
        Icons.Draw(g, IconName, RectangleF.Inflate(ib, -S(9), -S(9)), Enabled ? Accent : Theme.Dim, S(1.8f));
        int tx = (int)(ib.Right + S(12));
        using (var ft = Theme.F(10f, true))
            Theme.Text(g, Title, ft, Enabled ? Theme.Txt : Theme.Dim, new Rectangle(tx, (int)(Height / 2 - S(20)), Width - tx - (int)S(8), (int)S(20)));
        using (var fs = Theme.F(8.5f))
            Theme.Text(g, Sub, fs, Theme.Mut, new Rectangle(tx, (int)(Height / 2 + S(1)), Width - tx - (int)S(8), (int)S(18)));
    }
}

/// <summary>Kachel mit Schieberegler, großer Wertanzeige und kurzer Erklärung.</summary>
sealed class SliderTile : Tile
{
    public readonly Slider Slider = new();
    string _value = "", _hint = "";
    public string ValueText { get => _value; set { if (_value != value) { _value = value; Invalidate(new Rectangle(Width / 2, 0, Width / 2, (int)Theme.Px(this, 50))); } } }
    public string Hint { get => _hint; set { _hint = value; Invalidate(); } }
    public Color Accent { get => Slider.Accent; set => Slider.Accent = value; }

    public SliderTile() { Controls.Add(Slider); }
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        Slider.SetBounds((int)Theme.Px(this, 12), (int)Theme.Px(this, 58), Width - (int)Theme.Px(this, 24), (int)Theme.Px(this, 28));
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        using (var fv = Theme.Num(17f))
            Theme.Text(g, _value, fv, Slider.Enabled ? Theme.Txt : Theme.Dim, new Rectangle(Width / 2, (int)Theme.Px(this, 12), Width / 2 - (int)Theme.Px(this, 18), (int)Theme.Px(this, 30)), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        using (var fh = Theme.F(8.5f))
            TextRenderer.DrawText(g, _hint, fh, new Rectangle((int)Theme.Px(this, 18), (int)Theme.Px(this, 94), Width - (int)Theme.Px(this, 36), Height - (int)Theme.Px(this, 100)), Theme.Mut, TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
    }
}

/// <summary>Schieberegler (Maus + Pfeiltasten).</summary>
sealed class Slider : Painted
{
    int _min, _max = 100, _val;
    bool _drag;
    public int Step { get; set; } = 1;
    public Color Accent { get; set; } = Theme.Accent;
    public int Zero { get; set; } = int.MinValue;
    public event EventHandler ValueChanged;

    public Slider() { SetStyle(ControlStyles.Selectable, true); Cursor = Cursors.Hand; TabStop = true; }

    public void SetRange(int min, int max) { _min = min; _max = Math.Max(min, max); Value = _val; Invalidate(); }
    public int Minimum => _min;
    public int Maximum => _max;
    public int Value
    {
        get => _val;
        set
        {
            int v = Math.Clamp(value, _min, _max);
            if (Step > 1 && v != _max && v != _min) v = _min + (int)Math.Round((v - _min) / (double)Step) * Step;
            v = Math.Clamp(v, _min, _max);
            if (v == _val) return;
            _val = v; Invalidate(); ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    float Pad => S(9);
    float X(int v) => Pad + (Width - 2 * Pad) * (_max == _min ? 0 : (v - _min) / (float)(_max - _min));

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prep(e);
        float cy = Height / 2f, th = S(4);
        var col = Enabled ? Accent : Theme.Dim;
        using (var t = new Pen(Theme.Track, th) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(t, Pad, cy, Width - Pad, cy);
        float x0 = X(Zero == int.MinValue ? _min : Math.Clamp(Zero, _min, _max)), x1 = X(_val);
        if (Math.Abs(x1 - x0) > 0.5f)
            using (var f = new Pen(col, th) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(f, x0, cy, x1, cy);
        if (Zero != int.MinValue && Zero > _min && Zero < _max)
            using (var z = new Pen(Theme.Dim, S(1.5f))) g.DrawLine(z, x0, cy - S(7), x0, cy + S(7));
        float k = S(Hover || _drag || Focused ? 9 : 8);
        if (Enabled && (Hover || _drag)) using (var halo = new SolidBrush(Color.FromArgb(40, col))) g.FillEllipse(halo, x1 - k * 1.8f, cy - k * 1.8f, k * 3.6f, k * 3.6f);
        using (var kb = new SolidBrush(Enabled ? Color.White : Theme.Mut)) g.FillEllipse(kb, x1 - k, cy - k, 2 * k, 2 * k);
        using (var kp = new Pen(col, S(2.5f))) g.DrawEllipse(kp, x1 - k + S(1.2f), cy - k + S(1.2f), 2 * k - S(2.4f), 2 * k - S(2.4f));
    }

    void FromMouse(int x)
    {
        float f = Math.Clamp((x - Pad) / Math.Max(1, Width - 2 * Pad), 0, 1);
        Value = _min + (int)Math.Round(f * (_max - _min));
    }
    protected override void OnMouseDown(MouseEventArgs e) { if (!Enabled) return; Focus(); _drag = true; FromMouse(e.X); }
    protected override void OnMouseMove(MouseEventArgs e) { if (_drag) FromMouse(e.X); }
    protected override void OnMouseUp(MouseEventArgs e) { _drag = false; Invalidate(); }
    protected override bool IsInputKey(Keys k) => k is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(k);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        int s = Math.Max(1, Step);
        if (e.KeyCode is Keys.Left or Keys.Down) Value = _val - s;
        if (e.KeyCode is Keys.Right or Keys.Up) Value = _val + s;
    }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
}

/// <summary>Segment-Auswahl (z. B. Turbo-Modus).</summary>
sealed class Segmented : Painted
{
    public string[] Items = Array.Empty<string>();
    public Color Accent = Theme.Accent;
    int _sel, _hover = -1;
    public event EventHandler SelectedChanged;
    public int SelectedIndex { get => _sel; set { if (_sel != value) { _sel = value; Invalidate(); SelectedChanged?.Invoke(this, EventArgs.Empty); } } }
    public Segmented() { Cursor = Cursors.Hand; }

    RectangleF Seg(int i) { float w = (Width - S(8)) / Items.Length; return new RectangleF(S(4) + i * w, S(4), w, Height - S(8)); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prep(e);
        using (var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), S(10)))
        using (var b = new SolidBrush(Theme.Bg)) g.FillPath(b, path);
        using var f = Theme.F(9.5f, true);
        for (int i = 0; i < Items.Length; i++)
        {
            var r = Seg(i);
            if (i == _sel || i == _hover)
                using (var path = Theme.Round(r, S(8)))
                using (var b = new SolidBrush(i == _sel ? (Enabled ? Accent : Theme.Dim) : Theme.TileHi)) g.FillPath(b, path);
            Theme.Text(g, Items[i], f, i == _sel ? Color.White : Theme.Mut, Rectangle.Round(r), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
    int Hit(int x) { for (int i = 0; i < Items.Length; i++) if (Seg(i).Contains(x, Height / 2)) return i; return -1; }
    protected override void OnMouseMove(MouseEventArgs e) { int h = Hit(e.X); if (h != _hover) { _hover = h; Invalidate(); } }
    protected override void OnMouseLeave(EventArgs e) { _hover = -1; base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (!Enabled) return; int h = Hit(e.X); if (h >= 0) SelectedIndex = h; }
}

/// <summary>An/Aus-Schalter.</summary>
sealed class Toggle : Painted
{
    bool _on;
    public bool Checked { get => _on; set { if (_on != value) { _on = value; Invalidate(); CheckedChanged?.Invoke(this, EventArgs.Empty); } } }
    public event EventHandler CheckedChanged;
    public Toggle() { Cursor = Cursors.Hand; Size = new Size(46, 24); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prep(e);
        var r = new RectangleF(1, 1, Width - 2, Height - 2);
        using (var path = Theme.Round(r, r.Height / 2))
        using (var b = new SolidBrush(_on ? Theme.Accent : Theme.Track)) g.FillPath(b, path);
        float d = r.Height - S(6);
        float x = _on ? r.Right - d - S(3) : r.X + S(3);
        using var k = new SolidBrush(Color.White);
        g.FillEllipse(k, x, r.Y + S(3), d, d);
    }
    protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }
}

/// <summary>Knopf – gefüllt (primär) oder mit Rahmen (sekundär).</summary>
sealed class PillButton : Painted
{
    public bool Primary;
    public Color Accent = Theme.Accent;
    public string IconName;
    public PillButton(string text, bool primary) { Text = text; Primary = primary; Cursor = Cursors.Hand; Size = new Size(150, 40); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prep(e);
        var r = new RectangleF(0.75f, 0.75f, Width - 2f, Height - 2f);
        using var path = Theme.Round(r, S(9));
        if (!Enabled)
        {
            using var b = new SolidBrush(Theme.TileHi); g.FillPath(b, path);
        }
        else if (Primary)
        {
            using var b = new SolidBrush(Hover ? Theme.Mix(Accent, Color.White, 0.12f) : Accent); g.FillPath(b, path);
        }
        else
        {
            using (var b = new SolidBrush(Hover ? Theme.TileHi : ParentBg)) g.FillPath(b, path);
            using (var p = new Pen(Hover ? Accent : Theme.Line, S(1.2f))) g.DrawPath(p, path);
        }
        var fg = !Enabled ? Theme.Dim : Primary ? Color.White : Theme.Txt;
        using var f = Theme.F(9.75f, true);
        var tsz = TextRenderer.MeasureText(g, Text, f, Size.Empty, TextFormatFlags.NoPadding);
        float iw = IconName != null ? S(16) + S(8) : 0;
        float x = (Width - tsz.Width - iw) / 2;
        if (IconName != null) Icons.Draw(g, IconName, new RectangleF(x, (Height - S(16)) / 2, S(16), S(16)), fg, S(1.8f));
        Theme.Text(g, Text, f, fg, new Rectangle((int)(x + iw), 0, tsz.Width + 4, Height));
    }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
}

/// <summary>Eintrag in der Seitenleiste.</summary>
sealed class NavItem : Painted
{
    public string IconName;
    bool _sel;
    public bool Selected { get => _sel; set { _sel = value; Invalidate(); } }
    public NavItem(string text, string icon) { Text = text; IconName = icon; Cursor = Cursors.Hand; Size = new Size(188, 42); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prep(e);
        if (_sel || Hover)
            using (var path = Theme.Round(new RectangleF(0, 0, Width - 1, Height - 1), S(9)))
            using (var b = new SolidBrush(_sel ? Theme.Tile : Color.FromArgb(14, 255, 255, 255))) g.FillPath(b, path);
        if (_sel) using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, 0, Height * 0.28f, S(3), Height * 0.44f);
        var col = _sel ? Theme.Txt : Theme.Mut;
        Icons.Draw(g, IconName, new RectangleF(S(16), (Height - S(18)) / 2, S(18), S(18)), _sel ? Theme.Accent : Theme.Mut, S(1.7f));
        using var f = Theme.F(10f, _sel);
        Theme.Text(g, Text, f, col, new Rectangle((int)S(46), 0, Width - (int)S(50), Height));
    }
}

/// <summary>Fenster-Knöpfe (Minimieren / Schließen).</summary>
sealed class WinButton : Painted
{
    readonly string _icon; readonly bool _close;
    public WinButton(string icon, bool close) { _icon = icon; _close = close; Size = new Size(46, 34); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prep(e);
        if (Hover) using (var b = new SolidBrush(_close ? Color.FromArgb(232, 17, 35) : Theme.TileHi)) g.FillRectangle(b, ClientRectangle);
        float s = S(14);
        Icons.Draw(g, _icon, new RectangleF((Width - s) / 2, (Height - s) / 2, s, s), Hover && _close ? Color.White : Theme.Mut, S(1.2f));
    }
}

/// <summary>Zustands-Plakette (z. B. „Getestet“).</summary>
sealed class Badge : Painted
{
    public Color Accent = Theme.Good;
    public Badge() { Size = new Size(110, 24); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prep(e);
        using (var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), (Height - 1) / 2f))
        using (var b = new SolidBrush(Theme.Mix(ParentBg, Accent, 0.18f))) g.FillPath(b, path);
        float d = S(7);
        using (var b = new SolidBrush(Accent)) g.FillEllipse(b, S(10), (Height - d) / 2, d, d);
        using var f = Theme.F(8.5f, true);
        Theme.Text(g, Text, f, Accent, new Rectangle((int)S(23), 0, Width - (int)S(26), Height));
    }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
}

/// <summary>Einfaches Textfeld ohne Hintergrund-Flackern.</summary>
sealed class TextLabel : Painted
{
    public Font TextFont = Theme.F(9.5f);
    public bool Wrap = true;
    public TextFormatFlags Align = TextFormatFlags.Left;
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prep(e);
        TextRenderer.DrawText(g, Text, TextFont, ClientRectangle, ForeColor, Align | TextFormatFlags.NoPadding | (Wrap ? TextFormatFlags.WordBreak : TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter));
    }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
    protected override void OnForeColorChanged(EventArgs e) { Invalidate(); base.OnForeColorChanged(e); }
}

// =========================================================================
//  Dialoge im App-Design
// =========================================================================
class StyledDialog : Form
{
    protected readonly Tile Body;
    public StyledDialog(string title, string text, Color accent, string icon, int height = 220)
    {
        FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
        BackColor = Theme.Line; ClientSize = new Size(460, height); Padding = new Padding(1); TopMost = true;
        Font = Theme.F(10f);
        Body = new Tile { Dock = DockStyle.Fill, Border = Theme.Line };
        Controls.Add(Body);
        var ic = new IconBox { IconName = icon, Accent = accent, Bounds = new Rectangle(22, 22, 40, 40) };
        var t = new TextLabel { Text = title, TextFont = Theme.F(12.5f, true), Wrap = false, Bounds = new Rectangle(76, 22, 360, 40) };
        var m = new TextLabel { Text = text, ForeColor = Theme.Mut, Bounds = new Rectangle(76, 66, 360, height - 140) };
        Body.Controls.AddRange(new Control[] { ic, t, m });
        Body.MouseDown += Drag; t.MouseDown += Drag;
    }
    void Drag(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Native.ReleaseCapture(); Native.SendMessage(Handle, 0xA1, 2, 0); } }
    protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; return cp; } }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Native.RoundCorners(Handle); }
}

sealed class IconBox : Painted
{
    public string IconName; public Color Accent;
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Prep(e);
        using (var path = Theme.Round(new RectangleF(0, 0, Width - 1, Height - 1), S(10)))
        using (var b = new SolidBrush(Theme.Mix(Theme.Tile, Accent, 0.2f))) g.FillPath(b, path);
        Icons.Draw(g, IconName, RectangleF.Inflate(new RectangleF(0, 0, Width, Height), -S(10), -S(10)), Accent, S(1.8f));
    }
}

sealed class NoticeDialog : StyledDialog
{
    public NoticeDialog(string title, string text, Color accent, string icon = "shield") : base(title, text, accent, icon, 240)
    {
        var ok = new PillButton("OK", true) { Accent = accent, Bounds = new Rectangle(306, 186, 130, 38) };
        ok.Click += (_, _) => Close();
        Body.Controls.Add(ok);
        AcceptButton = null;
        KeyPreview = true; KeyDown += (_, e) => { if (e.KeyCode is Keys.Enter or Keys.Escape) Close(); };
    }
    public static void Show(IWin32Window owner, string title, string text, Color accent, string icon = "shield")
    {
        using var d = new NoticeDialog(title, text, accent, icon);
        d.ShowDialog(owner);
    }
}

/// <summary>"Behalten?"-Abfrage mit Countdown – ohne Klick wird automatisch zurückgesetzt.</summary>
sealed class ConfirmDialog : StyledDialog
{
    int _left = 15;
    public ConfirmDialog() : base("Einstellungen behalten?", "Die neuen Werte sind aktiv. Läuft alles normal, ohne Flackern oder Ruckeln?", Theme.Accent, "shield", 230)
    {
        var c = new TextLabel { ForeColor = Theme.Warn, Wrap = false, Bounds = new Rectangle(76, 120, 360, 22) };
        var yes = new PillButton("Behalten", true) { Bounds = new Rectangle(296, 168, 140, 38) };
        var no = new PillButton("Zurücksetzen", false) { Bounds = new Rectangle(146, 168, 140, 38) };
        yes.Click += (_, _) => { DialogResult = DialogResult.Yes; Close(); };
        no.Click += (_, _) => { DialogResult = DialogResult.No; Close(); };
        Body.Controls.AddRange(new Control[] { c, yes, no });
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { DialogResult = DialogResult.Yes; Close(); } if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.No; Close(); } };
        var t = new System.Windows.Forms.Timer { Interval = 1000 };
        void Upd() => c.Text = $"Automatisch zurück in {_left} s";
        Upd();
        t.Tick += (_, _) => { _left--; Upd(); if (_left <= 0) { t.Stop(); DialogResult = DialogResult.No; Close(); } };
        Shown += (_, _) => t.Start();
        FormClosed += (_, _) => t.Dispose();
    }
}

static class Native
{
    [DllImport("user32.dll")] public static extern bool ReleaseCapture();
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, int w, int l);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
    public static void RoundCorners(IntPtr h)
    {
        try { int v = 2; DwmSetWindowAttribute(h, 33, ref v, 4); } catch { }   // Windows 11: abgerundete Ecken
        try { int d = 1; DwmSetWindowAttribute(h, 20, ref d, 4); } catch { }   // dunkler Modus
    }
}
