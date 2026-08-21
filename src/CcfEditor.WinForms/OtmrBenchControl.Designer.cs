namespace CcfEditor.WinForms;

partial class OtmrBenchControl
{
    private System.ComponentModel.IContainer? components = null;
    private TableLayoutPanel rootLayout = null!;
    private FlowLayoutPanel commandPanel = null!;
    private Label profileLabel = null!;
    private Label connectorLabel = null!;
    private ComboBox connectorComboBox = null!;
    private Button loadProfileButton = null!;
    private Button refreshCcfButton = null!;
    private Button armSelectedButton = null!;
    private Button stopObservationButton = null!;
    private Button clearObservationsButton = null!;
    private Label profileStatusLabel = null!;
    private Label ccfStatusLabel = null!;
    private Label decoderStatusLabel = null!;
    private Label coverageLabel = null!;
    private SplitContainer benchSplit = null!;
    private DataGridView benchGrid = null!;
    private DataGridViewTextBoxColumn pinColumn = null!;
    private DataGridViewTextBoxColumn roleColumn = null!;
    private DataGridViewTextBoxColumn mioColumn = null!;
    private DataGridViewTextBoxColumn benchChannelColumn = null!;
    private DataGridViewTextBoxColumn expectedFunctionColumn = null!;
    private DataGridViewTextBoxColumn safetyColumn = null!;
    private DataGridViewTextBoxColumn expectedRecordsColumn = null!;
    private DataGridViewTextBoxColumn expectedCcfColumn = null!;
    private DataGridViewTextBoxColumn currentCcfColumn = null!;
    private DataGridViewTextBoxColumn ccfCheckColumn = null!;
    private DataGridViewTextBoxColumn liveObservedColumn = null!;
    private DataGridViewTextBoxColumn liveValueColumn = null!;
    private DataGridViewTextBoxColumn resultColumn = null!;
    private GroupBox detailsGroupBox = null!;
    private TextBox detailsTextBox = null!;
    private Label statusLabel = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components is not null)
            components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        rootLayout = new TableLayoutPanel();
        commandPanel = new FlowLayoutPanel();
        profileLabel = new Label();
        connectorLabel = new Label();
        connectorComboBox = new ComboBox();
        loadProfileButton = new Button();
        refreshCcfButton = new Button();
        armSelectedButton = new Button();
        stopObservationButton = new Button();
        clearObservationsButton = new Button();
        profileStatusLabel = new Label();
        ccfStatusLabel = new Label();
        decoderStatusLabel = new Label();
        coverageLabel = new Label();
        benchSplit = new SplitContainer();
        benchGrid = new DataGridView();
        pinColumn = new DataGridViewTextBoxColumn();
        roleColumn = new DataGridViewTextBoxColumn();
        mioColumn = new DataGridViewTextBoxColumn();
        benchChannelColumn = new DataGridViewTextBoxColumn();
        expectedFunctionColumn = new DataGridViewTextBoxColumn();
        safetyColumn = new DataGridViewTextBoxColumn();
        expectedRecordsColumn = new DataGridViewTextBoxColumn();
        expectedCcfColumn = new DataGridViewTextBoxColumn();
        currentCcfColumn = new DataGridViewTextBoxColumn();
        ccfCheckColumn = new DataGridViewTextBoxColumn();
        liveObservedColumn = new DataGridViewTextBoxColumn();
        liveValueColumn = new DataGridViewTextBoxColumn();
        resultColumn = new DataGridViewTextBoxColumn();
        detailsGroupBox = new GroupBox();
        detailsTextBox = new TextBox();
        statusLabel = new Label();
        rootLayout.SuspendLayout();
        commandPanel.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)benchSplit).BeginInit();
        benchSplit.Panel1.SuspendLayout();
        benchSplit.Panel2.SuspendLayout();
        benchSplit.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)benchGrid).BeginInit();
        detailsGroupBox.SuspendLayout();
        SuspendLayout();
        // 
        // rootLayout
        // 
        rootLayout.ColumnCount = 1;
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        rootLayout.Controls.Add(commandPanel, 0, 0);
        rootLayout.Controls.Add(coverageLabel, 0, 1);
        rootLayout.Controls.Add(benchSplit, 0, 2);
        rootLayout.Controls.Add(statusLabel, 0, 3);
        rootLayout.Dock = DockStyle.Fill;
        rootLayout.Location = new Point(0, 0);
        rootLayout.Name = "rootLayout";
        rootLayout.Padding = new Padding(8);
        rootLayout.RowCount = 4;
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        rootLayout.Size = new Size(1450, 760);
        // 
        // commandPanel
        // 
        commandPanel.Controls.Add(profileLabel);
        commandPanel.Controls.Add(connectorLabel);
        commandPanel.Controls.Add(connectorComboBox);
        commandPanel.Controls.Add(loadProfileButton);
        commandPanel.Controls.Add(refreshCcfButton);
        commandPanel.Controls.Add(armSelectedButton);
        commandPanel.Controls.Add(stopObservationButton);
        commandPanel.Controls.Add(clearObservationsButton);
        commandPanel.Controls.Add(profileStatusLabel);
        commandPanel.Controls.Add(ccfStatusLabel);
        commandPanel.Controls.Add(decoderStatusLabel);
        commandPanel.Dock = DockStyle.Fill;
        commandPanel.Name = "commandPanel";
        commandPanel.Padding = new Padding(4);
        commandPanel.WrapContents = true;
        // 
        // profileLabel
        // 
        profileLabel.AutoSize = true;
        profileLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        profileLabel.Margin = new Padding(3, 9, 10, 3);
        profileLabel.Text = "Profile: Class 171";
        // 
        // connectorLabel
        // 
        connectorLabel.AutoSize = true;
        connectorLabel.Margin = new Padding(3, 9, 3, 3);
        connectorLabel.Text = "Connector:";
        // 
        // connectorComboBox
        // 
        connectorComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        connectorComboBox.Margin = new Padding(3, 4, 10, 3);
        connectorComboBox.Name = "connectorComboBox";
        connectorComboBox.Size = new Size(80, 23);
        connectorComboBox.SelectedIndexChanged += ConnectorComboBox_SelectedIndexChanged;
        // 
        // loadProfileButton
        // 
        loadProfileButton.AutoSize = true;
        loadProfileButton.Margin = new Padding(3, 3, 6, 3);
        loadProfileButton.Name = "loadProfileButton";
        loadProfileButton.Text = "Load Pin Map...";
        loadProfileButton.Click += LoadProfileButton_Click;
        // 
        // refreshCcfButton
        // 
        refreshCcfButton.AutoSize = true;
        refreshCcfButton.Margin = new Padding(3, 3, 12, 3);
        refreshCcfButton.Name = "refreshCcfButton";
        refreshCcfButton.Text = "Refresh CCF";
        refreshCcfButton.Click += RefreshCcfButton_Click;
        // 
        // armSelectedButton
        // 
        armSelectedButton.AutoSize = true;
        armSelectedButton.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        armSelectedButton.Margin = new Padding(3, 3, 6, 3);
        armSelectedButton.Name = "armSelectedButton";
        armSelectedButton.Text = "Arm Selected Pin";
        armSelectedButton.Click += ArmSelectedButton_Click;
        // 
        // stopObservationButton
        // 
        stopObservationButton.AutoSize = true;
        stopObservationButton.Enabled = false;
        stopObservationButton.Margin = new Padding(3, 3, 6, 3);
        stopObservationButton.Name = "stopObservationButton";
        stopObservationButton.Text = "Stop Observation";
        stopObservationButton.Click += StopObservationButton_Click;
        // 
        // clearObservationsButton
        // 
        clearObservationsButton.AutoSize = true;
        clearObservationsButton.Margin = new Padding(3, 3, 12, 3);
        clearObservationsButton.Name = "clearObservationsButton";
        clearObservationsButton.Text = "Clear Observations";
        clearObservationsButton.Click += ClearObservationsButton_Click;
        // 
        // profileStatusLabel
        // 
        profileStatusLabel.AutoSize = true;
        profileStatusLabel.Margin = new Padding(3, 9, 14, 3);
        profileStatusLabel.Name = "profileStatusLabel";
        profileStatusLabel.Text = "Pin map: not loaded";
        // 
        // ccfStatusLabel
        // 
        ccfStatusLabel.AutoSize = true;
        ccfStatusLabel.Margin = new Padding(3, 9, 14, 3);
        ccfStatusLabel.Name = "ccfStatusLabel";
        ccfStatusLabel.Text = "CCF: none loaded";
        // 
        // decoderStatusLabel
        // 
        decoderStatusLabel.AutoSize = true;
        decoderStatusLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        decoderStatusLabel.Margin = new Padding(3, 9, 3, 3);
        decoderStatusLabel.Name = "decoderStatusLabel";
        decoderStatusLabel.Text = "Live record detection: WAITING FOR VERIFIED DECODER";
        // 
        // coverageLabel
        // 
        coverageLabel.Dock = DockStyle.Fill;
        coverageLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        coverageLabel.Name = "coverageLabel";
        coverageLabel.Padding = new Padding(6);
        coverageLabel.Text = "Mapping coverage";
        coverageLabel.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // benchSplit
        // 
        benchSplit.Dock = DockStyle.Fill;
        benchSplit.Location = new Point(11, 141);
        benchSplit.Name = "benchSplit";
        benchSplit.Panel1.Controls.Add(benchGrid);
        benchSplit.Panel2.Controls.Add(detailsGroupBox);
        benchSplit.SplitterDistance = 1080;
        // 
        // benchGrid
        // 
        benchGrid.AllowUserToAddRows = false;
        benchGrid.AllowUserToDeleteRows = false;
        benchGrid.AllowUserToOrderColumns = true;
        benchGrid.AutoGenerateColumns = false;
        benchGrid.BackgroundColor = SystemColors.Window;
        benchGrid.Columns.AddRange(new DataGridViewColumn[] { pinColumn, roleColumn, mioColumn, benchChannelColumn, expectedFunctionColumn, safetyColumn, expectedRecordsColumn, expectedCcfColumn, currentCcfColumn, ccfCheckColumn, liveObservedColumn, liveValueColumn, resultColumn });
        benchGrid.Dock = DockStyle.Fill;
        benchGrid.MultiSelect = false;
        benchGrid.Name = "benchGrid";
        benchGrid.ReadOnly = true;
        benchGrid.RowHeadersVisible = false;
        benchGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        benchGrid.SelectionChanged += BenchGrid_SelectionChanged;
        // 
        // grid columns
        // 
        pinColumn.HeaderText = "Pin"; pinColumn.Name = "pinColumn"; pinColumn.ReadOnly = true; pinColumn.Width = 45;
        roleColumn.HeaderText = "Role"; roleColumn.Name = "roleColumn"; roleColumn.ReadOnly = true; roleColumn.Width = 125;
        mioColumn.HeaderText = "MIO"; mioColumn.Name = "mioColumn"; mioColumn.ReadOnly = true; mioColumn.Width = 70;
        benchChannelColumn.HeaderText = "Ch"; benchChannelColumn.Name = "benchChannelColumn"; benchChannelColumn.ReadOnly = true; benchChannelColumn.Width = 80;
        expectedFunctionColumn.HeaderText = "Physical / expected function"; expectedFunctionColumn.Name = "expectedFunctionColumn"; expectedFunctionColumn.ReadOnly = true; expectedFunctionColumn.Width = 180;
        safetyColumn.HeaderText = "Safety / bench instruction"; safetyColumn.Name = "safetyColumn"; safetyColumn.ReadOnly = true; safetyColumn.Width = 245;
        expectedRecordsColumn.HeaderText = "Ref records"; expectedRecordsColumn.Name = "expectedRecordsColumn"; expectedRecordsColumn.ReadOnly = true; expectedRecordsColumn.Width = 85;
        expectedCcfColumn.HeaderText = "Ref CCF"; expectedCcfColumn.Name = "expectedCcfColumn"; expectedCcfColumn.ReadOnly = true; expectedCcfColumn.Width = 100;
        currentCcfColumn.HeaderText = "Current opened CCF"; currentCcfColumn.Name = "currentCcfColumn"; currentCcfColumn.ReadOnly = true; currentCcfColumn.Width = 270;
        ccfCheckColumn.HeaderText = "CCF check"; ccfCheckColumn.Name = "ccfCheckColumn"; ccfCheckColumn.ReadOnly = true; ccfCheckColumn.Width = 105;
        liveObservedColumn.HeaderText = "Live observed record(s)"; liveObservedColumn.Name = "liveObservedColumn"; liveObservedColumn.ReadOnly = true; liveObservedColumn.Width = 250;
        liveValueColumn.HeaderText = "Live value"; liveValueColumn.Name = "liveValueColumn"; liveValueColumn.ReadOnly = true; liveValueColumn.Width = 90;
        resultColumn.HeaderText = "Bench result"; resultColumn.Name = "resultColumn"; resultColumn.ReadOnly = true; resultColumn.Width = 120;
        // 
        // detailsGroupBox
        // 
        detailsGroupBox.Controls.Add(detailsTextBox);
        detailsGroupBox.Dock = DockStyle.Fill;
        detailsGroupBox.Name = "detailsGroupBox";
        detailsGroupBox.Padding = new Padding(10);
        detailsGroupBox.Text = "Selected pin / mapping evidence";
        // 
        // detailsTextBox
        // 
        detailsTextBox.BackColor = SystemColors.Window;
        detailsTextBox.BorderStyle = BorderStyle.None;
        detailsTextBox.Dock = DockStyle.Fill;
        detailsTextBox.Font = new Font("Segoe UI", 9F);
        detailsTextBox.Multiline = true;
        detailsTextBox.Name = "detailsTextBox";
        detailsTextBox.ReadOnly = true;
        detailsTextBox.ScrollBars = ScrollBars.Vertical;
        detailsTextBox.Text = "Select a pin row to see mapping and safety details.";
        // 
        // statusLabel
        // 
        statusLabel.Dock = DockStyle.Fill;
        statusLabel.Name = "statusLabel";
        statusLabel.Padding = new Padding(6, 0, 0, 0);
        statusLabel.Text = "Observation-only bench tool. It does not apply voltage and does not transmit OTMR commands.";
        statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // OtmrBenchControl
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        Controls.Add(rootLayout);
        Name = "OtmrBenchControl";
        Size = new Size(1450, 760);
        rootLayout.ResumeLayout(false);
        commandPanel.ResumeLayout(false);
        commandPanel.PerformLayout();
        benchSplit.Panel1.ResumeLayout(false);
        benchSplit.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)benchSplit).EndInit();
        benchSplit.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)benchGrid).EndInit();
        detailsGroupBox.ResumeLayout(false);
        detailsGroupBox.PerformLayout();
        ResumeLayout(false);
    }
}
