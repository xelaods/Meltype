// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using Meltype.Input;

namespace Meltype.UI;

/// <summary>
/// ユーザー辞書の登録・削除。読みはローマ字で打ってもよい (kigoutou → きごうとう)。
/// 読みを打つと変換候補がプルダウンに出るので、そこから選ぶか、単語欄に直接打って登録する
/// (この画面では Meltype キーボードが使える)。
/// </summary>
internal sealed class UserDictionaryForm : Form
{
    private readonly CompositionService _service;
    private readonly TextBox _reading = new() { Width = 220 };
    private readonly Label _readingPreview = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Padding = new Padding(0, 6, 0, 0) };
    private readonly ComboBox _word = new() { Width = 220, DropDownStyle = ComboBoxStyle.DropDown };
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        RowHeadersVisible = false,
        BackgroundColor = SystemColors.Window,
    };
    private readonly Label _message = new() { AutoSize = true, ForeColor = Color.Firebrick, Padding = new Padding(0, 6, 0, 0) };
    private readonly System.Windows.Forms.Timer _suggestTimer = new() { Interval = 300 };

    public UserDictionaryForm(CompositionService service)
    {
        _service = service;
        Text = "Meltype ユーザー辞書";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(600, 620);
        MinimumSize = new Size(480, 480);
        Font = Theme.Body;
        BackColor = Theme.Canvas;
        ForeColor = Theme.Ink;
        _reading.BorderStyle = BorderStyle.FixedSingle;
        _reading.Dock = DockStyle.Fill;
        _word.Dock = DockStyle.Fill;
        _readingPreview.ForeColor = Theme.JapaneseText;
        _readingPreview.BackColor = Theme.Card;
        _message.ForeColor = Theme.JapaneseText;
        _message.BackColor = Theme.Card;

        // 上: 「ことばを登録」のカード。左に読み、右に単語、その下に登録ボタン。
        var add = new Button { Text = "登録", AutoSize = true, MinimumSize = new Size(96, 34), Anchor = AnchorStyles.Right };
        Theme.StyleButton(add, primary: true);
        add.Click += (_, _) => Register();
        var entry = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5, BackColor = Theme.Card, Padding = new Padding(8, 6, 8, 4) };
        entry.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        entry.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (var i = 0; i < 5; i++) entry.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var title = new Label { Text = "ことばを登録", AutoSize = true, Font = Theme.Section, BackColor = Theme.Card, Margin = new Padding(3, 0, 3, 8) };
        entry.Controls.Add(title, 0, 0);
        entry.SetColumnSpan(title, 2);
        entry.Controls.Add(new Label { Text = "読み (ローマ字・ひらがな)", AutoSize = true, ForeColor = Theme.Muted, Font = Theme.Small, BackColor = Theme.Card }, 0, 1);
        entry.Controls.Add(new Label { Text = "ことば (候補から選ぶか直接入力)", AutoSize = true, ForeColor = Theme.Muted, Font = Theme.Small, BackColor = Theme.Card }, 1, 1);
        entry.Controls.Add(_reading, 0, 2);
        entry.Controls.Add(_word, 1, 2);
        entry.Controls.Add(_readingPreview, 0, 3);
        entry.Controls.Add(_message, 1, 3);
        entry.Controls.Add(add, 1, 4);
        var entryCard = new CardPanel { Dock = DockStyle.Top, Height = 176, Padding = new Padding(10) };
        entryCard.Controls.Add(entry);
        var entryFrame = new Panel { Dock = DockStyle.Top, Height = 176 + 32, Padding = new Padding(16, 16, 16, 16), BackColor = Theme.Canvas };
        entryFrame.Controls.Add(entryCard);

        // 下: ボタンの帯
        var remove = new Button { Text = "選んだ語を削除", AutoSize = true };
        remove.Click += (_, _) => RemoveSelected();
        var close = new Button { Text = "閉じる", AutoSize = true, MinimumSize = new Size(96, 34), DialogResult = DialogResult.Cancel };
        close.Click += (_, _) => Close();
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12, 10, 12, 10), BackColor = Theme.Footer };
        buttons.Paint += (_, e) =>
        {
            using var line = new Pen(Theme.Border);
            e.Graphics.DrawLine(line, 0, 0, buttons.Width, 0);
        };
        // ほかの日本語入力 (Microsoft IME・Google 日本語入力) の辞書の取り込みと、Microsoft IME の形式での書き出し
        var import = new Button { Text = "取り込む...", AutoSize = true };
        import.Click += (_, _) => Import();
        var export = new Button { Text = "書き出す...", AutoSize = true };
        export.Click += (_, _) => Export();
        foreach (var button in new[] { remove, import, export }) Theme.StyleButton(button);
        Theme.StyleButton(close, primary: true);
        buttons.Controls.AddRange([close, remove, export, import]);

        // 真ん中: 登録済みの一覧
        _grid.Columns.Add("reading", "読み");
        _grid.Columns.Add("word", "ことば");
        _grid.BackgroundColor = Theme.Card;
        _grid.BorderStyle = BorderStyle.None;
        _grid.GridColor = Theme.Divider;
        _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Card;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Muted;
        _grid.ColumnHeadersDefaultCellStyle.Font = Theme.Small;
        _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.Card;
        _grid.ColumnHeadersHeight = 30;
        _grid.DefaultCellStyle.Font = Theme.Body;
        _grid.DefaultCellStyle.ForeColor = Theme.Ink;
        _grid.DefaultCellStyle.SelectionBackColor = Theme.JapaneseSoft;
        _grid.DefaultCellStyle.SelectionForeColor = Theme.Ink;
        _grid.RowTemplate.Height = 32;
        var listTitle = new Label { Text = "登録済み", Dock = DockStyle.Top, Height = 30, Font = Theme.BodyBold, BackColor = Theme.Canvas, Padding = new Padding(2, 4, 0, 0) };
        var listCard = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        listCard.Controls.Add(_grid);
        var gridPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 0, 16, 16), BackColor = Theme.Canvas };
        gridPanel.Controls.Add(listCard);
        gridPanel.Controls.Add(listTitle);

        Controls.Add(gridPanel);
        Controls.Add(entryFrame);
        Controls.Add(buttons);
        AcceptButton = add;
        CancelButton = close;

        _reading.TextChanged += (_, _) =>
        {
            _readingPreview.Text = _reading.Text.Any(char.IsAsciiLetter) ? "→ " + _service.ToReading(_reading.Text) : "";
            _suggestTimer.Stop();
            _suggestTimer.Start();
        };
        _suggestTimer.Tick += (_, _) =>
        {
            _suggestTimer.Stop();
            Suggest();
        };
        _grid.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete) RemoveSelected();
        };
        Reload();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ForegroundTracker.TypingAllowedWindows[Handle] = true;
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        ForegroundTracker.TypingAllowedWindows.TryRemove(Handle, out _);
        base.OnHandleDestroyed(e);
    }

    private void Suggest()
    {
        var reading = _service.ToReading(_reading.Text);
        var typed = _word.Text;
        _word.Items.Clear();
        if (reading.Length == 0) return;
        foreach (var word in _service.SuggestWords(reading)) _word.Items.Add(word);
        if (typed.Length == 0 && _word.Items.Count > 0) _word.SelectedIndex = 0;
    }

    private void Register()
    {
        var reading = _service.ToReading(_reading.Text);
        var error = _service.UserDictionary.Add(reading, _word.Text);
        if (error is not null)
        {
            _message.Text = error;
            return;
        }
        _message.Text = "";
        Diagnostics.Log.Info($"ユーザー辞書に登録しました: {Diagnostics.Log.Text(reading)} → {Diagnostics.Log.Text(_word.Text.Trim())}");
        _reading.Clear();
        _word.Text = "";
        _word.Items.Clear();
        Reload();
        _reading.Focus();
    }

    /// <summary>選んでいた語を入れて開く (Ctrl+F7)。読みは推測したもの (直して登録できる)。</summary>
    public void Prefill(string word, string reading)
    {
        _word.Text = word;
        _reading.Text = reading;
        _message.Text = reading.Length == 0 ? "読みを入力してください。" : "読みを確かめて「登録」を押してください。";
        _message.ForeColor = reading.Length == 0 ? Color.Firebrick : SystemColors.GrayText;
        Activate();
        _reading.Focus();
        _reading.SelectAll();
    }

    private void Import()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "ユーザー辞書を取り込む",
            Filter = "辞書のテキストファイル (*.txt)|*.txt|すべてのファイル (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var result = UserDictionaryFile.Parse(File.ReadAllBytes(dialog.FileName));
            var added = _service.UserDictionary.AddRange(result.Words);
            Reload();
            Diagnostics.Log.Info($"ユーザー辞書を取り込みました: {added} 語 ({result.Encoding})");
            var skipped = result.Skipped > 0 ? $"\n読みがかなでない・短すぎるなどで飛ばした行: {result.Skipped}" : "";
            var duplicates = result.Words.Count - added;
            MessageBox.Show(this, $"{added} 語を登録しました。{(duplicates > 0 ? $"\n登録済みの語: {duplicates}" : "")}{skipped}", "ユーザー辞書の取り込み",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"取り込めませんでした。\n\n{ex.Message}", "ユーザー辞書の取り込み", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void Export()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "ユーザー辞書を書き出す",
            Filter = "辞書のテキストファイル (*.txt)|*.txt",
            FileName = $"Meltype-ユーザー辞書-{DateTime.Now:yyyyMMdd}.txt",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllBytes(dialog.FileName, UserDictionaryFile.Export(_service.UserDictionary.Words));
            MessageBox.Show(this, $"{_service.UserDictionary.Count} 語を書き出しました。\nMicrosoft IME・Google 日本語入力・ATOK の辞書ツールで取り込めます。", "ユーザー辞書の書き出し",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"書き出せませんでした。\n\n{ex.Message}", "ユーザー辞書の書き出し", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void RemoveSelected()
    {
        foreach (DataGridViewRow row in _grid.SelectedRows)
        {
            if (row.Tag is UserWord word) _service.UserDictionary.Remove(word);
        }
        Reload();
    }

    private void Reload()
    {
        _grid.Rows.Clear();
        foreach (var word in _service.UserDictionary.Words.Reverse())
        {
            var index = _grid.Rows.Add(word.Reading, word.Word);
            _grid.Rows[index].Tag = word;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _suggestTimer.Dispose();
        base.Dispose(disposing);
    }
}
