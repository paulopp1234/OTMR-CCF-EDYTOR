#nullable enable

namespace CcfEditor.WinForms;

partial class OtmrRcmLiveControl
{
    private System.ComponentModel.IContainer? components = null;
    private TableLayoutPanel rootLayout = null!;
    private TableLayoutPanel statusLayout = null!;
    private Label rcmProfileStatusLabel = null!;
    private Label sourceCcfStatusLabel = null!;
    private Label verifiedMappingsStatusLabel = null!;
    private Label liveStateStatusLabel = null!;
    private Label decodedSignalsStatusLabel = null!;
    private DataGridView decodedSignalsGrid = null!;
    private DataGridViewTextBoxColumn decodedPhysicalColumn = null!;
    private DataGridViewTextBoxColumn decodedFunctionColumn = null!;
    private DataGridViewTextBoxColumn decodedLogicalColumn = null!;
    private DataGridViewTextBoxColumn decodedStateColumn = null!;
    private DataGridViewTextBoxColumn decodedRawColumn = null!;
    private DataGridViewTextBoxColumn decodedVerificationColumn = null!;

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
        statusLayout = new TableLayoutPanel();
        rcmProfileStatusLabel = new Label();
        sourceCcfStatusLabel = new Label();
        verifiedMappingsStatusLabel = new Label();
        liveStateStatusLabel = new Label();
        decodedSignalsStatusLabel = new Label();
        decodedSignalsGrid = new DataGridView();
        decodedPhysicalColumn = new DataGridViewTextBoxColumn();
        decodedFunctionColumn = new DataGridViewTextBoxColumn();
        decodedLogicalColumn = new DataGridViewTextBoxColumn();
        decodedStateColumn = new DataGridViewTextBoxColumn();
        decodedRawColumn = new DataGridViewTextBoxColumn();
        decodedVerificationColumn = new DataGridViewTextBoxColumn();
        rootLayout.SuspendLayout();
        statusLayout.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)decodedSignalsGrid).BeginInit();
        SuspendLayout();

        rootLayout.ColumnCount = 1;
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        rootLayout.Controls.Add(statusLayout, 0, 0);
        rootLayout.Controls.Add(decodedSignalsStatusLabel, 0, 1);
        rootLayout.Controls.Add(decodedSignalsGrid, 0, 2);
        rootLayout.Dock = DockStyle.Fill;
        rootLayout.Name = "rcmLiveRootLayout";
        rootLayout.Padding = new Padding(12);
        rootLayout.RowCount = 3;
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        statusLayout.BackColor = SystemColors.ControlLight;
        statusLayout.ColumnCount = 4;
        statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28F));
        statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
        statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27F));
        statusLayout.Controls.Add(rcmProfileStatusLabel, 0, 0);
        statusLayout.Controls.Add(sourceCcfStatusLabel, 1, 0);
        statusLayout.Controls.Add(verifiedMappingsStatusLabel, 2, 0);
        statusLayout.Controls.Add(liveStateStatusLabel, 3, 0);
        statusLayout.Dock = DockStyle.Fill;
        statusLayout.Name = "rcmLiveStatusLayout";
        statusLayout.Padding = new Padding(10);
        statusLayout.RowCount = 1;
        statusLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        rcmProfileStatusLabel.AutoEllipsis = true;
        rcmProfileStatusLabel.Dock = DockStyle.Fill;
        rcmProfileStatusLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        rcmProfileStatusLabel.Name = "rcmProfileStatusLabel";
        rcmProfileStatusLabel.Text = "RCM profile: No RCM profile loaded";
        rcmProfileStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
        sourceCcfStatusLabel.AutoEllipsis = true;
        sourceCcfStatusLabel.Dock = DockStyle.Fill;
        sourceCcfStatusLabel.Name = "sourceCcfStatusLabel";
        sourceCcfStatusLabel.Text = "Source CCF: -";
        sourceCcfStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
        verifiedMappingsStatusLabel.AutoEllipsis = true;
        verifiedMappingsStatusLabel.Dock = DockStyle.Fill;
        verifiedMappingsStatusLabel.Name = "verifiedMappingsStatusLabel";
        verifiedMappingsStatusLabel.Text = "Verified mappings decoded: 0";
        verifiedMappingsStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
        liveStateStatusLabel.AutoEllipsis = true;
        liveStateStatusLabel.Dock = DockStyle.Fill;
        liveStateStatusLabel.Name = "liveStateStatusLabel";
        liveStateStatusLabel.Text = "Live state: DISCONNECTED";
        liveStateStatusLabel.TextAlign = ContentAlignment.MiddleLeft;

        decodedSignalsStatusLabel.Dock = DockStyle.Fill;
        decodedSignalsStatusLabel.Name = "decodedSignalsStatusLabel";
        decodedSignalsStatusLabel.Padding = new Padding(4, 0, 0, 0);
        decodedSignalsStatusLabel.Text = "No verified RCM mappings available for decoded live signals.";
        decodedSignalsStatusLabel.TextAlign = ContentAlignment.MiddleLeft;

        decodedSignalsGrid.AllowUserToAddRows = false;
        decodedSignalsGrid.AllowUserToDeleteRows = false;
        decodedSignalsGrid.AutoGenerateColumns = false;
        decodedSignalsGrid.BackgroundColor = SystemColors.Window;
        decodedSignalsGrid.Columns.AddRange(new DataGridViewColumn[]
        {
            decodedPhysicalColumn, decodedFunctionColumn, decodedLogicalColumn,
            decodedStateColumn, decodedRawColumn, decodedVerificationColumn
        });
        decodedSignalsGrid.Dock = DockStyle.Fill;
        decodedSignalsGrid.MultiSelect = false;
        decodedSignalsGrid.Name = "decodedSignalsGrid";
        decodedSignalsGrid.ReadOnly = true;
        decodedSignalsGrid.RowHeadersVisible = false;
        decodedSignalsGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        decodedPhysicalColumn.HeaderText = "Physical";
        decodedPhysicalColumn.Name = "decodedPhysicalColumn";
        decodedPhysicalColumn.Width = 120;
        decodedFunctionColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        decodedFunctionColumn.HeaderText = "Function";
        decodedFunctionColumn.Name = "decodedFunctionColumn";
        decodedLogicalColumn.HeaderText = "Logical";
        decodedLogicalColumn.Name = "decodedLogicalColumn";
        decodedLogicalColumn.Width = 170;
        decodedStateColumn.HeaderText = "State";
        decodedStateColumn.Name = "decodedStateColumn";
        decodedStateColumn.Width = 140;
        decodedRawColumn.HeaderText = "Raw";
        decodedRawColumn.Name = "decodedRawColumn";
        decodedRawColumn.Width = 320;
        decodedVerificationColumn.HeaderText = "Verification";
        decodedVerificationColumn.Name = "decodedVerificationColumn";
        decodedVerificationColumn.Width = 130;

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        Controls.Add(rootLayout);
        Name = "OtmrRcmLiveControl";
        Size = new Size(1200, 700);
        ((System.ComponentModel.ISupportInitialize)decodedSignalsGrid).EndInit();
        statusLayout.ResumeLayout(true);
        rootLayout.ResumeLayout(true);
        ResumeLayout(true);
    }
}
