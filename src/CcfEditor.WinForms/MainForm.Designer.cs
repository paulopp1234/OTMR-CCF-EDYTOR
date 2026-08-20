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
    private TableLayoutPanel recordsTopTable = null!;
    private DataGridView recordsGrid = null!;
    private DataGridViewTextBoxColumn recordColumn = null!;
    private DataGridViewTextBoxColumn eventColumn = null!;
    private DataGridViewTextBoxColumn typeColumn = null!;
    private DataGridViewTextBoxColumn flagColumn = null!;
    private DataGridViewTextBoxColumn nameColumn = null!;
    private DataGridViewTextBoxColumn colourColumn = null!;
    private DataGridViewTextBoxColumn cardColumn = null!;
    private DataGridViewTextBoxColumn channelColumn = null!;
    private DataGridViewTextBoxColumn loggerColumn = null!;
    private DataGridViewTextBoxColumn functionColumn = null!;
    private DataGridViewTextBoxColumn pairColumn = null!;
    private DataGridViewTextBoxColumn offColumn = null!;
    private DataGridViewTextBoxColumn onColumn = null!;
    private DataGridViewTextBoxColumn rawOffsetColumn = null!;
    private GroupBox fieldHelpGroupBox = null!;
    private TextBox fieldHelpTextBox = null!;
    private TableLayoutPanel detailsTable = null!;
    private Label recordLabel = null!;
    private Label typeLabel = null!;
    private Label nameLabel = null!;
    private Label pairLabel = null!;
    private Label cardLabel = null!;
    private Label loggerLabel = null!;
    private Label offLabel = null!;
    private Label onLabel = null!;
    private Label offsetLabel = null!;
    private Label rawLabel = null!;
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
    private DataGridViewTextBoxColumn headerOffsetColumn = null!;
    private DataGridViewTextBoxColumn headerMeaningColumn = null!;
    private DataGridViewTextBoxColumn headerRawHexColumn = null!;
    private DataGridViewTextBoxColumn headerDecodedColumn = null!;
    private DataGridViewCheckBoxColumn headerEditableColumn = null!;
    private DataGridView hexGrid = null!;
    private DataGridViewTextBoxColumn hexOffsetColumn = null!;
    private DataGridViewTextBoxColumn hexBytesColumn = null!;
    private DataGridViewTextBoxColumn hexAsciiColumn = null!;
    private ListView validationList = null!;
    private ColumnHeader validationSeverityColumn = null!;
    private ColumnHeader validationMessageColumn = null!;

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
        recordsSplit = new SplitContainer();
        recordsTopTable = new TableLayoutPanel();
        recordsGrid = new DataGridView();
        recordColumn = new DataGridViewTextBoxColumn();
        eventColumn = new DataGridViewTextBoxColumn();
        typeColumn = new DataGridViewTextBoxColumn();
        flagColumn = new DataGridViewTextBoxColumn();
        nameColumn = new DataGridViewTextBoxColumn();
        colourColumn = new DataGridViewTextBoxColumn();
        cardColumn = new DataGridViewTextBoxColumn();
        channelColumn = new DataGridViewTextBoxColumn();
        loggerColumn = new DataGridViewTextBoxColumn();
        functionColumn = new DataGridViewTextBoxColumn();
        pairColumn = new DataGridViewTextBoxColumn();
        offColumn = new DataGridViewTextBoxColumn();
        onColumn = new DataGridViewTextBoxColumn();
        rawOffsetColumn = new DataGridViewTextBoxColumn();
        fieldHelpGroupBox = new GroupBox();
        fieldHelpTextBox = new TextBox();
        detailsTable = new TableLayoutPanel();
        recordLabel = new Label();
        typeLabel = new Label();
        nameLabel = new Label();
        pairLabel = new Label();
        cardLabel = new Label();
        loggerLabel = new Label();
        offLabel = new Label();
        onLabel = new Label();
        offsetLabel = new Label();
        rawLabel = new Label();
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
        headerTab = new TabPage();
        headerGrid = new DataGridView();
        headerOffsetColumn = new DataGridViewTextBoxColumn();
        headerMeaningColumn = new DataGridViewTextBoxColumn();
        headerRawHexColumn = new DataGridViewTextBoxColumn();
        headerDecodedColumn = new DataGridViewTextBoxColumn();
        headerEditableColumn = new DataGridViewCheckBoxColumn();
        hexTab = new TabPage();
        hexGrid = new DataGridView();
        hexOffsetColumn = new DataGridViewTextBoxColumn();
        hexBytesColumn = new DataGridViewTextBoxColumn();
        hexAsciiColumn = new DataGridViewTextBoxColumn();
        validationTab = new TabPage();
        validationList = new ListView();
        validationSeverityColumn = new ColumnHeader();
        validationMessageColumn = new ColumnHeader();
        menuStrip.SuspendLayout();
        toolStrip.SuspendLayout();
        statusStrip.SuspendLayout();
        tabs.SuspendLayout();
        recordsTab.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)recordsSplit).BeginInit();
        recordsSplit.Panel1.SuspendLayout();
        recordsSplit.Panel2.SuspendLayout();
        recordsSplit.SuspendLayout();
        recordsTopTable.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)recordsGrid).BeginInit();
        fieldHelpGroupBox.SuspendLayout();
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
        saveCopyMenuItem.ShortcutKeys = Keys.Control | Keys.Shift | Keys.S;
        saveCopyMenuItem.Text = "Save CCF &As...";
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
        saveCopyButton.Text = "Save CCF As";
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
        fileStatusLabel.Text = "No CCF loaded | v0.2";

        springStatusLabel.Name = "springStatusLabel";
        springStatusLabel.Spring = true;

        shaStatusLabel.Name = "shaStatusLabel";
        shaStatusLabel.Text = "Working SHA-256: -";

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
        recordsTab.Text = "Records - edit known fields in grid";
        recordsTab.UseVisualStyleBackColor = true;

        recordsSplit.Dock = DockStyle.Fill;
        recordsSplit.Location = new Point(3, 3);
        recordsSplit.Name = "recordsSplit";
        recordsSplit.Orientation = Orientation.Horizontal;
        recordsSplit.Panel1.Controls.Add(recordsTopTable);
        recordsSplit.Panel2.Controls.Add(detailsTable);
        recordsSplit.SplitterDistance = 520;

        recordsTopTable.ColumnCount = 2;
        recordsTopTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        recordsTopTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 420F));
        recordsTopTable.Controls.Add(recordsGrid, 0, 0);
        recordsTopTable.Controls.Add(fieldHelpGroupBox, 1, 0);
        recordsTopTable.Dock = DockStyle.Fill;
        recordsTopTable.Name = "recordsTopTable";
        recordsTopTable.RowCount = 1;
        recordsTopTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        recordsGrid.AllowUserToAddRows = false;
        recordsGrid.AllowUserToDeleteRows = false;
        recordsGrid.AllowUserToOrderColumns = true;
        recordsGrid.AutoGenerateColumns = false;
        recordsGrid.BackgroundColor = SystemColors.Window;
        recordsGrid.Columns.AddRange(new DataGridViewColumn[] { recordColumn, eventColumn, typeColumn, flagColumn, nameColumn, colourColumn, cardColumn, channelColumn, loggerColumn, functionColumn, pairColumn, offColumn, onColumn, rawOffsetColumn });
        recordsGrid.Dock = DockStyle.Fill;
        recordsGrid.MultiSelect = false;
        recordsGrid.Name = "recordsGrid";
        recordsGrid.RowHeadersVisible = false;
        recordsGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        recordsGrid.CellBeginEdit += RecordsGrid_CellBeginEdit;
        recordsGrid.CellEndEdit += RecordsGrid_CellEndEdit;
        recordsGrid.CellEnter += RecordsGrid_CellEnter;
        recordsGrid.CellValidating += RecordsGrid_CellValidating;
        recordsGrid.DataError += RecordsGrid_DataError;
        recordsGrid.SelectionChanged += RecordsGrid_SelectionChanged;

        recordColumn.DataPropertyName = "Record";
        recordColumn.HeaderText = "Record";
        recordColumn.Name = "recordColumn";
        recordColumn.ReadOnly = true;
        recordColumn.Width = 65;

        eventColumn.DataPropertyName = "EventIndex";
        eventColumn.HeaderText = "Event";
        eventColumn.Name = "eventColumn";
        eventColumn.ReadOnly = true;
        eventColumn.Width = 65;

        typeColumn.DataPropertyName = "Type";
        typeColumn.HeaderText = "Type*";
        typeColumn.Name = "typeColumn";
        typeColumn.Width = 50;

        flagColumn.DataPropertyName = "Flag";
        flagColumn.HeaderText = "Flag";
        flagColumn.Name = "flagColumn";
        flagColumn.ReadOnly = true;
        flagColumn.Width = 50;

        nameColumn.DataPropertyName = "Name";
        nameColumn.HeaderText = "Name*";
        nameColumn.Name = "nameColumn";
        nameColumn.Width = 180;

        colourColumn.DataPropertyName = "ColourHex";
        colourColumn.HeaderText = "Colour raw*";
        colourColumn.Name = "colourColumn";
        colourColumn.Width = 90;

        cardColumn.DataPropertyName = "Card";
        cardColumn.HeaderText = "Card*";
        cardColumn.Name = "cardColumn";
        cardColumn.Width = 55;

        channelColumn.DataPropertyName = "Channel";
        channelColumn.HeaderText = "Ch*";
        channelColumn.Name = "channelColumn";
        channelColumn.Width = 55;

        loggerColumn.DataPropertyName = "LoggerMode";
        loggerColumn.HeaderText = "Logger*";
        loggerColumn.Name = "loggerColumn";
        loggerColumn.Width = 60;

        functionColumn.DataPropertyName = "HardwareFunction";
        functionColumn.HeaderText = "Function*";
        functionColumn.Name = "functionColumn";
        functionColumn.Width = 70;

        pairColumn.DataPropertyName = "Pair";
        pairColumn.HeaderText = "Pair*";
        pairColumn.Name = "pairColumn";
        pairColumn.Width = 60;

        offColumn.DataPropertyName = "OffText";
        offColumn.HeaderText = "OFF text*";
        offColumn.Name = "offColumn";
        offColumn.Width = 150;

        onColumn.DataPropertyName = "OnText";
        onColumn.HeaderText = "ON text*";
        onColumn.Name = "onColumn";
        onColumn.Width = 150;

        rawOffsetColumn.DataPropertyName = "RawOffset";
        rawOffsetColumn.HeaderText = "Offset";
        rawOffsetColumn.Name = "rawOffsetColumn";
        rawOffsetColumn.ReadOnly = true;
        rawOffsetColumn.Width = 80;

        fieldHelpGroupBox.Controls.Add(fieldHelpTextBox);
        fieldHelpGroupBox.Dock = DockStyle.Fill;
        fieldHelpGroupBox.Margin = new Padding(8, 3, 3, 3);
        fieldHelpGroupBox.Name = "fieldHelpGroupBox";
        fieldHelpGroupBox.Padding = new Padding(10);
        fieldHelpGroupBox.Text = "Field description / schema help";

        fieldHelpTextBox.BackColor = SystemColors.Window;
        fieldHelpTextBox.BorderStyle = BorderStyle.None;
        fieldHelpTextBox.Dock = DockStyle.Fill;
        fieldHelpTextBox.Font = new Font("Segoe UI", 10F);
        fieldHelpTextBox.Multiline = true;
        fieldHelpTextBox.Name = "fieldHelpTextBox";
        fieldHelpTextBox.ReadOnly = true;
        fieldHelpTextBox.ScrollBars = ScrollBars.Vertical;
        fieldHelpTextBox.Text = "Click a cell in the Records grid to see what that CCF field means.\r\n\r\nThe current value shown here comes from the CCF you opened. Description text is schema/help information only.";

        detailsTable.ColumnCount = 4;
        detailsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125F));
        detailsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        detailsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125F));
        detailsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        detailsTable.Controls.Add(recordLabel, 0, 0);
        detailsTable.Controls.Add(detailRecordTextBox, 1, 0);
        detailsTable.Controls.Add(typeLabel, 2, 0);
        detailsTable.Controls.Add(detailTypeTextBox, 3, 0);
        detailsTable.Controls.Add(nameLabel, 0, 1);
        detailsTable.Controls.Add(detailNameTextBox, 1, 1);
        detailsTable.Controls.Add(pairLabel, 2, 1);
        detailsTable.Controls.Add(detailPairTextBox, 3, 1);
        detailsTable.Controls.Add(cardLabel, 0, 2);
        detailsTable.Controls.Add(detailCardChannelTextBox, 1, 2);
        detailsTable.Controls.Add(loggerLabel, 2, 2);
        detailsTable.Controls.Add(detailLoggerFunctionTextBox, 3, 2);
        detailsTable.Controls.Add(offLabel, 0, 3);
        detailsTable.Controls.Add(detailOffTextBox, 1, 3);
        detailsTable.Controls.Add(onLabel, 2, 3);
        detailsTable.Controls.Add(detailOnTextBox, 3, 3);
        detailsTable.Controls.Add(offsetLabel, 0, 4);
        detailsTable.Controls.Add(detailOffsetTextBox, 1, 4);
        detailsTable.Controls.Add(rawLabel, 2, 4);
        detailsTable.Controls.Add(detailRawTextBox, 3, 4);
        detailsTable.Dock = DockStyle.Fill;
        detailsTable.Padding = new Padding(8);
        detailsTable.RowCount = 5;
        detailsTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
        detailsTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
        detailsTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
        detailsTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
        detailsTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));

        recordLabel.Dock = DockStyle.Fill;
        recordLabel.Text = "Record / event";
        recordLabel.TextAlign = ContentAlignment.MiddleLeft;
        typeLabel.Dock = DockStyle.Fill;
        typeLabel.Text = "Type / flag";
        typeLabel.TextAlign = ContentAlignment.MiddleLeft;
        nameLabel.Dock = DockStyle.Fill;
        nameLabel.Text = "Name";
        nameLabel.TextAlign = ContentAlignment.MiddleLeft;
        pairLabel.Dock = DockStyle.Fill;
        pairLabel.Text = "Pair";
        pairLabel.TextAlign = ContentAlignment.MiddleLeft;
        cardLabel.Dock = DockStyle.Fill;
        cardLabel.Text = "Card / channel";
        cardLabel.TextAlign = ContentAlignment.MiddleLeft;
        loggerLabel.Dock = DockStyle.Fill;
        loggerLabel.Text = "Logger / function";
        loggerLabel.TextAlign = ContentAlignment.MiddleLeft;
        offLabel.Dock = DockStyle.Fill;
        offLabel.Text = "OFF text";
        offLabel.TextAlign = ContentAlignment.MiddleLeft;
        onLabel.Dock = DockStyle.Fill;
        onLabel.Text = "ON text";
        onLabel.TextAlign = ContentAlignment.MiddleLeft;
        offsetLabel.Dock = DockStyle.Fill;
        offsetLabel.Text = "Record offset";
        offsetLabel.TextAlign = ContentAlignment.MiddleLeft;
        rawLabel.Dock = DockStyle.Fill;
        rawLabel.Text = "Raw record bytes";
        rawLabel.TextAlign = ContentAlignment.MiddleLeft;

        detailRecordTextBox.Dock = DockStyle.Fill;
        detailRecordTextBox.ReadOnly = true;
        detailTypeTextBox.Dock = DockStyle.Fill;
        detailTypeTextBox.ReadOnly = true;
        detailNameTextBox.Dock = DockStyle.Fill;
        detailNameTextBox.ReadOnly = true;
        detailPairTextBox.Dock = DockStyle.Fill;
        detailPairTextBox.ReadOnly = true;
        detailCardChannelTextBox.Dock = DockStyle.Fill;
        detailCardChannelTextBox.ReadOnly = true;
        detailLoggerFunctionTextBox.Dock = DockStyle.Fill;
        detailLoggerFunctionTextBox.ReadOnly = true;
        detailOffTextBox.Dock = DockStyle.Fill;
        detailOffTextBox.ReadOnly = true;
        detailOnTextBox.Dock = DockStyle.Fill;
        detailOnTextBox.ReadOnly = true;
        detailOffsetTextBox.Dock = DockStyle.Fill;
        detailOffsetTextBox.ReadOnly = true;
        detailRawTextBox.Dock = DockStyle.Fill;
        detailRawTextBox.Font = new Font("Consolas", 9F);
        detailRawTextBox.ReadOnly = true;
        detailRawTextBox.WordWrap = false;

        headerTab.Controls.Add(headerGrid);
        headerTab.Location = new Point(4, 24);
        headerTab.Name = "headerTab";
        headerTab.Padding = new Padding(3);
        headerTab.Text = "Header - edit supported Decoded cells";
        headerTab.UseVisualStyleBackColor = true;

        headerGrid.AllowUserToAddRows = false;
        headerGrid.AllowUserToDeleteRows = false;
        headerGrid.AutoGenerateColumns = false;
        headerGrid.BackgroundColor = SystemColors.Window;
        headerGrid.Columns.AddRange(new DataGridViewColumn[] { headerOffsetColumn, headerMeaningColumn, headerRawHexColumn, headerDecodedColumn, headerEditableColumn });
        headerGrid.Dock = DockStyle.Fill;
        headerGrid.Name = "headerGrid";
        headerGrid.RowHeadersVisible = false;
        headerGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        headerGrid.CellBeginEdit += HeaderGrid_CellBeginEdit;
        headerGrid.CellEndEdit += HeaderGrid_CellEndEdit;
        headerGrid.CellValidating += HeaderGrid_CellValidating;
        headerGrid.DataError += HeaderGrid_DataError;

        headerOffsetColumn.DataPropertyName = "Offset";
        headerOffsetColumn.HeaderText = "Offset";
        headerOffsetColumn.Name = "headerOffsetColumn";
        headerOffsetColumn.ReadOnly = true;
        headerOffsetColumn.Width = 90;

        headerMeaningColumn.DataPropertyName = "Meaning";
        headerMeaningColumn.HeaderText = "Meaning";
        headerMeaningColumn.Name = "headerMeaningColumn";
        headerMeaningColumn.ReadOnly = true;
        headerMeaningColumn.Width = 260;

        headerRawHexColumn.DataPropertyName = "RawHex";
        headerRawHexColumn.HeaderText = "Raw bytes";
        headerRawHexColumn.Name = "headerRawHexColumn";
        headerRawHexColumn.ReadOnly = true;
        headerRawHexColumn.Width = 280;

        headerDecodedColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        headerDecodedColumn.DataPropertyName = "Decoded";
        headerDecodedColumn.HeaderText = "Decoded / edit value*";
        headerDecodedColumn.Name = "headerDecodedColumn";

        headerEditableColumn.DataPropertyName = "Editable";
        headerEditableColumn.HeaderText = "Editable";
        headerEditableColumn.Name = "headerEditableColumn";
        headerEditableColumn.ReadOnly = true;
        headerEditableColumn.Width = 65;

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
        hexGrid.Columns.AddRange(new DataGridViewColumn[] { hexOffsetColumn, hexBytesColumn, hexAsciiColumn });
        hexGrid.Dock = DockStyle.Fill;
        hexGrid.Font = new Font("Consolas", 9F);
        hexGrid.Name = "hexGrid";
        hexGrid.ReadOnly = true;
        hexGrid.RowHeadersVisible = false;
        hexGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;

        hexOffsetColumn.DataPropertyName = "Offset";
        hexOffsetColumn.HeaderText = "Offset";
        hexOffsetColumn.Name = "hexOffsetColumn";
        hexOffsetColumn.ReadOnly = true;
        hexOffsetColumn.Width = 90;

        hexBytesColumn.DataPropertyName = "Hex";
        hexBytesColumn.HeaderText = "Hex bytes";
        hexBytesColumn.Name = "hexBytesColumn";
        hexBytesColumn.ReadOnly = true;
        hexBytesColumn.Width = 630;

        hexAsciiColumn.DataPropertyName = "Ascii";
        hexAsciiColumn.HeaderText = "ASCII";
        hexAsciiColumn.Name = "hexAsciiColumn";
        hexAsciiColumn.ReadOnly = true;
        hexAsciiColumn.Width = 220;

        validationTab.Controls.Add(validationList);
        validationTab.Location = new Point(4, 24);
        validationTab.Name = "validationTab";
        validationTab.Padding = new Padding(3);
        validationTab.Text = "Validation";
        validationTab.UseVisualStyleBackColor = true;

        validationList.Columns.AddRange(new ColumnHeader[] { validationSeverityColumn, validationMessageColumn });
        validationList.Dock = DockStyle.Fill;
        validationList.FullRowSelect = true;
        validationList.GridLines = true;
        validationList.Name = "validationList";
        validationList.View = View.Details;

        validationSeverityColumn.Text = "Severity";
        validationSeverityColumn.Width = 100;

        validationMessageColumn.Text = "Message";
        validationMessageColumn.Width = 1200;

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
        Text = "OTMR CCF Editor / Creator v0.2";
        FormClosing += MainForm_FormClosing;
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
        recordsTopTable.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)recordsGrid).EndInit();
        fieldHelpGroupBox.ResumeLayout(false);
        fieldHelpGroupBox.PerformLayout();
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
