namespace CcfEditor.WinForms;

internal sealed class TextPromptDialog : Form
{
    private readonly TextBox _textBox = new();

    public TextPromptDialog(string title, string prompt, string initialValue = "")
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(430, 125);

        var label = new Label { Text = prompt, AutoSize = true, Location = new Point(12, 12) };
        _textBox.SetBounds(12, 38, 402, 24);
        _textBox.Text = initialValue;
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(248, 80), Width = 78 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(336, 80), Width = 78 };
        Controls.AddRange(new Control[] { label, _textBox, ok, cancel });
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public string Value => _textBox.Text.Trim();
}
