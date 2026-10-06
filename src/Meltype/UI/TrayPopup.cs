// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Meltype.Config;

namespace Meltype.UI;

/// <summary>トレイのパネルに出す今の状態。</summary>
internal sealed record TrayPopupState(
    bool Enabled,
    InputMode Mode,
    bool Direct,
    DetectionLevel Level,
    string Profile,
    IReadOnlyList<string> Profiles,
    string Status,
    string? UpdateVersion);

/// <summary>トレイのパネルのボタンを押したときにすること。</summary>
internal sealed class TrayPopupActions
{
    public Action<bool> SetEnabled { get; init; } = _ => { };
    public Action<InputMode> SetMode { get; init; } = _ => { };
    public Action<DetectionLevel> SetLevel { get; init; } = _ => { };
    public Action<string> SetProfile { get; init; } = _ => { };
    public Action Settings { get; init; } = () => { };
    public Action Dictionary { get; init; } = () => { };
    public Action Log { get; init; } = () => { };
    public Action Learned { get; init; } = () => { };
    public Action Update { get; init; } = () => { };
    public Action Exit { get; init; } = () => { };
    /// <summary>「その他」: 今までのメニュー (全部の項目) を出す。</summary>
    public Action<Point> More { get; init; } = _ => { };
}

/// <summary>
/// タスクトレイのアイコンをクリックしたときに出るパネル。
/// 上に 動作中かどうか (スイッチ)、その下に 入力方法・判定の強さ・プロファイル の切り替え、よく使う画面へのボタン。
/// 項目の多いメニューは「その他」から出す。ほかの場所をクリックすると閉じる。
/// </summary>
internal sealed class TrayPopup : Form
{
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private readonly Func<TrayPopupState> _state;
    private readonly TrayPopupActions _actions;
    private readonly FlowLayoutPanel _stack = new()
    {
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        BackColor = Theme.Card,
        Margin = Padding.Empty,
        Padding = new Padding(1, 1, 1, 1),
        Location = Point.Empty,
    };
    private bool _building;
    private long _hiddenAt;

    public TrayPopup(Func<TrayPopupState> state, TrayPopupActions actions)
    {
        _state = state;
        _actions = actions;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Theme.Card;
        Font = Theme.Body;
        ForeColor = Theme.Ink;
        DoubleBuffered = true;
        KeyPreview = true;
        Text = "Meltype";
        Controls.Add(_stack);
        Deactivate += (_, _) => HidePopup();
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) HidePopup();
        };
        Rebuild();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // タスクバーと Alt+Tab に出さない
            cp.ExStyle |= WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    /// <summary>今の幅 (高 DPI では大きくする)。</summary>
    private int PanelWidth => (int)(340 * DeviceDpi / 96F);

    /// <summary>
    /// アイコンの近くに出す。出ているときにアイコンを押したら閉じる
    /// (アイコンを押した時点でパネルの外のクリックとして閉じているので、閉じた直後なら出し直さない)。
    /// </summary>
    public void Toggle()
    {
        if (Visible)
        {
            HidePopup();
            return;
        }
        if (Environment.TickCount64 - _hiddenAt < 250) return;
        Rebuild();
        PlaceNearTray();
        Show();
        Activate();
        SetForegroundWindow(Handle);
        _stack.Controls.OfType<Control>().FirstOrDefault()?.Focus();
    }

    private void HidePopup()
    {
        if (!Visible) return;
        _hiddenAt = Environment.TickCount64;
        Hide();
    }

    /// <summary>タスクバーの位置 (下・上・左・右) に合わせて、画面の角に寄せて出す。</summary>
    private void PlaceNearTray()
    {
        var cursor = Cursor.Position;
        var screen = Screen.FromPoint(cursor);
        var area = screen.WorkingArea;
        var bounds = screen.Bounds;
        var gap = (int)(10 * DeviceDpi / 96F);
        int x = cursor.X - Width / 2, y = cursor.Y - Height - gap;
        if (area.Bottom < bounds.Bottom) y = area.Bottom - Height - gap; // タスクバーが下
        else if (area.Top > bounds.Top) y = area.Top + gap; // 上
        else if (area.Left > bounds.Left) x = area.Left + gap; // 左
        else if (area.Right < bounds.Right) x = area.Right - Width - gap; // 右
        x = Math.Clamp(x, area.Left + gap, Math.Max(area.Left + gap, area.Right - Width - gap));
        y = Math.Clamp(y, area.Top + gap, Math.Max(area.Top + gap, area.Bottom - Height - gap));
        Location = new Point(x, y);
    }

    /// <summary>今の状態で中身を作り直す (開くたびに呼ぶ)。</summary>
    public void Rebuild()
    {
        _building = true;
        SuspendLayout();
        _stack.SuspendLayout();
        foreach (Control c in _stack.Controls.Cast<Control>().ToList()) c.Dispose();
        _stack.Controls.Clear();
        var state = _state();
        var width = PanelWidth - 2;
        var scale = DeviceDpi / 96F;
        var pad = (int)(16 * scale);

        // 上: 状態とスイッチ
        _stack.Controls.Add(Header(state, width, pad));

        // 切り替え: 入力方法・判定の強さ・プロファイル
        var options = new Panel { Width = width, BackColor = Theme.Card, Margin = Padding.Empty };
        var y = 0;
        y = AddChoice(options, y, width, pad, "入力方法", ["キーボード", "IME 自動切替"], state.Mode == InputMode.Keyboard ? 0 : 1,
            i => _actions.SetMode(i == 0 ? InputMode.Keyboard : InputMode.AutoSwitch));
        y = AddChoice(options, y, width, pad, "自動判定の強さ", ["積極的", "標準", "慎重", "手動"], (int)state.Level,
            i => _actions.SetLevel(Enum.GetValues<DetectionLevel>()[i]));
        if (state.Profiles.Count is > 1 and <= 3 && state.Profiles.All(p => p.Length <= 8))
        {
            y = AddChoice(options, y, width, pad, "プロファイル", state.Profiles, Math.Max(0, state.Profiles.ToList().IndexOf(state.Profile)),
                i => _actions.SetProfile(state.Profiles[i]));
        }
        else if (state.Profiles.Count > 3 || state.Profiles.Any(p => p.Length > 8))
        {
            y = AddProfileDropDown(options, y, width, pad, state);
        }
        options.Height = y + (int)(6 * scale);
        _stack.Controls.Add(options);

        // よく使う画面
        _stack.Controls.Add(Divider(width));
        var actions = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Theme.Card, Margin = Padding.Empty, Padding = new Padding((int)(6 * scale)) };
        var rowWidth = width - (int)(12 * scale);
        actions.Controls.Add(new ActionRow(Glyph.Settings, "設定", rowWidth, Run(_actions.Settings)));
        actions.Controls.Add(new ActionRow(Glyph.Dictionary, "ユーザー辞書", rowWidth, Run(_actions.Dictionary)));
        actions.Controls.Add(new ActionRow(Glyph.Log, "ログ / 判定理由", rowWidth, Run(_actions.Log)));
        actions.Controls.Add(new ActionRow(Glyph.Learned, "学習した語", rowWidth, Run(_actions.Learned)));
        if (state.UpdateVersion is { } version)
        {
            actions.Controls.Add(new ActionRow(Glyph.Update, $"Meltype {version} に更新して再起動", rowWidth, Run(_actions.Update)) { Accent = true });
        }
        _stack.Controls.Add(actions);

        // 下: その他 と 終了
        _stack.Controls.Add(Footer(width, pad));

        _stack.ResumeLayout(true);
        ResumeLayout(true);
        ClientSize = new Size(PanelWidth, _stack.PreferredSize.Height);
        ApplyShape();
        _building = false;
    }

    /// <summary>ボタンを押したらパネルを閉じてから実行する (開いた画面がパネルの後ろに隠れないように)。</summary>
    private Action Run(Action action) => () =>
    {
        HidePopup();
        action();
    };

    private Control Header(TrayPopupState state, int width, int pad)
    {
        var scale = DeviceDpi / 96F;
        var height = (int)(76 * scale);
        var header = new Panel { Width = width, Height = height, BackColor = Theme.Card, Margin = Padding.Empty };
        var badgeSize = (int)(42 * scale);
        var badge = new Badge(state.Enabled ? state.Mode == InputMode.Keyboard && state.Direct ? "A" : "あ" : "A",
            !state.Enabled ? Theme.Paused : state.Mode == InputMode.Keyboard && state.Direct ? Theme.English : Theme.Japanese)
        {
            Bounds = new Rectangle(pad, (height - badgeSize) / 2, badgeSize, badgeSize),
        };
        var toggle = new ToggleSwitch { Checked = state.Enabled, AccessibleName = "Meltype を有効にする" };
        toggle.Location = new Point(width - pad - toggle.Width, (height - toggle.Height) / 2);
        toggle.CheckedChanged += (_, _) =>
        {
            if (_building) return;
            _actions.SetEnabled(toggle.Checked);
            // スイッチ自身のイベントの中で作り直すと、押したスイッチを消してしまうので後で
            BeginInvoke(new Action(Rebuild));
        };
        var textLeft = badge.Right + (int)(12 * scale);
        var textWidth = toggle.Left - textLeft - (int)(8 * scale);
        var title = new Label
        {
            Text = state.Enabled ? "Meltype は動作中" : "Meltype は一時停止中",
            Font = Theme.Section,
            ForeColor = Theme.Ink,
            BackColor = Theme.Card,
            AutoEllipsis = true,
            UseMnemonic = false,
            Bounds = new Rectangle(textLeft, height / 2 - (int)(22 * scale), textWidth, (int)(24 * scale)),
        };
        var sub = new Label
        {
            Text = state.Status,
            Font = Theme.Small,
            ForeColor = Theme.Muted,
            BackColor = Theme.Card,
            AutoEllipsis = true,
            UseMnemonic = false,
            Bounds = new Rectangle(textLeft, height / 2 + (int)(3 * scale), textWidth, (int)(20 * scale)),
        };
        header.Controls.AddRange([badge, title, sub, toggle]);
        return header;
    }

    private int AddChoice(Panel panel, int y, int width, int pad, string label, IReadOnlyList<string> items, int selected, Action<int> picked)
    {
        var scale = DeviceDpi / 96F;
        var caption = new Label { Text = label, Font = Theme.Small, ForeColor = Theme.Muted, BackColor = Theme.Card, AutoSize = true, Location = new Point(pad, y + (int)(6 * scale)) };
        var segments = new SegmentedControl(items)
        {
            AccessibleName = label,
            Bounds = new Rectangle(pad, caption.Bottom + (int)(4 * scale), width - pad * 2, (int)(34 * scale)),
            SelectedIndex = selected,
        };
        segments.SelectedIndexChanged += (_, _) =>
        {
            if (_building || segments.SelectedIndex < 0) return;
            picked(segments.SelectedIndex);
            // 選んだ結果 (状態の文字など) を出し直す。フォーカスはそのまま
            BeginInvoke(new Action(Rebuild));
        };
        panel.Controls.Add(caption);
        panel.Controls.Add(segments);
        return segments.Bottom + (int)(4 * scale);
    }

    private int AddProfileDropDown(Panel panel, int y, int width, int pad, TrayPopupState state)
    {
        var scale = DeviceDpi / 96F;
        var caption = new Label { Text = "プロファイル", Font = Theme.Small, ForeColor = Theme.Muted, BackColor = Theme.Card, AutoSize = true, Location = new Point(pad, y + (int)(6 * scale)) };
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, Font = Theme.Body, AccessibleName = "プロファイル" };
        combo.Items.AddRange(state.Profiles.Cast<object>().ToArray());
        combo.SelectedItem = state.Profile;
        combo.Bounds = new Rectangle(pad, caption.Bottom + (int)(4 * scale), width - pad * 2, combo.Height);
        combo.SelectionChangeCommitted += (_, _) =>
        {
            if (combo.SelectedItem is string name) _actions.SetProfile(name);
        };
        panel.Controls.Add(caption);
        panel.Controls.Add(combo);
        return combo.Bottom + (int)(4 * scale);
    }

    private Control Divider(int width)
    {
        var line = new Panel { Width = width, Height = 1, BackColor = Theme.Divider, Margin = Padding.Empty };
        return line;
    }

    private Control Footer(int width, int pad)
    {
        var scale = DeviceDpi / 96F;
        var height = (int)(48 * scale);
        var footer = new Panel { Width = width, Height = height, BackColor = Theme.Footer, Margin = Padding.Empty };
        footer.Paint += (_, e) =>
        {
            using var line = new Pen(Theme.Border);
            e.Graphics.DrawLine(line, 0, 0, footer.Width, 0);
        };
        var more = new Button { Text = "その他…", AutoSize = true };
        Theme.StyleButton(more);
        more.BackColor = Theme.Footer;
        more.MinimumSize = new Size(0, (int)(32 * scale));
        var exit = new Button { Text = "終了", AutoSize = true };
        Theme.StyleButton(exit);
        exit.BackColor = Theme.Footer;
        exit.ForeColor = Theme.JapaneseText;
        exit.MinimumSize = new Size(0, (int)(32 * scale));
        var version = new Label { Text = $"v{AppInfo.Version}", Font = Theme.Small, ForeColor = Theme.Muted, BackColor = Theme.Footer, AutoSize = true };
        footer.Controls.AddRange([version, more, exit]);
        footer.Layout += (_, _) =>
        {
            exit.Location = new Point(footer.Width - pad + (int)(6 * scale) - exit.Width, (height - exit.Height) / 2);
            more.Location = new Point(exit.Left - (int)(6 * scale) - more.Width, (height - more.Height) / 2);
            version.Location = new Point(pad, (height - version.Height) / 2);
        };
        more.Click += (_, _) =>
        {
            var at = more.PointToScreen(new Point(0, 0));
            HidePopup();
            _actions.More(at);
        };
        exit.Click += (_, _) =>
        {
            HidePopup();
            _actions.Exit();
        };
        return footer;
    }

    /// <summary>窓を角の丸い形にし、縁を描く。</summary>
    private void ApplyShape()
    {
        if (Width <= 0 || Height <= 0) return;
        var radius = 12 * DeviceDpi / 96F;
        using var path = Theme.RoundRect(new RectangleF(0, 0, Width, Height), radius);
        var old = Region;
        Region = new Region(path);
        old?.Dispose();
        Invalidate(true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.RoundRect(new RectangleF(0.5F, 0.5F, Width - 1.5F, Height - 1.5F), 12 * DeviceDpi / 96F);
        using var edge = new Pen(Theme.BorderStrong);
        e.Graphics.DrawPath(edge, path);
    }

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);

    /// <summary>アイコン用の文字 (Windows の記号のフォント)。無いときは文字を出さない。</summary>
    private static class Glyph
    {
        public const string Settings = "";
        public const string Dictionary = "";
        public const string Log = "";
        public const string Learned = "";
        public const string Update = "";

        private static readonly Lazy<Font?> _font = new(() =>
        {
            foreach (var name in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
            {
                if (FontFamily.Families.Any(f => f.Name == name)) return new Font(name, 11F);
            }
            return null;
        });

        public static Font? Font => _font.Value;
    }

    /// <summary>モードの札 (角の丸い四角に「あ」/「A」)。</summary>
    private sealed class Badge : Control
    {
        private readonly string _text;
        private readonly Color _color;
        private readonly Font _font = new(Theme.FontName, 15F, FontStyle.Bold);

        public Badge(string text, Color color)
        {
            _text = text;
            _color = color;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Theme.Card;
            AccessibleRole = AccessibleRole.Graphic;
            AccessibleName = text == "あ" ? "日本語" : "英字";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = Theme.RoundRect(new RectangleF(0, 0, Width - 1, Height - 1), 10 * DeviceDpi / 96F);
            using var brush = new SolidBrush(_color);
            g.FillPath(brush, path);
            TextRenderer.DrawText(g, _text, _font, ClientRectangle, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _font.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>パネルの 1 行のボタン (左に記号、右に名前)。</summary>
    private sealed class ActionRow : Button
    {
        private readonly string _glyph;
        private bool _hover;

        public ActionRow(string glyph, string text, int width, Action click)
        {
            _glyph = glyph;
            Text = text;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Theme.Card;
            Font = Theme.Body;
            Cursor = Cursors.Hand;
            Margin = Padding.Empty;
            Size = new Size(width, (int)(38 * DeviceDpi / 96F));
            Click += (_, _) => click();
        }

        /// <summary>目立たせる行 (更新など)。</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Accent { get; set; }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var scale = DeviceDpi / 96F;
            g.Clear(Theme.Card);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (_hover || Focused)
            {
                using var path = Theme.RoundRect(new RectangleF(0, 0, Width - 1, Height - 1), 7 * scale);
                using var fill = new SolidBrush(Theme.Hex(0xF4F2EC));
                g.FillPath(fill, path);
            }
            var color = Accent ? Theme.JapaneseText : Theme.Ink;
            var iconBox = new Rectangle((int)(10 * scale), 0, (int)(24 * scale), Height);
            if (Glyph.Font is { } font)
            {
                TextRenderer.DrawText(g, _glyph, font, iconBox, Accent ? Theme.Japanese : Theme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            var textLeft = iconBox.Right + (int)(10 * scale);
            TextRenderer.DrawText(g, Text, Accent ? Theme.BodyBold : Theme.Body, new Rectangle(textLeft, 0, Width - textLeft - (int)(8 * scale), Height), color,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues)
            {
                using var focus = new Pen(Theme.Ink, 1.5F) { DashStyle = DashStyle.Dot };
                using var ring = Theme.RoundRect(new RectangleF(1, 1, Width - 3, Height - 3), 7 * scale);
                g.DrawPath(focus, ring);
            }
        }
    }
}
