namespace RadioGolha;

/// <summary>Small dialog used to add a station: a name and a stream URL.</summary>
internal sealed class StationDialog : Form
{
    private readonly TextBox _nameBox = new();
    private readonly TextBox _urlBox = new();

    public StationDialog(string name, string url)
    {
        Text = "Add radio station";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;
        AutoScaleDimensions = new SizeF(7F, 15F);
        Font = SystemFonts.MessageBoxFont;
        ClientSize = new Size(420, 128);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new Padding(12),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        layout.Controls.Add(new Label
        {
            Text = "Name:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 4, 8, 0),
        }, 0, 0);
        _nameBox.Dock = DockStyle.Fill;
        _nameBox.Text = name;
        _nameBox.Margin = new Padding(0, 2, 0, 8);
        layout.Controls.Add(_nameBox, 1, 0);

        layout.Controls.Add(new Label
        {
            Text = "Stream URL:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 4, 8, 0),
        }, 0, 1);
        _urlBox.Dock = DockStyle.Fill;
        _urlBox.Text = url;
        _urlBox.Margin = new Padding(0, 2, 0, 8);
        layout.Controls.Add(_urlBox, 1, 1);

        var buttonRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0),
        };
        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(75, 25),
            Margin = new Padding(6, 0, 0, 0),
        };
        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(75, 25),
            Margin = new Padding(6, 0, 0, 0),
        };
        buttonRow.Controls.Add(okButton);
        buttonRow.Controls.Add(cancelButton);
        layout.Controls.Add(buttonRow, 0, 2);
        layout.SetColumnSpan(buttonRow, 2);

        okButton.Click += (_, _) =>
        {
            if (StationUrl.Length > 0)
                return;

            // keep the dialog open until a stream URL is entered
            DialogResult = DialogResult.None;
            MessageBox.Show(this, "Please enter the stream URL.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _urlBox.Focus();
        };

        Controls.Add(layout);
        AcceptButton = okButton;
        CancelButton = cancelButton;

        Shown += (_, _) =>
        {
            _nameBox.Focus();
            _nameBox.SelectAll();
        };
    }

    /// <summary>Entered name; falls back to the URL when left empty.</summary>
    public string StationName
    {
        get
        {
            var name = _nameBox.Text.Trim();
            return name.Length > 0 ? name : StationUrl;
        }
    }

    public string StationUrl => _urlBox.Text.Trim();
}
