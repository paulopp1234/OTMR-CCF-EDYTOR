using CcfEditor.Otmr.Rcm;

namespace CcfEditor.WinForms;

internal sealed class RcmConnectorPinsDialog : Form
{
    private readonly TextBox _pins = new()
    {
        AcceptsReturn = true,
        AcceptsTab = false,
        Dock = DockStyle.Fill,
        Multiline = true,
        ScrollBars = ScrollBars.Vertical,
        WordWrap = false
    };

    public RcmConnectorPinsDialog(RcmConnector connector)
    {
        ArgumentNullException.ThrowIfNull(connector);
        Text = $"Pin Sequence - {connector.Name}";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(430, 430);
        Size = new Size(480, 620);

        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 3,
            Dock = DockStyle.Fill,
            Padding = new Padding(10)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
        root.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Optional ordered pins, one per line. Assign Next Pin follows this exact order; blank means no suggestion.",
            Margin = new Padding(3, 3, 3, 8)
        }, 0, 0);
        _pins.Lines = connector.OrderedPins.ToArray();
        root.Controls.Add(_pins, 0, 1);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        var save = new Button { Text = "Save Pin Sequence", AutoSize = true };
        save.Click += (_, _) => SaveAndClose();
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(save);
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);
        CancelButton = cancel;
    }

    public IReadOnlyList<string> OrderedPins { get; private set; } = Array.Empty<string>();

    private void SaveAndClose()
    {
        List<string> pins = _pins.Lines.Select(pin => pin.Trim()).Where(pin => pin.Length > 0).ToList();
        if (pins.Distinct(StringComparer.Ordinal).Count() != pins.Count)
        {
            MessageBox.Show(this, "The ordered pin list contains a duplicate pin name.", "Invalid pin sequence",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        OrderedPins = pins;
        DialogResult = DialogResult.OK;
        Close();
    }
}
