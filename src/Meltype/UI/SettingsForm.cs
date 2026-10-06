// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.ComponentModel;
using System.Reflection;
using Meltype.Config;

namespace Meltype.UI;

/// <summary>
/// 設定画面。左に プロファイル と 分類の一覧、右に 分類ごとのカードを縦に並べる。
/// 各項目は 名前・説明・部品 の 1 行で、部品は種類に合わせる:
///   ON/OFF → スイッチ、選択肢が少ない列挙型 → 横並びの選択、多い列挙型 → プルダウン、数値 → 数値入力、表 → 説明の下に幅いっぱい。
/// 項目名・分類・説明は Settings の属性 (DisplayName / Category / Description) から取る。
/// いちばん下の「判定テスト」では、打った英字が IME 自動切替でどう判定されるかと理由を確認できる。
/// </summary>
internal sealed class SettingsForm : Form
{
    private const string On = "ON";
    private const string Off = "OFF";

    private readonly MeltypeEngine _engine;
    private readonly List<Binding> _bindings = [];
    private readonly TextBox _testInput = new() { Dock = DockStyle.Top, ImeMode = ImeMode.Disable, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Mono, BackColor = Theme.Canvas };
    private readonly TextBox _testResult = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Mono, BackColor = Theme.Canvas, ForeColor = Theme.Ink };
    private readonly ToolTip _toolTip = new() { AutoPopDelay = 20000 };
    // プロファイル (仕事用・趣味用・SNS 用など) を選ぶ欄
    private readonly ComboBox _profiles = new() { DropDownStyle = ComboBoxStyle.DropDownList, Font = Theme.Body };
    private bool _loadingProfiles;

    // 右側: 分類ごとの見出しとカード。大きさは LayoutBody で決める。
    private readonly Panel _body = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Canvas };
    private readonly List<(Label Header, CardPanel Card, List<SettingRow> Rows, Button Nav)> _sections = [];
    private Button? _activeNav;

    /// <summary>画面の部品に出していない値 (プロファイルの一覧・使っているプロファイル など) を持つ設定。</summary>
    private Settings _draft;

    /// <summary>1 項目分の部品と、設定値との受け渡し。</summary>
    private sealed record Binding(PropertyInfo Property, Control Control, Action<Settings> Load, Action<Settings> Store);

    public SettingsForm(MeltypeEngine engine)
    {
        _engine = engine;
        _draft = engine.Settings.Clone().Normalize();
        Text = "Meltype の設定";
        StartPosition = FormStartPosition.CenterScreen;
        // 画面に収まる大きさにする (中身はスクロールできる)。
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Size = new Size(Math.Min(980, area.Width - 40), Math.Min(760, area.Height - 60));
        MinimumSize = new Size(640, 460);
        Font = Theme.Body;
        BackColor = Theme.Canvas;
        ForeColor = Theme.Ink;

        var sidebar = BuildSidebar();
        BuildSections(sidebar.Nav);

        var ok = new Button { Text = "OK", AutoSize = true, MinimumSize = new Size(96, 34) };
        var cancel = new Button { Text = "キャンセル", AutoSize = true, MinimumSize = new Size(96, 34), DialogResult = DialogResult.Cancel };
        var defaults = new Button { Text = "既定値に戻す", AutoSize = true, MinimumSize = new Size(110, 34) };
        Theme.StyleButton(ok, primary: true);
        Theme.StyleButton(cancel);
        Theme.StyleButton(defaults);
        // OK と × (閉じる) は保存する。変更を捨てるのは キャンセル だけ。
        var discard = false;
        ok.Click += (_, _) => Close();
        cancel.Click += (_, _) =>
        {
            discard = true;
            Close();
        };
        FormClosing += (_, _) =>
        {
            if (discard) return;
            var next = Collect();
            if (next.ToJson() != _engine.Settings.Clone().Normalize().ToJson()) _engine.ApplySettings(next);
        };
        defaults.Click += (_, _) =>
        {
            LoadFrom(new Settings());
            RunTest();
        };
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Theme.Footer, Padding = new Padding(16, 10, 16, 10) };
        footer.Paint += (_, e) =>
        {
            using var line = new Pen(Theme.Border);
            e.Graphics.DrawLine(line, 0, 0, footer.Width, 0);
        };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, WrapContents = false, BackColor = Theme.Footer };
        buttons.Controls.AddRange([ok, cancel, defaults]);
        var note = new Label { Text = "× で閉じても保存されます", AutoSize = true, Dock = DockStyle.Left, ForeColor = Theme.Muted, Font = Theme.Small, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(0, 9, 0, 0) };
        footer.Controls.Add(buttons);
        footer.Controls.Add(note);

        Controls.Add(_body);
        Controls.Add(sidebar.Panel);
        Controls.Add(footer);
        AcceptButton = ok;
        CancelButton = cancel;

        _body.Resize += (_, _) => LayoutBody();
        LoadFrom(_draft);
        RefreshProfiles();
        LayoutBody();
    }

    /// <summary>左側: プロファイルの欄と、分類へ飛ぶボタン。</summary>
    private (Panel Panel, FlowLayoutPanel Nav) BuildSidebar()
    {
        var panel = new Panel { Dock = DockStyle.Left, Width = 220, BackColor = Theme.Sidebar, Padding = new Padding(14, 18, 14, 14) };
        panel.Paint += (_, e) =>
        {
            using var line = new Pen(Theme.Border);
            e.Graphics.DrawLine(line, panel.Width - 1, 0, panel.Width - 1, panel.Height);
        };
        var nav = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Sidebar, Padding = new Padding(0, 14, 0, 0) };
        nav.Resize += (_, _) =>
        {
            foreach (Control c in nav.Controls) c.Width = nav.ClientSize.Width - 2;
        };

        var label = new Label { Text = "プロファイル", Dock = DockStyle.Top, Height = 22, ForeColor = Theme.Muted, Font = Theme.Small, BackColor = Theme.Sidebar };
        var row = new TableLayoutPanel { Dock = DockStyle.Top, Height = _profiles.PreferredHeight + 4, ColumnCount = 2, BackColor = Theme.Sidebar, Margin = Padding.Empty };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, _profiles.PreferredHeight + 6));
        _profiles.Dock = DockStyle.Fill;
        _profiles.Margin = new Padding(0, 2, 6, 0);
        var more = new Button { Text = "…", Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 2), AccessibleName = "プロファイルの操作" };
        Theme.StyleButton(more);
        more.MinimumSize = Size.Empty;
        more.Padding = Padding.Empty;
        row.Controls.Add(_profiles, 0, 0);
        row.Controls.Add(more, 1, 0);
        more.Click += (_, _) => BuildProfileMenu().Show(more, new Point(0, more.Height));
        ShowHelpFor(_profiles, "プロファイル", "仕事用・趣味用・SNS 用など、設定の値をまとめて切り替えられます。トレイのメニューの「プロファイル」からも切り替えられます。Meltype の ON/OFF・ログ・更新の設定は、どのプロファイルでも共通です。「…」の「書き出す...」でファイルにして、ほかの人に渡せます (「読み込む...」で新しいプロファイルとして足せます)。");
        ShowHelpFor(more, "プロファイルの操作", "新規・名前を変更・削除・書き出す・読み込む");
        _profiles.SelectedIndexChanged += (_, _) =>
        {
            if (_loadingProfiles || _profiles.SelectedItem is not string name || name == _draft.ActiveProfile) return;
            // 今の画面の値を今のプロファイルに入れてから、選んだプロファイルの値を読み込む
            _draft = Collect().SwitchProfile(name);
            LoadFrom(_draft);
            RunTest();
        };

        var version = new Label { Text = $"Meltype {AppInfo.Version}", Dock = DockStyle.Bottom, Height = 22, ForeColor = Theme.Muted, Font = Theme.Small, BackColor = Theme.Sidebar };

        panel.Controls.Add(nav);
        panel.Controls.Add(version);
        panel.Controls.Add(row);
        panel.Controls.Add(label);
        return (panel, nav);
    }

    /// <summary>「…」のメニュー: プロファイルの 新規・名前を変更・削除・書き出す・読み込む。</summary>
    private ContextMenuStrip BuildProfileMenu()
    {
        var menu = new ContextMenuStrip { Renderer = Theme.MenuRenderer, Font = Theme.Body };
        menu.Items.Add("新規...", null, (_, _) => AddProfile());
        menu.Items.Add("名前を変更...", null, (_, _) => RenameProfile());
        menu.Items.Add("削除", null, (_, _) => RemoveProfile());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("書き出す...", null, (_, _) => ExportProfile());
        menu.Items.Add("読み込む...", null, (_, _) => ImportProfile());
        menu.Closed += (_, _) => BeginInvoke(new Action(menu.Dispose));
        return menu;
    }

    private void AddProfile()
    {
        if (TextPrompt.Ask(this, "新しいプロファイル", "名前 (例: 仕事用、趣味用、SNS 用)。今の設定を写して作ります。", "") is not { } name) return;
        if (Collect().AddProfile(name) is not { } next)
        {
            MessageBox.Show(this, "名前が空か、同じ名前のプロファイルがあります。", "プロファイル", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        _draft = next;
        RefreshProfiles();
    }

    private void RenameProfile()
    {
        var old = _draft.ActiveProfile;
        if (TextPrompt.Ask(this, "プロファイルの名前を変更", "新しい名前:", old) is not { } name || name == old) return;
        if (Collect().RenameProfile(old, name) is not { } next)
        {
            MessageBox.Show(this, "名前が空か、同じ名前のプロファイルがあります。", "プロファイル", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        _draft = next;
        RefreshProfiles();
    }

    private void RemoveProfile()
    {
        var name = _draft.ActiveProfile;
        if (_draft.Profiles.Count <= 1)
        {
            MessageBox.Show(this, "プロファイルが 1 つだけのときは削除できません。", "プロファイル", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(this, $"プロファイル「{name}」を削除しますか?", "プロファイル", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _draft = Collect().RemoveProfile(name) ?? _draft;
        LoadFrom(_draft);
        RefreshProfiles();
        RunTest();
    }

    // プロファイルを人に渡す: 書き出したファイルを、相手が「読み込む...」で新しいプロファイルとして足す
    private void ExportProfile()
    {
        var draft = Collect();
        using var dialog = new SaveFileDialog
        {
            Title = "プロファイルを書き出す",
            Filter = "Meltype のプロファイル (*.meltype-profile.json)|*.meltype-profile.json|すべてのファイル (*.*)|*.*",
            FileName = $"{string.Concat(draft.ActiveProfile.Split(Path.GetInvalidFileNameChars()))}.meltype-profile.json",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(dialog.FileName, draft.ExportProfile());
            MessageBox.Show(this, $"プロファイル「{draft.ActiveProfile}」を書き出しました。\nアプリ別設定 (アプリのプロセス名) も入っています。渡す前に、見られてもよいか確かめてください。", "プロファイル", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"書き出せませんでした: {ex.Message}", "プロファイル", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ImportProfile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "プロファイルを読み込む",
            Filter = "Meltype のプロファイル (*.meltype-profile.json)|*.meltype-profile.json|JSON (*.json)|*.json|すべてのファイル (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        Settings? next = null;
        try
        {
            // プロファイルは数 KB。大きすぎるファイルは読まない
            if (new FileInfo(dialog.FileName).Length <= 1024 * 1024) next = Collect().ImportProfile(File.ReadAllText(dialog.FileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        if (next is null)
        {
            MessageBox.Show(this, "Meltype のプロファイルとして読めませんでした。「書き出す...」で作ったファイルを選んでください。", "プロファイル", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _draft = next;
        LoadFrom(_draft);
        RefreshProfiles();
        RunTest();
    }

    /// <summary>プロファイルの一覧を出し直して、使っているものを選ぶ。</summary>
    private void RefreshProfiles()
    {
        _loadingProfiles = true;
        _profiles.Items.Clear();
        _profiles.Items.AddRange(_draft.ProfileNames.Cast<object>().ToArray());
        _profiles.SelectedItem = _draft.ActiveProfile;
        _loadingProfiles = false;
    }

    /// <summary>分類 (Category) ごとに 見出し + カード を作り、左の一覧にその分類へ飛ぶボタンを足す。最後に「判定テスト」。</summary>
    private void BuildSections(FlowLayoutPanel nav)
    {
        var categories = typeof(Settings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.GetCustomAttribute<BrowsableAttribute>()?.Browsable != false)
            .GroupBy(p => p.GetCustomAttribute<CategoryAttribute>()?.Category ?? "その他")
            .OrderBy(g => g.Key, StringComparer.Ordinal);
        foreach (var category in categories)
        {
            var rows = new List<SettingRow>();
            foreach (var property in category)
            {
                if (CreateBinding(property) is not { } binding) continue;
                var name = property.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ?? property.Name;
                var description = property.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "";
                // 表 (アプリ別設定は表 + ボタンの枠) は、右に置くと狭いので、説明の下に幅いっぱいで出す
                var row = new SettingRow(name, description, binding.Control, fullWidth: binding.Control is DataGridView or Panel);
                ShowHelpFor(binding.Control, name, description);
                rows.Add(row);
                _bindings.Add(binding);
            }
            if (rows.Count > 0) AddSection(nav, StripNumber(category.Key), rows);
        }

        var testLabel = new Label { Text = "英字で入力 (例: kyouha meeting)", Dock = DockStyle.Top, Height = 22, ForeColor = Theme.Muted, Font = Theme.Small, BackColor = Theme.Card };
        _testInput.TextChanged += (_, _) => RunTest();
        var resultFrame = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0), BackColor = Theme.Card };
        resultFrame.Controls.Add(_testResult);
        var testPanel = new Panel { Height = 170, BackColor = Theme.Card };
        testPanel.Controls.Add(resultFrame);
        testPanel.Controls.Add(_testInput);
        testPanel.Controls.Add(testLabel);
        AddSection(nav, "判定テスト",
            [new SettingRow("IME 自動切替の判定", "打った英字が日本語と英語のどちらと判定されるか、その理由と点数を確かめられます。", testPanel, fullWidth: true)]);
    }

    private void AddSection(FlowLayoutPanel nav, string title, List<SettingRow> rows)
    {
        var header = new Label { Text = title, AutoSize = true, Font = Theme.Section, ForeColor = Theme.Ink, BackColor = Theme.Canvas };
        var card = new CardPanel();
        for (var i = 0; i < rows.Count; i++)
        {
            rows[i].Last = i == rows.Count - 1;
            card.Controls.Add(rows[i]);
        }
        _body.Controls.Add(header);
        _body.Controls.Add(card);

        var button = new Button { Text = title, Width = Math.Max(120, nav.ClientSize.Width - 2), Margin = new Padding(0, 0, 0, 2) };
        Theme.StyleNavButton(button);
        button.Click += (_, _) =>
        {
            SelectNav(button);
            // 見出しが上に来るまでスクロールする
            _body.AutoScrollPosition = new Point(0, header.Top - _body.AutoScrollPosition.Y - 20);
        };
        nav.Controls.Add(button);
        _sections.Add((header, card, rows, button));
        if (_activeNav is null) SelectNav(button);
    }

    private void SelectNav(Button button)
    {
        if (_activeNav is { } previous)
        {
            previous.BackColor = Theme.Sidebar;
            previous.Font = Theme.Body;
            previous.ForeColor = Theme.Hex(0x3F4047);
        }
        _activeNav = button;
        button.BackColor = Theme.Card;
        button.Font = Theme.BodyBold;
        button.ForeColor = Theme.Ink;
    }

    /// <summary>右側の見出しとカードを、今の幅に合わせて上から並べ直す。</summary>
    private void LayoutBody()
    {
        if (_sections.Count == 0) return;
        _body.SuspendLayout();
        var scale = DeviceDpi / 96F;
        var pad = (int)(28 * scale);
        // 縦のスクロールバーが出ても右端が隠れないように、その幅を先に引いておく
        var available = _body.ClientSize.Width - (_body.VerticalScroll.Visible ? 0 : SystemInformation.VerticalScrollBarWidth);
        var width = Math.Max(320, Math.Min(available - pad * 2, (int)(860 * scale)));
        var origin = _body.AutoScrollPosition;
        var y = pad;
        foreach (var (header, card, rows, _) in _sections)
        {
            header.Location = new Point(pad + origin.X, y + origin.Y);
            y += header.PreferredHeight + (int)(10 * scale);
            var rowY = 1;
            foreach (var row in rows)
            {
                var h = row.Arrange(width - 2);
                row.SetBounds(1, rowY, width - 2, h);
                rowY += h;
            }
            card.SetBounds(pad + origin.X, y + origin.Y, width, rowY + 1);
            y += rowY + 1 + (int)(32 * scale);
        }
        // いちばん下まで見えるように、余白の分だけスクロールできる範囲を足す
        _body.AutoScrollMinSize = new Size(0, y + pad);
        _body.ResumeLayout();
    }

    /// <summary>項目の種類に合わせた部品を作る。</summary>
    private Binding? CreateBinding(PropertyInfo property)
    {
        var type = property.PropertyType;
        if (type == typeof(bool))
        {
            var toggle = new ToggleSwitch();
            toggle.CheckedChanged += (_, _) => RunTest();
            return new Binding(property, toggle,
                s => toggle.Checked = (bool)property.GetValue(s)!,
                s => property.SetValue(s, toggle.Checked));
        }
        if (type.IsEnum)
        {
            var values = Enum.GetValues(type).Cast<object>().ToList();
            var names = values.Select(v => EnumName(type, v)).ToArray();
            // 選択肢が少なく名前が短いときは横に並べる (どれを選べるかが一目で分かる)。多いときや長いときはプルダウン。
            var shortNames = names.Select(ShortName).ToArray();
            if (values.Count <= 4 && shortNames.All(n => n.Length <= 10))
            {
                var segments = new SegmentedControl(shortNames);
                segments.SelectedIndexChanged += (_, _) => RunTest();
                for (var i = 0; i < names.Length; i++)
                {
                    if (names[i] != shortNames[i]) _toolTip.SetToolTip(segments, string.Join(" / ", names));
                }
                return new Binding(property, segments,
                    s => segments.SelectedIndex = values.IndexOf(property.GetValue(s)!),
                    s => property.SetValue(s, values[Math.Max(0, segments.SelectedIndex)]));
            }
            var combo = DropDown(names);
            return new Binding(property, combo,
                s => combo.SelectedIndex = values.IndexOf(property.GetValue(s)!),
                s => property.SetValue(s, values[Math.Max(0, combo.SelectedIndex)]));
        }
        if (type == typeof(int))
        {
            var number = new NumericUpDown { Minimum = 0, Maximum = 60000, Width = 120, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Body, TextAlign = HorizontalAlignment.Right };
            return new Binding(property, number,
                s => number.Value = Math.Clamp((int)property.GetValue(s)!, (int)number.Minimum, (int)number.Maximum),
                s => property.SetValue(s, (int)number.Value));
        }
        if (type == typeof(List<AppRule>))
        {
            var grid = _rulesGrid = AppRulesGrid();
            StyleGrid(grid);
            // プロセス名 (maya.exe など) を知らなくても足せるように、実行中のアプリから選べるようにする。
            var add = new Button { Text = "実行中のアプリから追加…", AutoSize = true };
            Theme.StyleButton(add);
            add.Click += (_, _) => ShowRunningApps(grid, add);
            // 行の削除は Delete キーでもできるが、気づきにくいのでボタンも置く
            var remove = new Button { Text = "選んだ行を削除", AutoSize = true };
            Theme.StyleButton(remove);
            remove.Click += (_, _) =>
            {
                foreach (var row in grid.SelectedCells.Cast<DataGridViewCell>().Select(c => c.OwningRow).Distinct().Where(r => !r.IsNewRow).ToList()) grid.Rows.Remove(row);
            };
            var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = false, Margin = Padding.Empty, Padding = new Padding(0, 8, 0, 0), BackColor = Theme.Card };
            actions.Controls.AddRange([add, remove]);
            var panel = new Panel { Height = grid.Height + add.PreferredSize.Height + 16, BackColor = Theme.Card };
            panel.Controls.Add(grid);
            panel.Controls.Add(actions);
            return new Binding(property, panel,
                s =>
                {
                    RefreshKindChoices(s.AppKinds.Select(k => k.Name));
                    grid.Rows.Clear();
                    foreach (var rule in (List<AppRule>)property.GetValue(s)!)
                    {
                        var kind = rule.Kind is { Length: > 0 } name && s.AppKinds.Any(k => k.Name == name) ? name : EnumName(typeof(AppProfile), rule.Profile);
                        grid.Rows.Add(rule.Process, rule.Enabled ? On : Off, kind);
                    }
                },
                s => property.SetValue(s, grid.Rows.Cast<DataGridViewRow>()
                    .Where(r => !r.IsNewRow)
                    .Select(r => (Process: (r.Cells[0].Value as string ?? "").Trim(), r.Cells[1].Value, Kind: r.Cells[2].Value as string ?? ""))
                    .Where(r => r.Process.Length > 0)
                    .Select(r =>
                    {
                        var builtIn = Enum.GetValues<AppProfile>().Where(p => EnumName(typeof(AppProfile), p) == r.Kind).Cast<AppProfile?>().FirstOrDefault();
                        return new AppRule
                        {
                            Process = r.Process,
                            Enabled = !Equals(r.Value, Off),
                            Profile = builtIn ?? AppProfile.General,
                            Kind = builtIn is null && r.Kind.Length > 0 ? r.Kind : null,
                        };
                    })
                    .ToList()));
        }
        if (type == typeof(List<AppKind>))
        {
            var grid = AppKindsGrid();
            StyleGrid(grid);
            return new Binding(property, grid,
                s =>
                {
                    grid.Rows.Clear();
                    foreach (var kind in (List<AppKind>)property.GetValue(s)!)
                    {
                        grid.Rows.Add(kind.Name, EnumName(typeof(AppProfile), kind.Base),
                            kind.DetectionLevel is { } level ? EnumName(typeof(DetectionLevel), level) : SameAsGlobal,
                            kind.LiveConversion is { } live ? (live ? On : Off) : SameAsGlobal,
                            kind.StartInEnglish ? On : Off);
                    }
                },
                s => property.SetValue(s, ReadKinds(grid)));
        }
        return null;
    }

    /// <summary>"積極的 (Aggressive)" → "積極的"。括弧の中が英字だけのときは、横並びの選択では省く。</summary>
    private static string ShortName(string name)
    {
        var open = name.LastIndexOf(" (", StringComparison.Ordinal);
        if (open <= 0 || !name.EndsWith(')')) return name;
        var inner = name[(open + 2)..^1];
        return inner.All(c => char.IsAsciiLetter(c) || c == ' ') ? name[..open] : name;
    }

    /// <summary>表をカードになじむ見た目にする。</summary>
    private static void StyleGrid(DataGridView grid)
    {
        grid.BackgroundColor = Theme.Card;
        grid.BorderStyle = BorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.RowHeadersVisible = false;
        grid.GridColor = Theme.Divider;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Footer;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Muted;
        grid.ColumnHeadersDefaultCellStyle.Font = Theme.Small;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.Footer;
        grid.ColumnHeadersHeight = 32;
        grid.RowHeadersDefaultCellStyle.BackColor = Theme.Footer;
        grid.DefaultCellStyle.Font = Theme.Body;
        grid.DefaultCellStyle.ForeColor = Theme.Ink;
        grid.DefaultCellStyle.SelectionBackColor = Theme.JapaneseSoft;
        grid.DefaultCellStyle.SelectionForeColor = Theme.Ink;
        grid.RowTemplate.Height = 32;
        // プルダウンの矢印は、編集しているセルにだけ出す (表が矢印だらけにならないように)
        foreach (var column in grid.Columns.OfType<DataGridViewComboBoxColumn>())
        {
            column.DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing;
            column.FlatStyle = FlatStyle.Flat;
        }
        // 表の外枠 (枠なしにしたので、行の区切りとそろえた線を描く)
        grid.Paint += (_, e) =>
        {
            using var edge = new Pen(Theme.Border);
            e.Graphics.DrawRectangle(edge, 0, 0, grid.Width - 1, grid.Height - 1);
        };
    }

    private const string SameAsGlobal = "全体と同じ";
    private DataGridView? _rulesGrid;

    private static List<AppKind> ReadKinds(DataGridView grid) => grid.Rows.Cast<DataGridViewRow>()
        .Where(r => !r.IsNewRow && (r.Cells[0].Value as string ?? "").Trim().Length > 0)
        .Select(r => new AppKind
        {
            Name = ((string)r.Cells[0].Value).Trim(),
            Base = Equals(r.Cells[1].Value, EnumName(typeof(AppProfile), AppProfile.Code)) ? AppProfile.Code : AppProfile.General,
            DetectionLevel = Enum.GetValues<DetectionLevel>().Where(l => Equals(r.Cells[2].Value, EnumName(typeof(DetectionLevel), l))).Cast<DetectionLevel?>().FirstOrDefault(),
            LiveConversion = Equals(r.Cells[3].Value, On) ? true : Equals(r.Cells[3].Value, Off) ? false : null,
            StartInEnglish = Equals(r.Cells[4].Value, On),
        })
        .GroupBy(k => k.Name).Select(g => g.First())
        .ToList();

    /// <summary>アプリ別設定の「種類」の選択肢を、一般 / コード + 独自の種類 にする。</summary>
    private void RefreshKindChoices(IEnumerable<string> kinds)
    {
        if (_rulesGrid?.Columns[2] is not DataGridViewComboBoxColumn column) return;
        var choices = Enum.GetValues<AppProfile>().Select(p => EnumName(typeof(AppProfile), p)).Concat(kinds).Distinct().ToList();
        // 使われている値が選択肢から消えるとエラーになるので、今の値も残す。
        foreach (DataGridViewRow row in _rulesGrid.Rows)
        {
            if (row.Cells[2].Value is string value && !choices.Contains(value)) choices.Add(value);
        }
        column.Items.Clear();
        column.Items.AddRange(choices.Cast<object>().ToArray());
    }

    private DataGridView AppKindsGrid()
    {
        var grid = new DataGridView
        {
            Height = 140,
            Dock = DockStyle.Fill,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersWidth = 24,
            BackgroundColor = SystemColors.Window,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,
        };
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "名前 (例: チャット)", FillWeight = 26 });
        grid.Columns.Add(Choices("元にする種類", 18, Enum.GetValues<AppProfile>().Select(p => EnumName(typeof(AppProfile), p))));
        grid.Columns.Add(Choices("判定の強さ", 20, [SameAsGlobal, .. Enum.GetValues<DetectionLevel>().Select(l => EnumName(typeof(DetectionLevel), l))]));
        grid.Columns.Add(Choices("ライブ変換", 18, [SameAsGlobal, On, Off]));
        grid.Columns.Add(Choices("最初は英数", 18, [Off, On]));
        grid.DefaultValuesNeeded += (_, e) =>
        {
            e.Row.Cells[1].Value = EnumName(typeof(AppProfile), AppProfile.General);
            e.Row.Cells[2].Value = SameAsGlobal;
            e.Row.Cells[3].Value = SameAsGlobal;
            e.Row.Cells[4].Value = Off;
        };
        // 種類を足したり名前を変えたりしたら、アプリ別設定の「種類」の選択肢にすぐ出す。
        void Changed() => RefreshKindChoices(ReadKinds(grid).Select(k => k.Name));
        grid.CellValueChanged += (_, _) => Changed();
        grid.RowsRemoved += (_, _) => Changed();
        grid.DataError += (_, e) => e.ThrowException = false;
        return grid;

        static DataGridViewComboBoxColumn Choices(string header, int weight, IEnumerable<string> items)
        {
            var column = new DataGridViewComboBoxColumn { HeaderText = header, FillWeight = weight, FlatStyle = FlatStyle.Flat };
            column.Items.AddRange(items.Cast<object>().ToArray());
            return column;
        }
    }

    private ComboBox DropDown(string[] items)
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280, Font = Theme.Body };
        combo.Items.AddRange(items);
        combo.SelectedIndexChanged += (_, _) => RunTest();
        return combo;
    }

    /// <summary>窓を開いている実行中のアプリの一覧を出し、選んだものをアプリ別設定の表に足す (既に表にあれば、その行を選ぶ)。</summary>
    private static void ShowRunningApps(DataGridView grid, Control anchor)
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in System.Diagnostics.Process.GetProcesses())
        {
            try
            {
                if (process.MainWindowHandle != IntPtr.Zero && process.Id != Environment.ProcessId) names.Add(process.ProcessName + ".exe");
            }
            catch
            {
                // 終わったばかりのプロセスなど
            }
            finally
            {
                process.Dispose();
            }
        }
        var menu = new ContextMenuStrip();
        foreach (var name in names)
        {
            menu.Items.Add(name, null, (_, _) =>
            {
                var existing = grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => !r.IsNewRow && string.Equals(r.Cells[0].Value as string, name, StringComparison.OrdinalIgnoreCase));
                var row = existing ?? grid.Rows[grid.Rows.Add(name, On, EnumName(typeof(AppProfile), AppProfile.General))];
                grid.ClearSelection();
                row.Selected = true;
                grid.FirstDisplayedScrollingRowIndex = row.Index;
                grid.CurrentCell = row.Cells[2];
            });
        }
        if (menu.Items.Count == 0) menu.Items.Add("(窓を開いているアプリがありません)").Enabled = false;
        menu.Show(anchor, new Point(0, anchor.Height));
    }

    private static DataGridView AppRulesGrid()
    {
        var grid = new DataGridView
        {
            Height = 200,
            Dock = DockStyle.Fill,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersWidth = 24,
            BackgroundColor = SystemColors.Window,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,
        };
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "プロセス名 (例: code.exe)", FillWeight = 55 });
        var enabled = new DataGridViewComboBoxColumn { HeaderText = "自動切替", FillWeight = 22, FlatStyle = FlatStyle.Flat };
        enabled.Items.AddRange(On, Off);
        grid.Columns.Add(enabled);
        // 種類: 一般 / コード (コメント・文字列の中だけ日本語)
        var profile = new DataGridViewComboBoxColumn { HeaderText = "種類", FillWeight = 23, FlatStyle = FlatStyle.Flat };
        profile.Items.AddRange(Enum.GetValues<AppProfile>().Select(p => (object)EnumName(typeof(AppProfile), p)).ToArray());
        grid.Columns.Add(profile);
        grid.DefaultValuesNeeded += (_, e) =>
        {
            e.Row.Cells[1].Value = On;
            e.Row.Cells[2].Value = EnumName(typeof(AppProfile), AppProfile.General);
        };
        grid.DataError += (_, e) => e.ThrowException = false;
        return grid;
    }

    private static string EnumName(Type type, object value) =>
        type.GetField(value.ToString()!)?.GetCustomAttribute<DescriptionAttribute>()?.Description ?? value.ToString()!;

    /// <summary>"1. 全般" → "全般"</summary>
    private static string StripNumber(string category)
    {
        var dot = category.IndexOf(". ", StringComparison.Ordinal);
        return dot >= 0 && category[..dot].All(char.IsAsciiDigit) ? category[(dot + 2)..] : category;
    }

    /// <summary>説明は行の中に出しているので、ここでは部品にツールチップを付けるだけ (長い説明でも全文を読める)。</summary>
    private void ShowHelpFor(Control control, string name, string description)
    {
        _toolTip.SetToolTip(control, description.Length > 0 ? $"{name}: {description}" : name);
        if (control.AccessibleName is null) control.AccessibleName = name;
        if (description.Length > 0) control.AccessibleDescription = description;
    }

    private void LoadFrom(Settings settings)
    {
        foreach (var binding in _bindings) binding.Load(settings);
    }

    /// <summary>画面の内容を設定にする。画面に出していない項目 (SettingsVersion・プロファイルの一覧など) は今の値を引き継ぐ。</summary>
    private Settings Collect()
    {
        var settings = _draft.Clone();
        foreach (var binding in _bindings) binding.Store(settings);
        return settings.Normalize();
    }

    private void RunTest()
    {
        if (_bindings.Count == 0) return;
        var words = _testInput.Text.ToLowerInvariant().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            _testResult.Text = "";
            return;
        }
        var settings = Collect();
        var lines = new List<string>();
        foreach (var word in words)
        {
            var letters = new string(word.Where(c => c is >= 'a' and <= 'z').ToArray());
            if (letters.Length == 0) continue;
            // 実際の動作と同じく 1 文字ずつ判定し、最初に結論が出た時点の結果を表示する。
            for (var i = 1; i <= letters.Length; i++)
            {
                var result = _engine.Evaluate(letters[..i], settings);
                if (result.Verdict != Detection.Verdict.Undecided || i == letters.Length)
                {
                    var verdict = result.Verdict switch
                    {
                        Detection.Verdict.Undecided => "どちらとも言えない (Space で確定)",
                        _ => result.Verdict.ToString(),
                    };
                    lines.Add($"{word}: {verdict} — \"{letters[..i]}\" の時点, JP={result.JapaneseScore} EN={result.EnglishScore}\r\n    " +
                              string.Join("\r\n    ", result.Contributions.Select(c => c.ToString())));
                    break;
                }
            }
        }
        _testResult.Text = string.Join("\r\n", lines);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _toolTip.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>
    /// 設定の 1 行: 左に 名前 と 説明、右に部品。fullWidth の部品 (表など) は説明の下に幅いっぱいで置く。
    /// 高さは Arrange で、説明の折り返しに合わせて決める。
    /// </summary>
    private sealed class SettingRow : Panel
    {
        private readonly Label _name;
        private readonly Label _description;
        private readonly Control _editor;
        private readonly bool _fullWidth;
        private readonly bool _hasDescription;

        public SettingRow(string name, string description, Control editor, bool fullWidth)
        {
            BackColor = Theme.Card;
            DoubleBuffered = true;
            _editor = editor;
            _fullWidth = fullWidth;
            _hasDescription = description.Length > 0;
            _name = new Label { Text = name, AutoSize = false, Font = Theme.BodyBold, ForeColor = Theme.Ink, BackColor = Theme.Card, UseMnemonic = false };
            _description = new Label { Text = description, AutoSize = false, Font = Theme.Small, ForeColor = Theme.Muted, BackColor = Theme.Card, UseMnemonic = false, Visible = description.Length > 0 };
            Controls.Add(_name);
            Controls.Add(_description);
            Controls.Add(editor);
        }

        /// <summary>カードの最後の行には区切り線を引かない。</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Last { get; set; }

        /// <summary>width のときの部品の位置を決め、行の高さを返す。</summary>
        public int Arrange(int width)
        {
            var scale = DeviceDpi / 96F;
            int padX = (int)(18 * scale), padY = (int)(14 * scale), gap = (int)(20 * scale);
            var editorWidth = _fullWidth ? 0 : EditorWidth();
            var textWidth = Math.Max(80, width - padX * 2 - (editorWidth > 0 ? editorWidth + gap : 0));
            // ラベルが実際に描くときと同じ折り返しで高さを測る (多めに見積もると行の下が空く)
            var nameHeight = _name.GetPreferredSize(new Size(textWidth, 0)).Height;
            // Visible は画面に出る前は false なので、説明があるかは自分で覚えておいたもので見る
            var descHeight = _hasDescription ? _description.GetPreferredSize(new Size(textWidth, 0)).Height : 0;
            _name.SetBounds(padX, padY, textWidth, nameHeight);
            _description.SetBounds(padX, padY + nameHeight + (int)(3 * scale), textWidth, descHeight);
            var textBottom = padY + nameHeight + (descHeight > 0 ? descHeight + (int)(3 * scale) : 0);
            if (_fullWidth)
            {
                var top = textBottom + (int)(10 * scale);
                _editor.SetBounds(padX, top, width - padX * 2, _editor.Height);
                return top + _editor.Height + padY;
            }
            var height = Math.Max(textBottom, padY + _editor.Height) + padY;
            // 部品は右端に、名前の行の高さにそろえて置く (説明が長くても上に寄せる)
            var editorTop = Math.Max(padY - (int)(4 * scale), padY + (nameHeight - _editor.Height) / 2);
            if (descHeight == 0) editorTop = (height - _editor.Height) / 2;
            _editor.SetBounds(width - padX - editorWidth, editorTop, editorWidth, _editor.Height);
            return height;
        }

        private int EditorWidth() => _editor switch
        {
            SegmentedControl s => s.PreferredWidth(),
            ToggleSwitch t => t.Width,
            _ => _editor.Width,
        };

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Last) return;
            using var line = new Pen(Theme.Divider);
            var inset = (int)(18 * DeviceDpi / 96F);
            e.Graphics.DrawLine(line, inset, Height - 1, Width - inset, Height - 1);
        }
    }
}
