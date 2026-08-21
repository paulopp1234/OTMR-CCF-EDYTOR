#nullable enable

namespace CcfEditor.WinForms;

partial class OtmrBenchControl
{
    private System.ComponentModel.IContainer? components = null;
    private TableLayoutPanel rootLayout = null!;
    private FlowLayoutPanel commandPanel = null!;
    private Label profileLabel = null!;
    private Label connectorLabel = null!;
    private ComboBox connectorComboBox = null!;
    private Button loadPinMapButton = null!;
    private Button refreshCcfButton = null!;
    private Button createRcmProfileButton = null!;
    private Button openRcmProfileButton = null!;
    private Button saveRcmProfileButton = null!;
    private Button saveRcmProfileAsButton = null!;
    private Button addConnectorButton = null!;
    private Button renameConnectorButton = null!;
    private Button editConnectorPinsButton = null!;
    private Button deleteConnectorButton = null!;
    private Button addInputButton = null!;
    private Button editInputButton = null!;
    private Button assignNextPinButton = null!;
    private Button deleteInputButton = null!;
    private Label captureWindowLabel = null!;
    private NumericUpDown captureSecondsNumeric = null!;
    private Label captureSecondsLabel = null!;
    private Label pinMapStatusLabel = null!;
    private Label ccfStatusLabel = null!;
    private TableLayoutPanel progressPanel = null!;
    private Label progressLabel = null!;
    private Label profilePathLabel = null!;
    private SplitContainer mainSplit = null!;
    private DataGridView rcmGrid = null!;
    private DataGridViewComboBoxColumn connectorColumn = null!;
    private DataGridViewTextBoxColumn pinColumn = null!;
    private DataGridViewTextBoxColumn functionColumn = null!;
    private DataGridViewTextBoxColumn expectedCcfColumn = null!;
    private DataGridViewTextBoxColumn voltageRemovedColumn = null!;
    private DataGridViewTextBoxColumn voltageAppliedColumn = null!;
    private DataGridViewTextBoxColumn stateDifferenceColumn = null!;
    private DataGridViewTextBoxColumn decoderColumn = null!;
    private DataGridViewTextBoxColumn rcmResultColumn = null!;
    private TableLayoutPanel workflowLayout = null!;
    private Label selectedPinLabel = null!;
    private GroupBox voltageRemovedGroup = null!;
    private FlowLayoutPanel voltageRemovedPanel = null!;
    private Label voltageRemovedInstructionLabel = null!;
    private Button captureVoltageRemovedButton = null!;
    private Label voltageRemovedStatusLabel = null!;
    private GroupBox voltageAppliedGroup = null!;
    private FlowLayoutPanel voltageAppliedPanel = null!;
    private Label voltageAppliedInstructionLabel = null!;
    private Button captureVoltageAppliedButton = null!;
    private Label voltageAppliedStatusLabel = null!;
    private FlowLayoutPanel actionPanel = null!;
    private Button editSelectedWorkflowButton = null!;
    private Button compareStatesButton = null!;
    private Button resetInputButton = null!;
    private GroupBox evidenceGroup = null!;
    private TextBox evidenceTextBox = null!;
    private Label statusLabel = null!;
    private System.Windows.Forms.Timer captureWindowTimer = null!;

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
        loadPinMapButton = new Button();
        refreshCcfButton = new Button();
        createRcmProfileButton = new Button();
        openRcmProfileButton = new Button();
        saveRcmProfileButton = new Button();
        saveRcmProfileAsButton = new Button();
        addConnectorButton = new Button();
        renameConnectorButton = new Button();
        editConnectorPinsButton = new Button();
        deleteConnectorButton = new Button();
        addInputButton = new Button();
        editInputButton = new Button();
        assignNextPinButton = new Button();
        deleteInputButton = new Button();
        captureWindowLabel = new Label();
        captureSecondsNumeric = new NumericUpDown();
        captureSecondsLabel = new Label();
        pinMapStatusLabel = new Label();
        ccfStatusLabel = new Label();
        progressPanel = new TableLayoutPanel();
        progressLabel = new Label();
        profilePathLabel = new Label();
        mainSplit = new SplitContainer();
        rcmGrid = new DataGridView();
        connectorColumn = new DataGridViewComboBoxColumn();
        pinColumn = new DataGridViewTextBoxColumn();
        functionColumn = new DataGridViewTextBoxColumn();
        expectedCcfColumn = new DataGridViewTextBoxColumn();
        voltageRemovedColumn = new DataGridViewTextBoxColumn();
        voltageAppliedColumn = new DataGridViewTextBoxColumn();
        stateDifferenceColumn = new DataGridViewTextBoxColumn();
        decoderColumn = new DataGridViewTextBoxColumn();
        rcmResultColumn = new DataGridViewTextBoxColumn();
        workflowLayout = new TableLayoutPanel();
        selectedPinLabel = new Label();
        voltageRemovedGroup = new GroupBox();
        voltageRemovedPanel = new FlowLayoutPanel();
        voltageRemovedInstructionLabel = new Label();
        captureVoltageRemovedButton = new Button();
        voltageRemovedStatusLabel = new Label();
        voltageAppliedGroup = new GroupBox();
        voltageAppliedPanel = new FlowLayoutPanel();
        voltageAppliedInstructionLabel = new Label();
        captureVoltageAppliedButton = new Button();
        voltageAppliedStatusLabel = new Label();
        actionPanel = new FlowLayoutPanel();
        editSelectedWorkflowButton = new Button();
        compareStatesButton = new Button();
        resetInputButton = new Button();
        evidenceGroup = new GroupBox();
        evidenceTextBox = new TextBox();
        statusLabel = new Label();
        captureWindowTimer = new System.Windows.Forms.Timer(components);
        rootLayout.SuspendLayout();
        commandPanel.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)captureSecondsNumeric).BeginInit();
        progressPanel.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)mainSplit).BeginInit();
        mainSplit.Panel1.SuspendLayout();
        mainSplit.Panel2.SuspendLayout();
        mainSplit.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)rcmGrid).BeginInit();
        workflowLayout.SuspendLayout();
        voltageRemovedGroup.SuspendLayout();
        voltageRemovedPanel.SuspendLayout();
        voltageAppliedGroup.SuspendLayout();
        voltageAppliedPanel.SuspendLayout();
        actionPanel.SuspendLayout();
        evidenceGroup.SuspendLayout();
        SuspendLayout();
        //
        // rootLayout
        //
        rootLayout.ColumnCount = 1;
        rootLayout.ColumnStyles.Clear();
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        rootLayout.Controls.Add(commandPanel, 0, 0);
        rootLayout.Controls.Add(progressPanel, 0, 1);
        rootLayout.Controls.Add(mainSplit, 0, 2);
        rootLayout.Controls.Add(statusLabel, 0, 3);
        rootLayout.Dock = DockStyle.Fill;
        rootLayout.Name = "rootLayout";
        rootLayout.Padding = new Padding(8);
        rootLayout.RowCount = 4;
        rootLayout.RowStyles.Clear();
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 126F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
        //
        // commandPanel
        //
        commandPanel.Controls.Add(profileLabel);
        commandPanel.Controls.Add(connectorLabel);
        commandPanel.Controls.Add(connectorComboBox);
        commandPanel.Controls.Add(loadPinMapButton);
        commandPanel.Controls.Add(refreshCcfButton);
        commandPanel.Controls.Add(createRcmProfileButton);
        commandPanel.Controls.Add(openRcmProfileButton);
        commandPanel.Controls.Add(saveRcmProfileButton);
        commandPanel.Controls.Add(saveRcmProfileAsButton);
        commandPanel.Controls.Add(addConnectorButton);
        commandPanel.Controls.Add(renameConnectorButton);
        commandPanel.Controls.Add(editConnectorPinsButton);
        commandPanel.Controls.Add(deleteConnectorButton);
        commandPanel.Controls.Add(addInputButton);
        commandPanel.Controls.Add(editInputButton);
        commandPanel.Controls.Add(assignNextPinButton);
        commandPanel.Controls.Add(deleteInputButton);
        commandPanel.Controls.Add(captureWindowLabel);
        commandPanel.Controls.Add(captureSecondsNumeric);
        commandPanel.Controls.Add(captureSecondsLabel);
        commandPanel.Controls.Add(pinMapStatusLabel);
        commandPanel.Controls.Add(ccfStatusLabel);
        commandPanel.Dock = DockStyle.Fill;
        commandPanel.Name = "commandPanel";
        commandPanel.Padding = new Padding(4);
        commandPanel.WrapContents = true;
        //
        // top controls
        //
        profileLabel.AutoSize = true;
        profileLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        profileLabel.Margin = new Padding(3, 9, 10, 3);
        profileLabel.Text = "RCM: Class 171";
        connectorLabel.AutoSize = true;
        connectorLabel.Margin = new Padding(3, 9, 3, 3);
        connectorLabel.Text = "Connector filter:";
        connectorComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        connectorComboBox.Margin = new Padding(3, 4, 10, 3);
        connectorComboBox.Name = "connectorComboBox";
        connectorComboBox.Size = new Size(72, 23);
        connectorComboBox.SelectedIndexChanged += ConnectorComboBox_SelectedIndexChanged;
        loadPinMapButton.AutoSize = true;
        loadPinMapButton.Name = "loadPinMapButton";
        loadPinMapButton.Text = "Import Pin Map...";
        loadPinMapButton.Click += LoadPinMapButton_Click;
        refreshCcfButton.AutoSize = true;
        refreshCcfButton.Name = "refreshCcfButton";
        refreshCcfButton.Text = "Refresh CCF";
        refreshCcfButton.Click += RefreshCcfButton_Click;
        createRcmProfileButton.AutoSize = true;
        createRcmProfileButton.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        createRcmProfileButton.Name = "createRcmProfileButton";
        createRcmProfileButton.Text = "New RCM Profile From CCF";
        createRcmProfileButton.Click += CreateRcmProfileButton_Click;
        openRcmProfileButton.AutoSize = true;
        openRcmProfileButton.Name = "openRcmProfileButton";
        openRcmProfileButton.Text = "Open RCM Profile";
        openRcmProfileButton.Click += OpenRcmProfileButton_Click;
        saveRcmProfileButton.AutoSize = true;
        saveRcmProfileButton.Name = "saveRcmProfileButton";
        saveRcmProfileButton.Text = "Save RCM Profile";
        saveRcmProfileButton.Click += SaveRcmProfileButton_Click;
        saveRcmProfileAsButton.AutoSize = true;
        saveRcmProfileAsButton.Name = "saveRcmProfileAsButton";
        saveRcmProfileAsButton.Text = "Save RCM Profile As";
        saveRcmProfileAsButton.Click += SaveRcmProfileAsButton_Click;
        addConnectorButton.AutoSize = true;
        addConnectorButton.Name = "addConnectorButton";
        addConnectorButton.Text = "Add Connector";
        addConnectorButton.Click += AddConnectorButton_Click;
        renameConnectorButton.AutoSize = true;
        renameConnectorButton.Name = "renameConnectorButton";
        renameConnectorButton.Text = "Rename Connector";
        renameConnectorButton.Click += RenameConnectorButton_Click;
        editConnectorPinsButton.AutoSize = true;
        editConnectorPinsButton.Name = "editConnectorPinsButton";
        editConnectorPinsButton.Text = "Edit Pin Sequence";
        editConnectorPinsButton.Click += EditConnectorPinsButton_Click;
        deleteConnectorButton.AutoSize = true;
        deleteConnectorButton.Name = "deleteConnectorButton";
        deleteConnectorButton.Text = "Delete Connector";
        deleteConnectorButton.Click += DeleteConnectorButton_Click;
        addInputButton.AutoSize = true;
        addInputButton.Name = "addInputButton";
        addInputButton.Text = "Add Input / Pin";
        addInputButton.Click += AddInputButton_Click;
        editInputButton.AutoSize = true;
        editInputButton.Name = "editInputButton";
        editInputButton.Text = "Edit Selected Input";
        editInputButton.Click += EditInputButton_Click;
        assignNextPinButton.AutoSize = true;
        assignNextPinButton.Name = "assignNextPinButton";
        assignNextPinButton.Text = "Assign Next Pin";
        assignNextPinButton.Click += AssignNextPinButton_Click;
        deleteInputButton.AutoSize = true;
        deleteInputButton.Name = "deleteInputButton";
        deleteInputButton.Text = "Delete Input / Pin";
        deleteInputButton.Click += DeleteInputButton_Click;
        captureWindowLabel.AutoSize = true;
        captureWindowLabel.Margin = new Padding(12, 9, 3, 3);
        captureWindowLabel.Text = "Capture window:";
        captureSecondsNumeric.DecimalPlaces = 1;
        captureSecondsNumeric.Increment = 0.5M;
        captureSecondsNumeric.Maximum = 10M;
        captureSecondsNumeric.Minimum = 0.5M;
        captureSecondsNumeric.Name = "captureSecondsNumeric";
        captureSecondsNumeric.Size = new Size(58, 23);
        captureSecondsNumeric.Value = 2M;
        captureSecondsLabel.AutoSize = true;
        captureSecondsLabel.Margin = new Padding(0, 9, 12, 3);
        captureSecondsLabel.Text = "seconds";
        pinMapStatusLabel.AutoSize = true;
        pinMapStatusLabel.Margin = new Padding(3, 9, 18, 3);
        pinMapStatusLabel.Name = "pinMapStatusLabel";
        pinMapStatusLabel.Text = "Physical pin map: not loaded";
        ccfStatusLabel.AutoSize = true;
        ccfStatusLabel.Margin = new Padding(3, 9, 3, 3);
        ccfStatusLabel.Name = "ccfStatusLabel";
        ccfStatusLabel.Text = "CCF: none loaded";
        //
        // progressPanel
        //
        progressPanel.ColumnCount = 1;
        progressPanel.ColumnStyles.Clear();
        progressPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        progressPanel.Controls.Add(progressLabel, 0, 0);
        progressPanel.Controls.Add(profilePathLabel, 0, 1);
        progressPanel.Dock = DockStyle.Fill;
        progressPanel.Name = "progressPanel";
        progressPanel.RowCount = 2;
        progressPanel.RowStyles.Clear();
        progressPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        progressPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        progressLabel.AutoSize = true;
        progressLabel.Dock = DockStyle.Fill;
        progressLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        progressLabel.Name = "progressLabel";
        progressLabel.Text = "RCM Progress: no profile created/opened";
        profilePathLabel.AutoSize = true;
        profilePathLabel.Dock = DockStyle.Fill;
        profilePathLabel.Name = "profilePathLabel";
        profilePathLabel.Text = "RCM JSON: none";
        //
        // mainSplit
        //
        mainSplit.Dock = DockStyle.Fill;
        mainSplit.FixedPanel = FixedPanel.Panel2;
        mainSplit.IsSplitterFixed = false;
        mainSplit.Name = "mainSplit";
        mainSplit.Size = new Size(1434, 520);
        mainSplit.Panel1.Controls.Add(rcmGrid);
        mainSplit.Panel1MinSize = 420;
        mainSplit.Panel2.Controls.Add(workflowLayout);
        mainSplit.Panel2MinSize = 480;
        mainSplit.SplitterDistance = 940;
        mainSplit.SplitterWidth = 6;
        mainSplit.SplitterMoved += MainSplit_SplitterMoved;
        //
        // rcmGrid
        //
        rcmGrid.AllowUserToAddRows = false;
        rcmGrid.AllowUserToDeleteRows = false;
        rcmGrid.AllowUserToOrderColumns = true;
        rcmGrid.AutoGenerateColumns = false;
        rcmGrid.BackgroundColor = SystemColors.Window;
        rcmGrid.Columns.AddRange(new DataGridViewColumn[] { connectorColumn, pinColumn, functionColumn, expectedCcfColumn, voltageRemovedColumn, voltageAppliedColumn, stateDifferenceColumn, decoderColumn, rcmResultColumn });
        rcmGrid.Dock = DockStyle.Fill;
        rcmGrid.MultiSelect = false;
        rcmGrid.Name = "rcmGrid";
        rcmGrid.ReadOnly = false;
        rcmGrid.RowHeadersVisible = false;
        rcmGrid.ScrollBars = ScrollBars.Both;
        rcmGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        rcmGrid.CurrentCellChanged += RcmGrid_SelectionChanged;
        rcmGrid.CellEndEdit += RcmGrid_CellEndEdit;
        rcmGrid.CellValidating += RcmGrid_CellValidating;
        rcmGrid.EditingControlShowing += RcmGrid_EditingControlShowing;
        connectorColumn.DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox;
        connectorColumn.FlatStyle = FlatStyle.Flat;
        connectorColumn.HeaderText = "Connector"; connectorColumn.Name = "connectorColumn"; connectorColumn.ReadOnly = false; connectorColumn.Width = 85;
        pinColumn.HeaderText = "Pin"; pinColumn.Name = "pinColumn"; pinColumn.ReadOnly = false; pinColumn.Width = 55;
        functionColumn.HeaderText = "Function"; functionColumn.Name = "functionColumn"; functionColumn.ReadOnly = true; functionColumn.Width = 140;
        expectedCcfColumn.HeaderText = "Expected CCF"; expectedCcfColumn.Name = "expectedCcfColumn"; expectedCcfColumn.ReadOnly = true; expectedCcfColumn.Width = 200;
        voltageRemovedColumn.HeaderText = "Voltage Removed"; voltageRemovedColumn.Name = "voltageRemovedColumn"; voltageRemovedColumn.ReadOnly = true; voltageRemovedColumn.Width = 120;
        voltageAppliedColumn.HeaderText = "+24V Applied"; voltageAppliedColumn.Name = "voltageAppliedColumn"; voltageAppliedColumn.ReadOnly = true; voltageAppliedColumn.Width = 120;
        stateDifferenceColumn.HeaderText = "State Difference"; stateDifferenceColumn.Name = "stateDifferenceColumn"; stateDifferenceColumn.ReadOnly = true; stateDifferenceColumn.Width = 150;
        decoderColumn.HeaderText = "Decoder"; decoderColumn.Name = "decoderColumn"; decoderColumn.ReadOnly = true; decoderColumn.Width = 90;
        rcmResultColumn.HeaderText = "RCM Result"; rcmResultColumn.Name = "rcmResultColumn"; rcmResultColumn.ReadOnly = true; rcmResultColumn.Width = 150;
        //
        // workflowLayout
        //
        workflowLayout.ColumnCount = 1;
        workflowLayout.ColumnStyles.Clear();
        workflowLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        workflowLayout.Controls.Add(selectedPinLabel, 0, 0);
        workflowLayout.Controls.Add(voltageRemovedGroup, 0, 1);
        workflowLayout.Controls.Add(voltageAppliedGroup, 0, 2);
        workflowLayout.Controls.Add(actionPanel, 0, 3);
        workflowLayout.Controls.Add(evidenceGroup, 0, 4);
        workflowLayout.AutoScroll = true;
        workflowLayout.Dock = DockStyle.Fill;
        workflowLayout.Name = "workflowLayout";
        workflowLayout.Padding = new Padding(8, 0, 0, 0);
        workflowLayout.RowCount = 5;
        workflowLayout.RowStyles.Clear();
        workflowLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        workflowLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        workflowLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        workflowLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        workflowLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        selectedPinLabel.AutoSize = true;
        selectedPinLabel.Dock = DockStyle.Fill;
        selectedPinLabel.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        selectedPinLabel.Name = "selectedPinLabel";
        selectedPinLabel.Padding = new Padding(6);
        selectedPinLabel.Text = "Select a physical pin";
        //
        // voltage removed workflow
        //
        voltageRemovedGroup.Controls.Add(voltageRemovedPanel);
        voltageRemovedGroup.AutoSize = true;
        voltageRemovedGroup.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        voltageRemovedGroup.Dock = DockStyle.Fill;
        voltageRemovedGroup.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        voltageRemovedGroup.Name = "voltageRemovedGroup";
        voltageRemovedGroup.MinimumSize = new Size(0, 108);
        voltageRemovedGroup.Text = "STEP 1 — TEST VOLTAGE REMOVED";
        voltageRemovedPanel.Controls.Add(voltageRemovedInstructionLabel);
        voltageRemovedPanel.Controls.Add(captureVoltageRemovedButton);
        voltageRemovedPanel.Controls.Add(voltageRemovedStatusLabel);
        voltageRemovedPanel.Dock = DockStyle.Fill;
        voltageRemovedPanel.AutoSize = true;
        voltageRemovedPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        voltageRemovedPanel.FlowDirection = FlowDirection.TopDown;
        voltageRemovedPanel.Name = "voltageRemovedPanel";
        voltageRemovedPanel.Padding = new Padding(6);
        voltageRemovedPanel.WrapContents = false;
        voltageRemovedInstructionLabel.AutoSize = true;
        voltageRemovedInstructionLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        voltageRemovedInstructionLabel.Name = "voltageRemovedInstructionLabel";
        voltageRemovedInstructionLabel.Text = "REMOVE TEST VOLTAGE FROM SELECTED PIN";
        captureVoltageRemovedButton.AutoSize = true;
        captureVoltageRemovedButton.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        captureVoltageRemovedButton.Name = "captureVoltageRemovedButton";
        captureVoltageRemovedButton.Text = "Capture Voltage Removed";
        captureVoltageRemovedButton.Click += CaptureVoltageRemovedButton_Click;
        voltageRemovedStatusLabel.AutoSize = true;
        voltageRemovedStatusLabel.Name = "voltageRemovedStatusLabel";
        voltageRemovedStatusLabel.Text = "NOT CAPTURED";
        //
        // voltage applied workflow
        //
        voltageAppliedGroup.Controls.Add(voltageAppliedPanel);
        voltageAppliedGroup.AutoSize = true;
        voltageAppliedGroup.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        voltageAppliedGroup.Dock = DockStyle.Fill;
        voltageAppliedGroup.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        voltageAppliedGroup.Name = "voltageAppliedGroup";
        voltageAppliedGroup.MinimumSize = new Size(0, 108);
        voltageAppliedGroup.Text = "STEP 2 — +24 V APPLIED";
        voltageAppliedPanel.Controls.Add(voltageAppliedInstructionLabel);
        voltageAppliedPanel.Controls.Add(captureVoltageAppliedButton);
        voltageAppliedPanel.Controls.Add(voltageAppliedStatusLabel);
        voltageAppliedPanel.Dock = DockStyle.Fill;
        voltageAppliedPanel.AutoSize = true;
        voltageAppliedPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        voltageAppliedPanel.FlowDirection = FlowDirection.TopDown;
        voltageAppliedPanel.Name = "voltageAppliedPanel";
        voltageAppliedPanel.Padding = new Padding(6);
        voltageAppliedPanel.WrapContents = false;
        voltageAppliedInstructionLabel.AutoSize = true;
        voltageAppliedInstructionLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        voltageAppliedInstructionLabel.Name = "voltageAppliedInstructionLabel";
        voltageAppliedInstructionLabel.Text = "APPLY +24 V TO SELECTED PIN";
        captureVoltageAppliedButton.AutoSize = true;
        captureVoltageAppliedButton.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        captureVoltageAppliedButton.Name = "captureVoltageAppliedButton";
        captureVoltageAppliedButton.Text = "Capture +24V Applied";
        captureVoltageAppliedButton.Click += CaptureVoltageAppliedButton_Click;
        voltageAppliedStatusLabel.AutoSize = true;
        voltageAppliedStatusLabel.Name = "voltageAppliedStatusLabel";
        voltageAppliedStatusLabel.Text = "NOT CAPTURED";
        //
        // actionPanel
        //
        actionPanel.Controls.Add(editSelectedWorkflowButton);
        actionPanel.Controls.Add(compareStatesButton);
        actionPanel.Controls.Add(resetInputButton);
        actionPanel.AutoSize = true;
        actionPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        actionPanel.Dock = DockStyle.Fill;
        actionPanel.Name = "actionPanel";
        editSelectedWorkflowButton.AutoSize = true;
        editSelectedWorkflowButton.Name = "editSelectedWorkflowButton";
        editSelectedWorkflowButton.Text = "Edit Selected Input";
        editSelectedWorkflowButton.Click += EditInputButton_Click;
        compareStatesButton.AutoSize = true;
        compareStatesButton.Name = "compareStatesButton";
        compareStatesButton.Text = "Compare States";
        compareStatesButton.Click += CompareStatesButton_Click;
        resetInputButton.AutoSize = true;
        resetInputButton.Name = "resetInputButton";
        resetInputButton.Text = "Reset Test Evidence";
        resetInputButton.Click += ResetInputButton_Click;
        //
        // evidenceGroup
        //
        evidenceGroup.Controls.Add(evidenceTextBox);
        evidenceGroup.Dock = DockStyle.Fill;
        evidenceGroup.Name = "evidenceGroup";
        evidenceGroup.Text = "State comparison / CANDIDATE RAW EVIDENCE";
        evidenceTextBox.BackColor = SystemColors.Window;
        evidenceTextBox.BorderStyle = BorderStyle.None;
        evidenceTextBox.Dock = DockStyle.Fill;
        evidenceTextBox.Multiline = true;
        evidenceTextBox.Name = "evidenceTextBox";
        evidenceTextBox.ReadOnly = true;
        evidenceTextBox.ScrollBars = ScrollBars.Vertical;
        evidenceTextBox.Text = "Create or open an RCM profile, then select a physical input.";
        evidenceTextBox.WordWrap = true;
        //
        // status and timer
        //
        statusLabel.Dock = DockStyle.Fill;
        statusLabel.Name = "statusLabel";
        statusLabel.Padding = new Padding(6, 0, 0, 0);
        statusLabel.Text = "RCM evidence capture only. No protocol semantics, PASS, or FAIL are inferred.";
        statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        captureWindowTimer.Tick += CaptureWindowTimer_Tick;
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
        ((System.ComponentModel.ISupportInitialize)captureSecondsNumeric).EndInit();
        progressPanel.ResumeLayout(false);
        progressPanel.PerformLayout();
        mainSplit.Panel1.ResumeLayout(false);
        mainSplit.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)mainSplit).EndInit();
        mainSplit.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)rcmGrid).EndInit();
        workflowLayout.ResumeLayout(false);
        voltageRemovedGroup.ResumeLayout(false);
        voltageRemovedPanel.ResumeLayout(false);
        voltageRemovedPanel.PerformLayout();
        voltageAppliedGroup.ResumeLayout(false);
        voltageAppliedPanel.ResumeLayout(false);
        voltageAppliedPanel.PerformLayout();
        actionPanel.ResumeLayout(false);
        actionPanel.PerformLayout();
        evidenceGroup.ResumeLayout(false);
        evidenceGroup.PerformLayout();
        ResumeLayout(false);
    }
}
