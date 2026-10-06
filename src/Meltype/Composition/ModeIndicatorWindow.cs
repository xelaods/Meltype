// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Drawing.Drawing2D;
using Meltype.UI;

namespace Meltype.Composition;

/// <summary>
/// 入力モード (あ / A) をカーソルの近くに一瞬だけ出す小さなウィンドウ。フォーカスを奪わない。
/// Meltype キーボードの使用中は Windows の IME を OFF にしているので、タスクバーの IME の表示は常に「A」になる。
/// 今どちらで入力されるかは、入力欄に入ったときと 半角/全角 を押したときにこれで知らせる。
/// 見た目: 濃い色の丸い札に、色の付いた丸 (日本語はオレンジの「あ」、英字は青の「A」) と、モードの名前。
/// </summary>
internal sealed class ModeIndicatorWindow : Form
{
    private const int WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x00000080, WS_EX_TOPMOST = 0x00000008, WS_EX_TRANSPARENT = 0x00000020;
    private static readonly Color Pill = Theme.Ink;
    private readonly Font _badgeFont = new(Theme.FontName, 12F, FontStyle.Bold);
    private readonly Font _labelFont = new(Theme.FontName, 9.5F);
    private readonly System.Windows.Forms.Timer _hideTimer = new() { Interval = 1200 };
    private bool _japanese = true;

    public ModeIndicatorWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        BackColor = Pill;
        Size = new Size(96, 40);
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            Hide();
        };
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // クリックは下のウィンドウへ通す。
            cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_TRANSPARENT;
            return cp;
        }
    }

    private string Badge => _japanese ? "あ" : "A";
    private string ModeName => _japanese ? "日本語" : "英字";

    /// <summary>japanese なら「あ」、そうでなければ「A」を location に出し、しばらくしたら消す。</summary>
    public void Flash(bool japanese, Point location)
    {
        _japanese = japanese;
        // 大きさは文字に合わせる (高 DPI でも見切れないように)
        var scale = DeviceDpi / 96F;
        var labelWidth = TextRenderer.MeasureText(ModeName, _labelFont).Width;
        var height = (int)(40 * scale);
        Size = new Size((int)(6 * scale) + (height - (int)(12 * scale)) + (int)(8 * scale) + labelWidth + (int)(12 * scale), height);
        ApplyRoundRegion();
        var screen = Screen.FromPoint(location).WorkingArea;
        location.X = Math.Clamp(location.X, screen.Left, screen.Right - Width);
        location.Y = Math.Clamp(location.Y, screen.Top, screen.Bottom - Height);
        Location = location;
        if (!Visible) Show();
        Invalidate();
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    /// <summary>窓の形を角の丸い札にする。</summary>
    private void ApplyRoundRegion()
    {
        using var path = Theme.RoundRect(new RectangleF(0, 0, Width, Height), Height / 2F);
        var old = Region;
        Region = new Region(path);
        old?.Dispose();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = DeviceDpi / 96F;
        var inset = 6 * scale;
        var diameter = Height - inset * 2;
        var badge = new RectangleF(inset, inset, diameter, diameter);
        using (var brush = new SolidBrush(_japanese ? Theme.Japanese : Theme.Hex(0x6E9BE0))) g.FillEllipse(brush, badge);
        TextRenderer.DrawText(g, Badge, _badgeFont, Rectangle.Round(badge), Theme.Ink,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        var labelLeft = (int)(badge.Right + 8 * scale);
        TextRenderer.DrawText(g, ModeName, _labelFont, new Rectangle(labelLeft, 0, Width - labelLeft, Height), Theme.Canvas,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hideTimer.Dispose();
            _badgeFont.Dispose();
            _labelFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
