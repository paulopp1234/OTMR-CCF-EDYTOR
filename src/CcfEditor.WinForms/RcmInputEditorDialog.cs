using CcfEditor.Otmr.Rcm;

namespace CcfEditor.WinForms;

internal sealed class RcmInputEditorDialog : Form
{
    private readonly Dictionary<string, TextBox> _fields = new(StringComparer.Ordinal);
    private readonly ComboBox _connector = new() { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill };
    private readonly CheckBox _testable = new() { Text = "Allow controlled voltage capture", AutoSize = true };

    public RcmInputEditorDialog(
        string title,
        IEnumerable<string> connectors,
        RcmInputEdit initial)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(720, 650);
        Size = new Size(780, 820);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(10)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
        var editor = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            ColumnCount = 2,
            RowCount = 0
        };
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190F));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        _connector.Items.Add(string.Empty);
        _connector.Items.AddRange(connectors.Cast<object>().ToArray());
        _connector.Text = initial.Connector;
        AddControl(editor, "Connector", _connector);
        AddText(editor, "Pin", "Pin", initial.Pin);
        AddText(editor, "Function / signal name", "Function", initial.Function);
        AddText(editor, "Role", "Role", initial.Role);
        AddText(editor, "MIO", "Mio", initial.Mio);
        AddText(editor, "Physical channel", "PhysicalChannel", initial.PhysicalChannel);
        AddText(editor, "Return / pair wiring", "ReturnOrPair", initial.ReturnOrPair);
        AddControl(editor, "Testable", _testable);
        _testable.Checked = initial.Testable;
        AddText(editor, "Safety / bench instruction", "Safety", initial.SafetyClassification);
        AddText(editor, "Notes", "Notes", initial.Notes);
        AddSeparator(editor, "LOGICAL / CCF");
        AddText(editor, "Record A", "RecordA", Format(initial.RecordA));
        AddText(editor, "Record B", "RecordB", Format(initial.RecordB));
        AddText(editor, "Logical card", "LogicalCard", Format(initial.LogicalCard));
        AddText(editor, "Logical channel", "LogicalChannel", Format(initial.LogicalChannel));
        AddText(editor, "Record A description", "RecordAText", initial.RecordAText);
        AddText(editor, "Record A value/text", "RecordAValue", initial.RecordAValue);
        AddText(editor, "Record B description", "RecordBText", initial.RecordBText);
        AddText(editor, "Record B value/text", "RecordBValue", initial.RecordBValue);
        AddText(editor, "Record type", "RecordType", Format(initial.RecordType));
        AddText(editor, "Pair relationship", "PairRelationship", initial.PairRelationship);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var ok = new Button { Text = "Save Input", AutoSize = true };
        ok.Click += (_, _) => SaveAndClose();
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        root.Controls.Add(editor, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        Controls.Add(root);
        CancelButton = cancel;
    }

    public RcmInputEdit? Result { get; private set; }

    private void SaveAndClose()
    {
        try
        {
            Result = new RcmInputEdit
            {
                Connector = _connector.Text,
                Pin = Get("Pin"),
                Function = Get("Function"),
                Role = Get("Role"),
                Mio = Get("Mio"),
                PhysicalChannel = Get("PhysicalChannel"),
                ReturnOrPair = Get("ReturnOrPair"),
                Testable = _testable.Checked,
                SafetyClassification = Get("Safety"),
                Notes = Get("Notes"),
                RecordA = ParseNullableInt("RecordA"),
                RecordB = ParseNullableInt("RecordB"),
                LogicalCard = ParseNullableInt("LogicalCard"),
                LogicalChannel = ParseNullableInt("LogicalChannel"),
                RecordAText = Get("RecordAText"),
                RecordAValue = Get("RecordAValue"),
                RecordBText = Get("RecordBText"),
                RecordBValue = Get("RecordBValue"),
                RecordType = ParseNullableInt("RecordType"),
                PairRelationship = Get("PairRelationship")
            };
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Invalid input configuration", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private string Get(string key) => _fields[key].Text.Trim();

    private int? ParseNullableInt(string key)
    {
        string text = Get(key);
        if (text.Length == 0)
            return null;
        return int.TryParse(text, out int value)
            ? value
            : throw new FormatException($"{key} must be a whole number or blank.");
    }

    private void AddText(TableLayoutPanel table, string label, string key, string? value)
    {
        var textBox = new TextBox { Text = value ?? string.Empty, Dock = DockStyle.Fill };
        _fields.Add(key, textBox);
        AddControl(table, label, textBox);
    }

    private static void AddControl(TableLayoutPanel table, string label, Control control)
    {
        int row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 7, 8, 3) }, 0, row);
        table.Controls.Add(control, 1, row);
    }

    private static void AddSeparator(TableLayoutPanel table, string text)
    {
        int row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var label = new Label { Text = text, AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold), Margin = new Padding(3, 14, 3, 5) };
        table.Controls.Add(label, 0, row);
        table.SetColumnSpan(label, 2);
    }

    private static string Format(int? value) => value?.ToString() ?? string.Empty;
}
