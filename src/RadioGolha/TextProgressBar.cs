namespace RadioGolha;

/// <summary>
/// A normal Windows progress bar that can draw a short label centered over the
/// bar itself. The bar keeps its stock themed appearance - the text is simply
/// painted on top after the bar draws, so no custom colors or styles are needed.
/// </summary>
internal sealed class TextProgressBar : ProgressBar
{
    private const int WmPaint = 0x000F;

    private string _text = string.Empty;

    /// <summary>Text centered inside the bar; empty string hides it.</summary>
    public new string Text
    {
        get => _text;
        set
        {
            value ??= string.Empty;
            if (_text == value) return;

            _text = value;
            AccessibleName = value; // expose it to screen readers / UI Automation
            Invalidate();
        }
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);

        if (m.Msg != WmPaint || _text.Length == 0)
            return;

        using var graphics = Graphics.FromHwnd(Handle);
        TextRenderer.DrawText(
            graphics,
            _text,
            Font,
            ClientRectangle,
            SystemColors.ControlText,
            TextFormatFlags.HorizontalCenter
                | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine
                | TextFormatFlags.NoPrefix);
    }
}
