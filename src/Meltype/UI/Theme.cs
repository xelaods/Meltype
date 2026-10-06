// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Drawing.Drawing2D;

namespace Meltype.UI;

/// <summary>
/// 画面の色と文字の決まり。日本語 (かなにする) はオレンジ、英字のままは青、で全部の画面をそろえる。
/// </summary>
internal static class Theme
{
    public const string FontName = "Yu Gothic UI";

    // 地と文字
    public static readonly Color Canvas = Hex(0xFBFAF7);
    public static readonly Color Sidebar = Hex(0xF3F1EB);
    public static readonly Color Card = Color.White;
    public static readonly Color Footer = Hex(0xF7F5F0);
    public static readonly Color Border = Hex(0xE4E0D6);
    public static readonly Color BorderStrong = Hex(0xCFCAC0);
    public static readonly Color Divider = Hex(0xEEEBE3);
    public static readonly Color Ink = Hex(0x17181C);
    public static readonly Color Muted = Hex(0x5E5F66);
    public static readonly Color Faint = Hex(0x8C887F);

    // 日本語 (かなにする)
    public static readonly Color Japanese = Hex(0xD0602A);
    public static readonly Color JapaneseText = Hex(0x8A3412);
    public static readonly Color JapaneseSoft = Hex(0xFBE7DC);

    // 英字のまま
    public static readonly Color English = Hex(0x2F6FD6);
    public static readonly Color EnglishText = Hex(0x1D4F9E);
    public static readonly Color EnglishSoft = Hex(0xE1EBFA);

    // 部品
    public static readonly Color SwitchOff = Hex(0xC9C4B9);
    public static readonly Color SegmentBack = Hex(0xF1EEE7);
    public static readonly Color Paused = Hex(0x6E6A72);

    public static readonly Font Body = new(FontName, 9.5F);
    public static readonly Font BodyBold = new(FontName, 9.5F, FontStyle.Bold);
    public static readonly Font Small = new(FontName, 8.5F);
    public static readonly Font Section = new(FontName, 12F, FontStyle.Bold);
    public static readonly Font Title = new(FontName, 15F, FontStyle.Bold);
    public static readonly Font Mono = new("Consolas", 10F);

    public static Color Hex(int rgb) => Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

    /// <summary>角の丸い四角。</summary>
    public static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        var d = Math.Max(0.1F, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>ボタンを平らな見た目にする。primary は濃い地に白い文字 (OK など)、ほかは白地に枠。</summary>
    public static void StyleButton(Button button, bool primary = false)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.Font = primary ? BodyBold : Body;
        button.Cursor = Cursors.Hand;
        button.MinimumSize = new Size(0, 34);
        button.Padding = new Padding(10, 0, 10, 0);
        if (primary)
        {
            button.BackColor = Ink;
            button.ForeColor = Canvas;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Hex(0x33343A);
            button.FlatAppearance.MouseDownBackColor = Hex(0x000000);
        }
        else
        {
            button.BackColor = Card;
            button.ForeColor = Ink;
            button.FlatAppearance.BorderColor = BorderStrong;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = Hex(0xF4F2EC);
            button.FlatAppearance.MouseDownBackColor = Hex(0xEAE6DD);
        }
    }

    /// <summary>メニューのボタン (サイドバーの項目など): 枠なし、左寄せ。</summary>
    public static void StyleNavButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Hex(0xEAE7DF);
        button.FlatAppearance.MouseDownBackColor = Hex(0xE2DED4);
        button.BackColor = Sidebar;
        button.ForeColor = Hex(0x3F4047);
        button.TextAlign = ContentAlignment.MiddleLeft;
        button.Font = Body;
        button.Cursor = Cursors.Hand;
        button.Height = 36;
        button.Padding = new Padding(8, 0, 0, 0);
    }

    /// <summary>右クリックのメニュー (トレイなど) をこの見た目で描く。</summary>
    public static ToolStripRenderer MenuRenderer { get; } = new ToolStripProfessionalRenderer(new MenuColors()) { RoundedEdges = false };

    private sealed class MenuColors : ProfessionalColorTable
    {
        public MenuColors() => UseSystemColors = false;
        public override Color ToolStripDropDownBackground => Card;
        public override Color ImageMarginGradientBegin => Card;
        public override Color ImageMarginGradientMiddle => Card;
        public override Color ImageMarginGradientEnd => Card;
        public override Color MenuBorder => BorderStrong;
        public override Color MenuItemBorder => Hex(0xEAE6DD);
        public override Color MenuItemSelected => Hex(0xF4F2EC);
        public override Color MenuItemSelectedGradientBegin => Hex(0xF4F2EC);
        public override Color MenuItemSelectedGradientEnd => Hex(0xF4F2EC);
        public override Color MenuItemPressedGradientBegin => Hex(0xEAE6DD);
        public override Color MenuItemPressedGradientEnd => Hex(0xEAE6DD);
        public override Color SeparatorDark => Divider;
        public override Color SeparatorLight => Card;
        public override Color CheckBackground => JapaneseSoft;
        public override Color CheckSelectedBackground => JapaneseSoft;
        public override Color CheckPressedBackground => JapaneseSoft;
        public override Color ButtonSelectedBorder => Hex(0xEAE6DD);
    }
}

/// <summary>白い地に角の丸い枠を描く入れ物 (設定の各カード)。中の部品の地の色は Card にする。</summary>
internal sealed class CardPanel : Panel
{
    public CardPanel()
    {
        DoubleBuffered = true;
        BackColor = Theme.Canvas;
        Padding = new Padding(1);
        ResizeRedraw = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.RoundRect(new RectangleF(0.5F, 0.5F, Width - 1.5F, Height - 1.5F), 10 * DeviceDpi / 96F);
        using var fill = new SolidBrush(Theme.Card);
        using var edge = new Pen(Theme.Border);
        g.FillPath(fill, path);
        g.DrawPath(edge, path);
    }
}

/// <summary>ON / OFF のスイッチ。中身は CheckBox なので、Tab で選べて Space で切り替えられる。</summary>
internal sealed class ToggleSwitch : CheckBox
{
    public ToggleSwitch()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoSize = false;
        Cursor = Cursors.Hand;
        Text = "";
        Size = new Size(48, 28);
        Anchor = AnchorStyles.Top | AnchorStyles.Left;
        Margin = new Padding(12, 6, 4, 6);
    }

    /// <summary>オンのときの色 (既定はオレンジ)。</summary>
    public Color OnColor { get; set; } = Theme.Japanese;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Card);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = DeviceDpi / 96F;
        var track = new RectangleF(1, (Height - 26 * scale) / 2, Math.Min(Width - 2, 46 * scale), 26 * scale);
        using (var path = Theme.RoundRect(track, track.Height / 2))
        using (var brush = new SolidBrush(!Enabled ? Theme.Divider : Checked ? OnColor : Theme.SwitchOff))
        {
            g.FillPath(brush, path);
        }
        var knob = track.Height - 6 * scale;
        var x = Checked ? track.Right - knob - 3 * scale : track.X + 3 * scale;
        using (var shadow = new SolidBrush(Color.FromArgb(40, 0, 0, 0))) g.FillEllipse(shadow, x, track.Y + 3 * scale + 1, knob, knob);
        g.FillEllipse(Brushes.White, x, track.Y + 3 * scale, knob, knob);
        if (Focused && ShowFocusCues)
        {
            using var focus = new Pen(Theme.Ink, 1.5F) { DashStyle = DashStyle.Dot };
            using var ring = Theme.RoundRect(new RectangleF(track.X - 1, track.Y - 1, track.Width + 1, track.Height + 1), (track.Height + 2) / 2);
            g.DrawPath(focus, ring);
        }
    }

    protected override void OnCheckedChanged(EventArgs e)
    {
        base.OnCheckedChanged(e);
        AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
        Invalidate();
    }
}

/// <summary>
/// 横に並んだ選択肢から 1 つを選ぶ部品 (「積極的 / 標準 / 慎重 / 手動」など)。
/// ←→ キーで選び直せる。選択肢が少ない列挙型に使う。
/// </summary>
internal sealed class SegmentedControl : Control
{
    private readonly List<string> _items = [];
    private int _selected = -1;
    private int _hover = -1;

    public SegmentedControl(IEnumerable<string> items)
    {
        _items.AddRange(items);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        Font = Theme.Body;
        Cursor = Cursors.Hand;
        Height = 36;
        AccessibleRole = AccessibleRole.PageTabList;
        Width = PreferredWidth();
    }

    public event EventHandler? SelectedIndexChanged;

    /// <summary>はみ出さずに並べるのに要る幅。</summary>
    public int PreferredWidth()
    {
        var scale = DeviceDpi / 96F;
        var widest = _items.Count == 0 ? 0 : _items.Max(t => TextRenderer.MeasureText(t, Theme.BodyBold).Width);
        return (int)(_items.Count * (widest + 28 * scale) + 8 * scale);
    }

    public int SelectedIndex
    {
        get => _selected;
        set
        {
            value = _items.Count == 0 ? -1 : Math.Clamp(value, 0, _items.Count - 1);
            if (value == _selected) return;
            _selected = value;
            AccessibleName = _selected >= 0 ? _items[_selected] : null;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private RectangleF Segment(int index)
    {
        var pad = 3 * DeviceDpi / 96F;
        var w = (Width - pad * 2) / Math.Max(1, _items.Count);
        return new RectangleF(pad + w * index, pad, w, Height - pad * 2);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Card);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = DeviceDpi / 96F;
        using (var back = Theme.RoundRect(new RectangleF(0, 0, Width - 1, Height - 1), 8 * scale))
        using (var brush = new SolidBrush(Theme.SegmentBack))
        {
            g.FillPath(brush, back);
        }
        for (var i = 0; i < _items.Count; i++)
        {
            var r = Segment(i);
            var selected = i == _selected;
            if (selected || i == _hover)
            {
                using var path = Theme.RoundRect(r, 6 * scale);
                using var fill = new SolidBrush(selected ? Theme.Card : Color.FromArgb(120, 255, 255, 255));
                if (selected)
                {
                    using var shadow = new SolidBrush(Color.FromArgb(30, 0, 0, 0));
                    using var shadowPath = Theme.RoundRect(new RectangleF(r.X, r.Y + 1, r.Width, r.Height), 6 * scale);
                    g.FillPath(shadow, shadowPath);
                }
                g.FillPath(fill, path);
                if (selected)
                {
                    using var line = new Pen(Theme.Japanese, 2 * scale);
                    g.DrawLine(line, r.X + 8 * scale, r.Bottom - 1.5F * scale, r.Right - 8 * scale, r.Bottom - 1.5F * scale);
                }
            }
            TextRenderer.DrawText(g, _items[i], selected ? Theme.BodyBold : Theme.Body, Rectangle.Round(r), selected ? Theme.Ink : Theme.Hex(0x4A4B52),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
        if (Focused && ShowFocusCues && _selected >= 0)
        {
            using var focus = new Pen(Theme.Ink, 1.5F) { DashStyle = DashStyle.Dot };
            using var ring = Theme.RoundRect(Segment(_selected), 6 * scale);
            g.DrawPath(focus, ring);
        }
    }

    private int HitTest(Point p)
    {
        for (var i = 0; i < _items.Count; i++) if (Segment(i).Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hit = HitTest(e.Location);
        if (hit == _hover) return;
        _hover = hit;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button != MouseButtons.Left) return;
        var hit = HitTest(e.Location);
        if (hit >= 0) SelectedIndex = hit;
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Left) SelectedIndex = Math.Max(0, _selected - 1);
        else if (e.KeyCode == Keys.Right) SelectedIndex = _selected + 1;
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }
}
