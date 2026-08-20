namespace CcfEditor.WinForms;

partial class MainForm
{
    private System.ComponentModel.IContainer? components = null;

    private MenuStrip menuStrip = null!;
    private ToolStripMenuItem fileMenu = null!;
    private ToolStripMenuItem openMenuItem = null!;
    private ToolStripMenuItem saveCopyMenuItem = null!;
    private ToolStripMenuItem exitMenuItem = null!;
    private ToolStrip toolStrip = null!;
    private ToolStripButton openButton = null!;
    private ToolStripButton saveCopyButton = null!;
    private ToolStripLabel searchLabel = null!;
    private ToolStripTextBox searchTextBox = null!;
    private ToolStripButton jumpToPairButton = null!;
    private StatusStrip statusStrip = null!;
    private ToolStripStatusLabel fileStatusLabel = null!;
    private ToolStripStatusLabel springStatusLabel = null!;
    private ToolStripStatusLabel shaStatusLabel = null!;
    private TabControl tabs = null!;
    private TabPage recordsTab = null!;
    private TabPage headerTab = null!;
    private TabPage hexTab = null!;
    private TabPage validationTab = null!;
    private SplitContainer recordsSplit = null!;
    private DataGridView recordsGrid = null!;
    private TableLayoutPanel detailsTable = null!;
    private TextBox detailRecordTextBox = null!;
    private TextBox detailTypeTextBox = null!;
    private TextBox detailNameTextBox = null!;
    private TextBox detailPairTextBox = null!;
    private TextBox detailCardChannelTextBox = null!;
    private TextBox detailLoggerFunctionTextBox = null!;
    private TextBox detailOffTextBox = null!;
    private TextBox detailOnTextBox = null!;
    private TextBox detailOffsetTextBox = null!;
    private TextBox detailRawTextBox = null!;
    private DataGridView headerGrid = null!;
    private DataGridView hexGrid = null!;
    private ListView validationList = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components is not null)
            components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        menuStrip = new MenuStrip();
        fileMenu = new ToolStripMenuItem();
        openMenuItem = new ToolStripMenuItem();
        saveCopyMenuItem = new ToolStripMenuItem();
        exitMenuItem = new ToolStripMenuItem();
        toolStrip = new ToolStrip();
        openButton = new ToolStripButton();
        saveCopyButton = new ToolStripButton();
        searchLabel = new ToolStripLabel();
        searchTextBox = new ToolStripTextBox();
        jumpToPairButton = new ToolStripButton();
        statusStrip = new StatusStrip();
        fileStatusLabel = new ToolStripStatusLabel();
        springStatusLabel = new ToolStripStatusLabel();
        shaStatusLabel = new ToolStripStatusLabel();
        tabs = new TabControl();
        recordsTab = new TabPage();
        headerTab = new TabPage();
        hexTab = new TabPage();
        validationTab = new TabPage();
        recordsSplit = new SplitContainer();
        recordsGrid = new DataGridView();
        detailsTable = new TableLayoutPanel();
        detailRecordTextBox = new TextBox();
        detailTypeTextBox = new TextBox();
        detailNameTextBox = new TextBox();
        detailPairTextBox = new TextBox();
        detailCardChannelTextBox = new TextBox();
        detailLoggerFunctionTextBox = new TextBox();
        detailOffTextBox = new TextBox();
        detailOnTextBox = new TextBox();
        detailOffsetTextBox = new TextBox();
        detailRawTextBox = new TextBox();
        headerGrid = new DataGridView();
        hexGrid = new DataGridView();
        validationList = new ListView();

        menuStrip.SuspendLayout();
        toolStrip.SuspendLayout();
        statusStrip.SuspendLayout();
        tabs.SuspendLayout();
        recordsTab.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)recordsSplit).BeginInit();
        recordsSplit.Panel1.SuspendLayout();
        recordsSplit.Panel2.SuspendLayout();
        recordsSplit.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)recordsGrid).BeginInit();
        detailsTable.SuspendLayout();
        headerTab.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)headerGrid).BeginInit();
        hexTab.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)hexGrid).BeginInit();
        validationTab.SuspendLayout();
        SuspendLayout();

        menuStrip.Items.AddRange(new ToolStripItem[] { fileMenu });
        menuStrip.Location = new Point(0, 0);
        menuStrip.Name = "menuStrip";
        menuStrip.Size = new Size(1500, 24);

        fileMenu.DropDownItems.AddRange(new ToolStripItem[] { openMenuItem, saveCopyMenuItem, new ToolStripSeparator(), exitMenuItem });
        fileMenu.Name = "fileMenu";
        fileMenu.Text = "&File";

        openMenuItem.Name = "openMenuItem";
        openMenuItem.ShortcutKeys = Keys.Control | Keys.O;
        openMenuItem.Text = "&Open CCF...";
        openMenuItem.Click += OpenMenuItem_Click;

        saveCopyMenuItem.Enabled = false;
        saveCopyMenuItem.Name = "saveCopyMenuItem";
        saveCopyMenuItem.Text = "Save No-Edit Copy &As...";
        saveCopyMenuItem.Click += SaveCopyMenuItem_Click;

        exitMenuItem.Name = "exitMenuItem";
        exitMenuItem.Text = "E&xit";
        exitMenuItem.Click += ExitMenuItem_Click;

        toolStrip.Items.AddRange(new ToolStripItem[] { openButton, saveCopyButton, new ToolStripSeparator(), searchLabel, searchTextBox, jumpToPairButton });
        toolStrip.Location = new Point(0, 24);
        toolStrip.Name = "toolStrip";
        toolStrip.Size = new Size(1500, 25);

        openButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
        openButton.Name = "openButton";
        openButton.Text = "Open CCF";
        openButton.Click += OpenMenuItem_Click;

        saveCopyButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
        saveCopyButton.Enabled = false;
        saveCopyButton.Name = "saveCopyButton";
        saveCopyButton.Text = "Save No-Edit Copy As";
        saveCopyButton.Click += SaveCopyMenuItem_Click;

        searchLabel.Name = "searchLabel";
        searchLabel.Text = "Search:";

        searchTextBox.Name = "searchTextBox";
        searchTextBox.Size = new Size(260, 25);
        searchTextBox.TextChanged += SearchTextBox_TextChanged;

        jumpToPairButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
        jumpToPairButton.Enabled = false;
        jumpToPairButton.Name = "jumpToPairButton";
        jumpToPairButton.Text = "Jump to pair";
        jumpToPairButton.Click += JumpToPairButton_Click;

        statusStrip.Items.AddRange(new ToolStripItem[] { fileStatusLabel, springStatusLabel, shaStatusLabel });
        statusStrip.Location = new Point(0, 878);
        statusStrip.Name = "statusStrip";
        statusStrip.Size = new Size(1500, 22);

        fileStatusLabel.Name = "fileStatusLabel";
        fileStatusLabel.Text = "No CCF loaded";

        springStatusLabel.Name = "springStatusLabel";
        springStatusLabel.Spring = true;

        shaStatusLabel.Name = "shaStatusLabel";
        shaStatusLabel.Text = "SHA-256: -";

        tabs.Controls.Add(recordsTab);
        tabs.Controls.Add(headerTab);
        tabs.Controls.Add(hexTab);
        tabs.Controls.Add(validationTab);
        tabs.Dock = DockStyle.Fill;
        tabs.Location = new Point(0, 49);
        tabs.Name = "tabs";
        tabs.SelectedIndex = 0;
        tabs.Size = new Size(1500, 829);

        recordsTab.Controls.Add(recordsSplit);
        recordsTab.Location = new Point(4, 24);
        recordsTab.Name = "recordsTab";
        recordsTab.Padding = new Padding(3);
        recordsTab.Text = "Records";
        recordsTab.UseVisualStyleBackColor = true;

        recordsSplit.Dock = DockStyle.Fill;
        recordsSplit.Location = new Point(3, 3);
        recordsSplit.Name = "recordsSplit";
        recordsSplit.Orientation = Orientation.Horizontal;
        recordsSplit.SplitterDistance = 520;
        recordsSplit.Panel1.Controls.Add(recordsGrid);
        recordsSplit.Panel2.Controls.Add(detailsTable);

        recordsGrid.AllowUserToAddRows = false;
        recordsGrid.AllowUserToDeleteRows = false;
        recordsGrid.AllowUserToOrderColumns = true;
        recordsGrid.AutoGenerateColumns = false;
        recordsGrid.BackgroundColor = SystemColors.Window;
        recordsGrid.Dock = DockStyle.Fill;
        recordsGrid.MultiSelect = false;
        recordsGrid.Name = "recordsGrid";
        recordsGrid.ReadOnly = true;
        recordsGrid.RowHeadersVisible = false;
        recordsGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        recordsGrid.SelectionChanged += RecordsGrid_SelectionChanged;

        recordsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Record", HeaderText = "Record", ReadOnly = true, Width = 65 });
        recordsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "EventIndex", HeaderText = "Event", ReadOnly = true, Width = 65 });
        recordsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Type", HeaderText = "Type", ReadOnly = true, Width = 50 });
        recordsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Flag", HeaderText = "Flag", ReadOnly = true, Width = 50 });
        recordsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Name", HeaderText = "Name", ReadOnly = true, Width = 180 });
        recordsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Card", HeaderText = "Card", ReadOnly = true, Width = 55 });
        recordsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Channel", HeaderText = "Ch", ReadOnly = true, Width = 55 });
        recordsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "LoggerMode", HeaderText = "Logger", ReadOnly = true, Width = 60 });
        recordsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "HardwareFunction", HeaderText = "Function", ReadOnly = true, Width = 70 });
        recordsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Pair", HeaderText = "Pair", ReadOnly = true, Width = 60 });
        recordsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "OffText", HeaderText = "OFF text", ReadOnly = true, Width = 150 });
        recordsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "OnText", HeaderText = "ON text", ReadOnly = true, Width = 150 });
        recordsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "RawOffset", HeaderText = "Offset", ReadOnly = true, Width = 80 });

        detailsTable.ColumnCount = 4;
        detailsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125F));
        detailsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        detailsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125F));
        detailsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        detailsTable.Dock = DockStyle.Fill;
        detailsTable.Padding = new Padding(8);
        detailsTable.RowCount = 5;
        for (int i = 0; i < 5; i++) detailsTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));

        Label recordLabel = new Label { Text = "Record / event", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        Label typeLabel = new Label { Text = "Type / flag", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        Label nameLabel = new Label { Text = "Name", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        Label pairLabel = new Label { Text = "Pair", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        Label cardLabel = new Label { Text = "Card / channel", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        Label loggerLabel = new Label { Text = "Logger / function", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        Label offLabel = new Label { Text = "OFF text", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        Label onLabel = new Label { Text = "ON text", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        Label offsetLabel = new Label { Text = "Record offset", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        Label rawLabel = new Label { Text = "Raw record bytes", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };

        detailRecordTextBox.Dock = DockStyle.Fill; detailRecordTextBox.ReadOnly = true;
        detailTypeTextBox.Dock = DockStyle.Fill; detailTypeTextBox.ReadOnly = true;
        detailNameTextBox.Dock = DockStyle.Fill; detailNameTextBox.ReadOnly = true;
        detailPairTextBox.Dock = DockStyle.Fill; detailPairTextBox.ReadOnly = true;
        detailCardChannelTextBox.Dock = DockStyle.Fill; detailCardChannelTextBox.ReadOnly = true;
        detailLoggerFunctionTextBox.Dock = DockStyle.Fill; detailLoggerFunctionTextBox.ReadOnly = true;
        detailOffTextBox.Dock = DockStyle.Fill; detailOffTextBox.ReadOnly = true;
        detailOnTextBox.Dock = DockStyle.Fill; detailOnTextBox.ReadOnly = true;
        detailOffsetTextBox.Dock = DockStyle.Fill; detailOffsetTextBox.ReadOnly = true;
        detailRawTextBox.Dock = DockStyle.Fill; detailRawTextBox.ReadOnly = true; detailRawTextBox.Font = new Font("Consolas", 9F); detailRawTextBox.WordWrap = false;

        detailsTable.Controls.Add(recordLabel, 0, 0); detailsTable.Controls.Add(detailRecordTextBox, 1, 0);
        detailsTable.Controls.Add(typeLabel, 2, 0); detailsTable.Controls.Add(detailTypeTextBox, 3, 0);
        detailsTable.Controls.Add(nameLabel, 0, 1); detailsTable.Controls.Add(detailNameTextBox, 1, 1);
        detailsTable.Controls.Add(pairLabel, 2, 1); detailsTable.Controls.Add(detailPairTextBox, 3, 1);
        detailsTable.Controls.Add(cardLabel, 0, 2); detailsTable.Controls.Add(detailCardChannelTextBox, 1, 2);
        detailsTable.Controls.Add(loggerLabel, 2, 2); detailsTable.Controls.Add(detailLoggerFunctionTextBox, 3, 2);
        detailsTable.Controls.Add(offLabel, 0, 3); detailsTable.Controls.Add(detailOffTextBox, 1, 3);
        detailsTable.Controls.Add(onLabel, 2, 3); detailsTable.Controls.Add(detailOnTextBox, 3, 3);
        detailsTable.Controls.Add(offsetLabel, 0, 4); detailsTable.Controls.Add(detailOffsetTextBox, 1, 4);
        detailsTable.Controls.Add(rawLabel, 2, 4); detailsTable.Controls.Add(detailRawTextBox, 3, 4);

        headerTab.Controls.Add(headerGrid);
        headerTab.Location = new Point(4, 24);
        headerTab.Name = "headerTab";
        headerTab.Padding = new Padding(3);
        headerTab.Text = "Header";
        headerTab.UseVisualStyleBackColor = true;

        headerGrid.AllowUserToAddRows = false;
        headerGrid.AllowUserToDeleteRows = false;
        headerGrid.AutoGenerateColumns = false;
        headerGrid.BackgroundColor = SystemColors.Window;
        headerGrid.Dock = DockStyle.Fill;
        headerGrid.Name = "headerGrid";
        headerGrid.ReadOnly = true;
        headerGrid.RowHeadersVisible = false;
        headerGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        headerGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Offset", HeaderText = "Offset", ReadOnly = true, Width = 90 });
        headerGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Meaning", HeaderText = "Meaning", ReadOnly = true, Width = 260 });
        headerGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "RawHex", HeaderText = "Raw bytes", ReadOnly = true, Width = 280 });
        headerGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Decoded", HeaderText = "Decoded / preview", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });

        hexTab.Controls.Add(hexGrid);
        hexTab.Location = new Point(4, 24);
        hexTab.Name = "hexTab";
        hexTab.Padding = new Padding(3);
        hexTab.Text = "Hex";
        hexTab.UseVisualStyleBackColor = true;

        hexGrid.AllowUserToAddRows = false;
        hexGrid.AllowUserToDeleteRows = false;
        hexGrid.AutoGenerateColumns = false;
        hexGrid.BackgroundColor = SystemColors.Window;
        hexGrid.Dock = DockStyle.Fill;
        hexGrid.Font = new Font("Consolas", 9F);
        hexGrid.Name = "hexGrid";
        hexGrid.ReadOnly = true;
        hexGrid.RowHeadersVisible = false;
        hexGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        hexGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Offset", HeaderText = "Offset", ReadOnly = true, Width = 90 });
        hexGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Hex", HeaderText = "Hex bytes", ReadOnly = true, Width = 630 });
        hexGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Ascii", HeaderText = "ASCII", ReadOnly = true, Width = 220 });

        validationTab.Controls.Add(validationList);
        validationTab.Location = new Point(4, 24);
        validationTab.Name = "validationTab";
        validationTab.Padding = new Padding(3);
        validationTab.Text = "Validation";
        validationTab.UseVisualStyleBackColor = true;

        validationList.Dock = DockStyle.Fill;
        validationList.FullRowSelect = true;
        validationList.GridLines = true;
        validationList.Name = "validationList";
        validationList.View = View.Details;
        validationList.Columns.Add("Severity", 100);
        validationList.Columns.Add("Message", 1200);

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1500, 900);
        Controls.Add(tabs);
        Controls.Add(toolStrip);
        Controls.Add(menuStrip);
        Controls.Add(statusStrip);
        MainMenuStrip = menuStrip;
        MinimumSize = new Size(1100, 700);
        Name = "MainForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "CCF Editor / Creator";

        menuStrip.ResumeLayout(false);
        menuStrip.PerformLayout();
        toolStrip.ResumeLayout(false);
        toolStrip.PerformLayout();
        statusStrip.ResumeLayout(false);
        statusStrip.PerformLayout();
        tabs.ResumeLayout(false);
        recordsTab.ResumeLayout(false);
        recordsSplit.Panel1.ResumeLayout(false);
        recordsSplit.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)recordsSplit).EndInit();
        recordsSplit.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)recordsGrid).EndInit();
        detailsTable.ResumeLayout(false);
        detailsTable.PerformLayout();
        headerTab.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)headerGrid).EndInit();
        hexTab.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)hexGrid).EndInit();
        validationTab.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }
}
