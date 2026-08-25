#nullable enable

namespace CcfEditor.WinForms;

partial class OtmrLiveControl
{
    private System.ComponentModel.IContainer? components = null;
    private TableLayoutPanel rootLayout = null!;
    private GroupBox connectionGroupBox = null!;
    private TableLayoutPanel connectionLayout = null!;
    private Label portLabel = null!;
    private ComboBox portComboBox = null!;
    private Button refreshPortsButton = null!;
    private Label baudLabel = null!;
    private TextBox baudTextBox = null!;
    private Label dataBitsLabel = null!;
    private TextBox dataBitsTextBox = null!;
    private Label parityLabel = null!;
    private TextBox parityTextBox = null!;
    private Label stopBitsLabel = null!;
    private TextBox stopBitsTextBox = null!;
    private Button connectButton = null!;
    private Button startLiveButton = null!;
    private Button stopLiveButton = null!;
    private Button stopRestoreButton = null!;
    private Button disconnectButton = null!;
    private Label liveStateLabel = null!;
    private Label safetyLabel = null!;
    private GroupBox databaseRecordingGroupBox = null!;
    private TableLayoutPanel databaseRecordingLayout = null!;
    private FlowLayoutPanel databaseButtonsPanel = null!;
    private Button startDatabaseRecordingButton = null!;
    private Button stopDatabaseRecordingButton = null!;
    private Button openDatabaseFolderButton = null!;
    private Label databaseRecordingStateLabel = null!;
    private Label databaseSessionLabel = null!;
    private Label databaseCountsLabel = null!;
    private Label databasePathLabel = null!;
    private Label databaseMessageLabel = null!;
    private GroupBox captureGroupBox = null!;
    private DataGridView captureGrid = null!;
    private DataGridViewTextBoxColumn timeColumn = null!;
    private DataGridViewTextBoxColumn directionColumn = null!;
    private DataGridViewTextBoxColumn bytesColumn = null!;
    private DataGridViewTextBoxColumn interpretationColumn = null!;
    private FlowLayoutPanel captureButtonsPanel = null!;
    private Button clearCaptureButton = null!;
    private Button saveCaptureButton = null!;
    private Button copyHexButton = null!;
    private Label filterLabel = null!;
    private ComboBox captureFilterComboBox = null!;
    private Label captureCountLabel = null!;
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
        connectionGroupBox = new GroupBox();
        connectionLayout = new TableLayoutPanel();
        portLabel = new Label();
        portComboBox = new ComboBox();
        refreshPortsButton = new Button();
        baudLabel = new Label();
        baudTextBox = new TextBox();
        dataBitsLabel = new Label();
        dataBitsTextBox = new TextBox();
        parityLabel = new Label();
        parityTextBox = new TextBox();
        stopBitsLabel = new Label();
        stopBitsTextBox = new TextBox();
        connectButton = new Button();
        startLiveButton = new Button();
        stopLiveButton = new Button();
        stopRestoreButton = new Button();
        disconnectButton = new Button();
        liveStateLabel = new Label();
        safetyLabel = new Label();
        databaseRecordingGroupBox = new GroupBox();
        databaseRecordingLayout = new TableLayoutPanel();
        databaseButtonsPanel = new FlowLayoutPanel();
        startDatabaseRecordingButton = new Button();
        stopDatabaseRecordingButton = new Button();
        openDatabaseFolderButton = new Button();
        databaseRecordingStateLabel = new Label();
        databaseSessionLabel = new Label();
        databaseCountsLabel = new Label();
        databasePathLabel = new Label();
        databaseMessageLabel = new Label();
        captureGroupBox = new GroupBox();
        captureGrid = new DataGridView();
        timeColumn = new DataGridViewTextBoxColumn();
        directionColumn = new DataGridViewTextBoxColumn();
        bytesColumn = new DataGridViewTextBoxColumn();
        interpretationColumn = new DataGridViewTextBoxColumn();
        captureButtonsPanel = new FlowLayoutPanel();
        clearCaptureButton = new Button();
        saveCaptureButton = new Button();
        copyHexButton = new Button();
        filterLabel = new Label();
        captureFilterComboBox = new ComboBox();
        captureCountLabel = new Label();
        statusLabel = new Label();
        rootLayout.SuspendLayout();
        connectionGroupBox.SuspendLayout();
        connectionLayout.SuspendLayout();
        databaseRecordingGroupBox.SuspendLayout();
        databaseRecordingLayout.SuspendLayout();
        databaseButtonsPanel.SuspendLayout();
        captureGroupBox.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)captureGrid).BeginInit();
        captureButtonsPanel.SuspendLayout();
        SuspendLayout();
        // 
        // rootLayout
        // 
        rootLayout.ColumnCount = 1;
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        rootLayout.Controls.Add(connectionGroupBox, 0, 0);
        rootLayout.Controls.Add(databaseRecordingGroupBox, 0, 1);
        rootLayout.Controls.Add(captureGroupBox, 0, 2);
        rootLayout.Controls.Add(statusLabel, 0, 3);
        rootLayout.Dock = DockStyle.Fill;
        rootLayout.Location = new Point(0, 0);
        rootLayout.Name = "rootLayout";
        rootLayout.Padding = new Padding(8);
        rootLayout.RowCount = 4;
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 170F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 130F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        rootLayout.Size = new Size(1200, 700);
        // 
        // connectionGroupBox
        // 
        connectionGroupBox.Controls.Add(connectionLayout);
        connectionGroupBox.Dock = DockStyle.Fill;
        connectionGroupBox.Name = "connectionGroupBox";
        connectionGroupBox.Text = "Class 171 OTMR live-start control";
        // 
        // connectionLayout
        // 
        connectionLayout.ColumnCount = 10;
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80F));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80F));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80F));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80F));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80F));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125F));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155F));
        connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        connectionLayout.Controls.Add(portLabel, 0, 0);
        connectionLayout.Controls.Add(portComboBox, 1, 0);
        connectionLayout.Controls.Add(refreshPortsButton, 2, 0);
        connectionLayout.Controls.Add(baudLabel, 3, 0);
        connectionLayout.Controls.Add(baudTextBox, 4, 0);
        connectionLayout.Controls.Add(dataBitsLabel, 5, 0);
        connectionLayout.Controls.Add(dataBitsTextBox, 6, 0);
        connectionLayout.Controls.Add(parityLabel, 3, 1);
        connectionLayout.Controls.Add(parityTextBox, 4, 1);
        connectionLayout.Controls.Add(stopBitsLabel, 5, 1);
        connectionLayout.Controls.Add(stopBitsTextBox, 6, 1);
        connectionLayout.Controls.Add(connectButton, 7, 0);
        connectionLayout.Controls.Add(startLiveButton, 8, 0);
        connectionLayout.Controls.Add(stopLiveButton, 7, 1);
        connectionLayout.Controls.Add(stopRestoreButton, 8, 1);
        connectionLayout.Controls.Add(liveStateLabel, 0, 1);
        connectionLayout.Controls.Add(disconnectButton, 9, 1);
        connectionLayout.Controls.Add(safetyLabel, 0, 2);
        connectionLayout.Dock = DockStyle.Fill;
        connectionLayout.Name = "connectionLayout";
        connectionLayout.Padding = new Padding(10);
        connectionLayout.RowCount = 3;
        connectionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        connectionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        connectionLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        // 
        // serial controls
        // 
        portLabel.Dock = DockStyle.Fill;
        portLabel.Text = "Serial Port:";
        portLabel.TextAlign = ContentAlignment.MiddleLeft;
        portComboBox.Dock = DockStyle.Fill;
        portComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        portComboBox.Name = "portComboBox";
        refreshPortsButton.Dock = DockStyle.Fill;
        refreshPortsButton.Name = "refreshPortsButton";
        refreshPortsButton.Text = "Refresh Ports";
        refreshPortsButton.Click += RefreshPortsButton_Click;
        baudLabel.Dock = DockStyle.Fill;
        baudLabel.Text = "Baud:";
        baudLabel.TextAlign = ContentAlignment.MiddleRight;
        baudTextBox.Dock = DockStyle.Fill;
        baudTextBox.ReadOnly = true;
        baudTextBox.Text = "38400";
        dataBitsLabel.Dock = DockStyle.Fill;
        dataBitsLabel.Text = "Data bits:";
        dataBitsLabel.TextAlign = ContentAlignment.MiddleRight;
        dataBitsTextBox.Dock = DockStyle.Fill;
        dataBitsTextBox.ReadOnly = true;
        dataBitsTextBox.Text = "8";
        parityLabel.Dock = DockStyle.Fill;
        parityLabel.Text = "Parity:";
        parityLabel.TextAlign = ContentAlignment.MiddleRight;
        parityTextBox.Dock = DockStyle.Fill;
        parityTextBox.ReadOnly = true;
        parityTextBox.Text = "None";
        stopBitsLabel.Dock = DockStyle.Fill;
        stopBitsLabel.Text = "Stop bits:";
        stopBitsLabel.TextAlign = ContentAlignment.MiddleRight;
        stopBitsTextBox.Dock = DockStyle.Fill;
        stopBitsTextBox.ReadOnly = true;
        stopBitsTextBox.Text = "1";
        connectButton.Dock = DockStyle.Fill;
        connectButton.Name = "connectButton";
        connectButton.Text = "Connect";
        connectButton.Click += ConnectButton_Click;
        startLiveButton.Dock = DockStyle.Fill;
        startLiveButton.Enabled = false;
        startLiveButton.Name = "startLiveButton";
        startLiveButton.Text = "Start OTMR Live";
        startLiveButton.Click += StartLiveButton_Click;
        stopLiveButton.Dock = DockStyle.Fill;
        stopLiveButton.Enabled = false;
        stopLiveButton.Name = "stopLiveButton";
        stopLiveButton.Text = "Stop Live";
        stopLiveButton.Click += StopLiveButton_Click;
        stopRestoreButton.Dock = DockStyle.Fill;
        stopRestoreButton.Enabled = false;
        stopRestoreButton.Name = "stopRestoreButton";
        stopRestoreButton.Text = "Stop + Restore";
        stopRestoreButton.Click += StopRestoreButton_Click;
        liveStateLabel.AutoSize = true;
        connectionLayout.SetColumnSpan(liveStateLabel, 3);
        liveStateLabel.Dock = DockStyle.Fill;
        liveStateLabel.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
        liveStateLabel.Name = "liveStateLabel";
        liveStateLabel.Text = "DISCONNECTED";
        liveStateLabel.TextAlign = ContentAlignment.MiddleLeft;
        disconnectButton.Dock = DockStyle.Fill;
        disconnectButton.Enabled = false;
        disconnectButton.Name = "disconnectButton";
        disconnectButton.Text = "Disconnect";
        disconnectButton.Click += DisconnectButton_Click;
        safetyLabel.AutoSize = true;
        connectionLayout.SetColumnSpan(safetyLabel, 10);
        safetyLabel.Dock = DockStyle.Fill;
        safetyLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        safetyLabel.Text = "BENCH VALIDATION: START requires the selected Class 171 CCF and complete proven preflight. Stop Live only closes COM; Stop + Restore separately writes the frozen pre-START configuration. LiveActive still requires a complete FB FB ... FF frame.";
        safetyLabel.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // databaseRecordingGroupBox
        // 
        databaseRecordingGroupBox.Controls.Add(databaseRecordingLayout);
        databaseRecordingGroupBox.Dock = DockStyle.Fill;
        databaseRecordingGroupBox.Name = "databaseRecordingGroupBox";
        databaseRecordingGroupBox.Padding = new Padding(8);
        databaseRecordingGroupBox.Text = "Local SQLite data recording — capture/history storage only; RCM JSON remains configuration";
        // 
        // databaseRecordingLayout
        // 
        databaseRecordingLayout.ColumnCount = 2;
        databaseRecordingLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));
        databaseRecordingLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
        databaseRecordingLayout.Controls.Add(databaseButtonsPanel, 0, 0);
        databaseRecordingLayout.Controls.Add(databaseRecordingStateLabel, 1, 0);
        databaseRecordingLayout.Controls.Add(databaseSessionLabel, 0, 1);
        databaseRecordingLayout.Controls.Add(databaseCountsLabel, 1, 1);
        databaseRecordingLayout.Controls.Add(databasePathLabel, 0, 2);
        databaseRecordingLayout.Controls.Add(databaseMessageLabel, 1, 2);
        databaseRecordingLayout.Dock = DockStyle.Fill;
        databaseRecordingLayout.Name = "databaseRecordingLayout";
        databaseRecordingLayout.RowCount = 3;
        databaseRecordingLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        databaseRecordingLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        databaseRecordingLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        // 
        // databaseButtonsPanel
        // 
        databaseButtonsPanel.Controls.Add(startDatabaseRecordingButton);
        databaseButtonsPanel.Controls.Add(stopDatabaseRecordingButton);
        databaseButtonsPanel.Controls.Add(openDatabaseFolderButton);
        databaseButtonsPanel.Dock = DockStyle.Fill;
        databaseButtonsPanel.Name = "databaseButtonsPanel";
        databaseButtonsPanel.WrapContents = false;
        startDatabaseRecordingButton.AutoSize = true;
        startDatabaseRecordingButton.Name = "startDatabaseRecordingButton";
        startDatabaseRecordingButton.Text = "Start Recording";
        startDatabaseRecordingButton.Click += StartDatabaseRecordingButton_Click;
        stopDatabaseRecordingButton.AutoSize = true;
        stopDatabaseRecordingButton.Enabled = false;
        stopDatabaseRecordingButton.Name = "stopDatabaseRecordingButton";
        stopDatabaseRecordingButton.Text = "Stop Recording";
        stopDatabaseRecordingButton.Click += StopDatabaseRecordingButton_Click;
        openDatabaseFolderButton.AutoSize = true;
        openDatabaseFolderButton.Name = "openDatabaseFolderButton";
        openDatabaseFolderButton.Text = "Open Database Folder";
        openDatabaseFolderButton.Click += OpenDatabaseFolderButton_Click;
        databaseRecordingStateLabel.Dock = DockStyle.Fill;
        databaseRecordingStateLabel.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        databaseRecordingStateLabel.Name = "databaseRecordingStateLabel";
        databaseRecordingStateLabel.Text = "STOPPED";
        databaseRecordingStateLabel.TextAlign = ContentAlignment.MiddleRight;
        databaseSessionLabel.AutoEllipsis = true;
        databaseSessionLabel.Dock = DockStyle.Fill;
        databaseSessionLabel.Name = "databaseSessionLabel";
        databaseSessionLabel.Text = "Session: -";
        databaseSessionLabel.TextAlign = ContentAlignment.MiddleLeft;
        databaseCountsLabel.AutoEllipsis = true;
        databaseCountsLabel.Dock = DockStyle.Fill;
        databaseCountsLabel.Name = "databaseCountsLabel";
        databaseCountsLabel.Text = "Raw entries: 0 | Complete frames: 0";
        databaseCountsLabel.TextAlign = ContentAlignment.MiddleRight;
        databasePathLabel.AutoEllipsis = true;
        databasePathLabel.Dock = DockStyle.Fill;
        databasePathLabel.Name = "databasePathLabel";
        databasePathLabel.Text = "Database path";
        databasePathLabel.TextAlign = ContentAlignment.MiddleLeft;
        databaseMessageLabel.AutoEllipsis = true;
        databaseMessageLabel.Dock = DockStyle.Fill;
        databaseMessageLabel.Name = "databaseMessageLabel";
        databaseMessageLabel.Text = "Stopped. Start Recording before the OTMR sequence if you want every TX/RX byte retained.";
        databaseMessageLabel.TextAlign = ContentAlignment.MiddleRight;
        // 
        // captureGroupBox
        // 
        captureGroupBox.Controls.Add(captureGrid);
        captureGroupBox.Controls.Add(captureButtonsPanel);
        captureGroupBox.Dock = DockStyle.Fill;
        captureGroupBox.Name = "captureGroupBox";
        captureGroupBox.Padding = new Padding(8);
        captureGroupBox.Text = "Raw Communication Log - exact received/transmitted bytes retained";
        // 
        // captureGrid
        // 
        captureGrid.AllowUserToAddRows = false;
        captureGrid.AllowUserToDeleteRows = false;
        captureGrid.AutoGenerateColumns = false;
        captureGrid.BackgroundColor = SystemColors.Window;
        captureGrid.Columns.AddRange(new DataGridViewColumn[] { timeColumn, directionColumn, bytesColumn, interpretationColumn });
        captureGrid.Dock = DockStyle.Fill;
        captureGrid.MultiSelect = false;
        captureGrid.Name = "captureGrid";
        captureGrid.ReadOnly = true;
        captureGrid.RowHeadersVisible = false;
        captureGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        timeColumn.HeaderText = "Time";
        timeColumn.Name = "timeColumn";
        timeColumn.ReadOnly = true;
        timeColumn.Width = 105;
        directionColumn.HeaderText = "TX/RX";
        directionColumn.Name = "directionColumn";
        directionColumn.ReadOnly = true;
        directionColumn.Width = 60;
        bytesColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        bytesColumn.HeaderText = "Raw bytes";
        bytesColumn.Name = "bytesColumn";
        bytesColumn.ReadOnly = true;
        interpretationColumn.HeaderText = "Interpretation (if verified)";
        interpretationColumn.Name = "interpretationColumn";
        interpretationColumn.ReadOnly = true;
        interpretationColumn.Width = 300;
        // 
        // captureButtonsPanel
        // 
        captureButtonsPanel.Controls.Add(clearCaptureButton);
        captureButtonsPanel.Controls.Add(saveCaptureButton);
        captureButtonsPanel.Controls.Add(copyHexButton);
        captureButtonsPanel.Controls.Add(filterLabel);
        captureButtonsPanel.Controls.Add(captureFilterComboBox);
        captureButtonsPanel.Controls.Add(captureCountLabel);
        captureButtonsPanel.Dock = DockStyle.Bottom;
        captureButtonsPanel.FlowDirection = FlowDirection.LeftToRight;
        captureButtonsPanel.Height = 38;
        captureButtonsPanel.Name = "captureButtonsPanel";
        captureButtonsPanel.Padding = new Padding(0, 6, 0, 0);
        clearCaptureButton.AutoSize = true;
        clearCaptureButton.Name = "clearCaptureButton";
        clearCaptureButton.Text = "Clear";
        clearCaptureButton.Click += ClearCaptureButton_Click;
        saveCaptureButton.AutoSize = true;
        saveCaptureButton.Name = "saveCaptureButton";
        saveCaptureButton.Text = "Save Capture";
        saveCaptureButton.Click += SaveCaptureButton_Click;
        copyHexButton.AutoSize = true;
        copyHexButton.Name = "copyHexButton";
        copyHexButton.Text = "Copy Hex";
        copyHexButton.Click += CopyHexButton_Click;
        filterLabel.AutoSize = true;
        filterLabel.Margin = new Padding(18, 7, 3, 0);
        filterLabel.Name = "filterLabel";
        filterLabel.Text = "Show:";
        captureFilterComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        captureFilterComboBox.FormattingEnabled = true;
        captureFilterComboBox.Items.AddRange(new object[] { "All", "RX", "TX" });
        captureFilterComboBox.Name = "captureFilterComboBox";
        captureFilterComboBox.Size = new Size(75, 23);
        captureFilterComboBox.SelectedIndex = 0;
        captureFilterComboBox.SelectedIndexChanged += CaptureFilterComboBox_SelectedIndexChanged;
        captureCountLabel.AutoSize = true;
        captureCountLabel.Margin = new Padding(16, 7, 3, 0);
        captureCountLabel.Name = "captureCountLabel";
        captureCountLabel.Text = "Shown: 0 / Total: 0";
        // 
        // statusLabel
        // 
        statusLabel.Dock = DockStyle.Fill;
        statusLabel.Name = "statusLabel";
        statusLabel.Text = "Disconnected.";
        statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // OtmrLiveControl
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        Controls.Add(rootLayout);
        Name = "OtmrLiveControl";
        Size = new Size(1200, 700);
        connectionLayout.ResumeLayout(true);
        connectionLayout.PerformLayout();
        connectionGroupBox.ResumeLayout(true);
        databaseButtonsPanel.ResumeLayout(true);
        databaseButtonsPanel.PerformLayout();
        databaseRecordingLayout.ResumeLayout(true);
        databaseRecordingLayout.PerformLayout();
        databaseRecordingGroupBox.ResumeLayout(true);
        ((System.ComponentModel.ISupportInitialize)captureGrid).EndInit();
        captureButtonsPanel.ResumeLayout(true);
        captureButtonsPanel.PerformLayout();
        captureGroupBox.ResumeLayout(true);
        rootLayout.ResumeLayout(true);
        ResumeLayout(true);
        PerformLayout();
    }
}
