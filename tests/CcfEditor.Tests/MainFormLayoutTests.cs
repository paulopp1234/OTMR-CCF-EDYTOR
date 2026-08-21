using System.Reflection;
using CcfEditor.Core;
using CcfEditor.Otmr.Live;
using CcfEditor.Otmr.Rcm;
using CcfEditor.WinForms;

namespace CcfEditor.Tests;

public sealed class MainFormLayoutTests
{
    [Fact]
    public void VisibleBenchReceivesLoadedAndReplacementCcfWithoutManualRefresh()
    {
        RunInStaThread(() =>
        {
            string directory = Path.Combine(Path.GetTempPath(), $"CcfEditor.BenchSync.{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            string firstPath = Path.Combine(directory, "CLASS171_v26_J2_ABCDE.ccf");
            string secondPath = Path.Combine(directory, "CLASS171_replacement.ccf");
            File.Copy(FindFromRoot("TestData", "CLASS171_GUI_TEST.ccf"), firstPath);
            File.Copy(firstPath, secondPath);
            try
            {
                using var form = new MainForm();
                form.Show();
                TabControl tabs = Find<TabControl>(form, "tabs");
                tabs.SelectedIndex = 5;
                Application.DoEvents();

                Label benchStatus = Find<Label>(form, "ccfStatusLabel");
                Button createProfile = Find<Button>(form, "createRcmProfileButton");
                Assert.Equal("CCF NOT LOADED", benchStatus.Text);
                Assert.False(createProfile.Enabled);

                InvokeLoadCcf(form, firstPath);
                Application.DoEvents();

                ToolStripStatusLabel mainStatus = FindToolStripItem<ToolStripStatusLabel>(form, "fileStatusLabel");
                Assert.Contains("CLASS171_v26_J2_ABCDE.ccf", mainStatus.Text, StringComparison.Ordinal);
                Assert.Contains("26,600 bytes", mainStatus.Text, StringComparison.Ordinal);
                Assert.Contains("CLASS171_v26_J2_ABCDE.ccf", benchStatus.Text, StringComparison.Ordinal);
                Assert.DoesNotContain("CCF NOT LOADED", benchStatus.Text, StringComparison.Ordinal);
                Assert.True(createProfile.Enabled);

                createProfile.PerformClick();
                Application.DoEvents();
                DataGridView grid = Find<DataGridView>(form, "rcmGrid");
                ComboBox filter = Find<ComboBox>(form, "connectorComboBox");
                OtmrBenchControl bench = Find<OtmrBenchControl>(form, "otmrBenchControl");
                RcmProfile profile = GetPrivateField<RcmProfile>(bench, "_rcmProfile");
                Assert.Equal(RcmInputFilter.All, filter.SelectedItem);
                Assert.Equal(profile.Pins.Count, grid.Rows.Count);
                AssertImported(profile, 0, 12, 0, 0, "Throttle 1");
                AssertImported(profile, 1, 13, 0, 1, "Throttle 2");
                AssertImported(profile, 2, 14, 0, 2, "Throttle 3");

                int importedCount = profile.Pins.Count;
                RcmProfileEditor.AddConnector(profile, "J1");
                InvokePrivate(bench, "PopulateConnectors");
                Application.DoEvents();
                Assert.Equal(RcmInputFilter.All, filter.SelectedItem);
                Assert.Equal(importedCount, grid.Rows.Count);
                filter.SelectedItem = RcmInputFilter.Unassigned;
                Application.DoEvents();
                Assert.Equal(importedCount, grid.Rows.Count);

                RcmPinProfile throttle1 = profile.Pins.Single(pin => pin.Function == "Throttle 1");
                RcmProfileEditor.AssignPhysical(profile, throttle1.Id, "J1", "A");
                filter.SelectedItem = RcmInputFilter.All;
                InvokePrivate(bench, "RenderTable", throttle1.Id);
                Application.DoEvents();
                Assert.Equal(importedCount, grid.Rows.Count);
                filter.SelectedItem = "J1";
                Application.DoEvents();
                DataGridViewRow assigned = Assert.Single(grid.Rows.Cast<DataGridViewRow>());
                Assert.Equal("J1", Convert.ToString(assigned.Cells["connectorColumn"].Value));
                Assert.Equal("A", Convert.ToString(assigned.Cells["pinColumn"].Value));

                InvokeLoadCcf(form, secondPath);
                Application.DoEvents();
                Assert.Contains("CLASS171_replacement.ccf", mainStatus.Text, StringComparison.Ordinal);
                Assert.Contains("CLASS171_replacement.ccf", benchStatus.Text, StringComparison.Ordinal);
                CcfDocument benchDocument = GetPrivateField<CcfDocument>(bench, "_document");
                Assert.Equal(Path.GetFullPath(secondPath), benchDocument.SourcePath);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        });
    }

    [Fact]
    public void RelevantCcfEditIsPushedToBenchBeforeNewProfileCreation()
    {
        RunInStaThread(() =>
        {
            string ccfPath = FindFromRoot("TestData", "CLASS171_GUI_TEST.ccf");
            using var form = new MainForm();
            form.Show();
            TabControl tabs = Find<TabControl>(form, "tabs");
            tabs.SelectedIndex = 5;
            Application.DoEvents();
            InvokeLoadCcf(form, ccfPath);
            Application.DoEvents();

            OtmrBenchControl bench = Find<OtmrBenchControl>(form, "otmrBenchControl");
            CcfDocument hostDocument = GetPrivateField<CcfDocument>(form, "_document");
            CcfEditService.SetRecordName(hostDocument, 1, "Throttle 2 edit");
            InvokePrivate(form, "RefreshAfterEdit", 1);
            Application.DoEvents();

            Assert.Same(hostDocument, GetPrivateField<CcfDocument>(bench, "_document"));
            Assert.Contains("CCF loaded", Find<Label>(form, "ccfStatusLabel").Text, StringComparison.Ordinal);
            Find<Button>(form, "createRcmProfileButton").PerformClick();
            Application.DoEvents();
            RcmProfile profile = GetPrivateField<RcmProfile>(bench, "_rcmProfile");
            Assert.Equal("Throttle 2 edit", profile.Pins.Single(pin => pin.CcfReference?.RecordA == 1).Function);
        });
    }

    [Fact]
    public void EveryTabRendersExpectedControlsAndLoadedCcfData()
    {
        RunInStaThread(() =>
        {
            string ccfPath = Path.Combine(Path.GetTempPath(), $"CcfEditor.UiSmoke.{Guid.NewGuid():N}.ccf");
            File.WriteAllBytes(ccfPath, TestCcfFactory.CreateDeterministicFile());
            try
            {
                using var form = new MainForm();
                form.Show();
                Application.DoEvents();
                InvokeLoadCcf(form, ccfPath);
                Application.DoEvents();

                TabControl tabs = Find<TabControl>(form, "tabs");
                Assert.Equal(new[]
                {
                    "Records - edit known fields in grid",
                    "Header - edit supported Decoded cells",
                    "Hex",
                    "Validation",
                    "OTMR Live",
                    "OTMR I/O Bench"
                }, tabs.TabPages.Cast<TabPage>().Select(page => page.Text));

                foreach (TabPage page in tabs.TabPages)
                {
                    tabs.SelectedTab = page;
                    Application.DoEvents();
                    Control content = Assert.Single(page.Controls.Cast<Control>());
                    Assert.True(content.Visible, $"{page.Text}: root content is not visible.");
                    Assert.True(content.Width >= page.DisplayRectangle.Width * 0.9);
                    Assert.True(content.Height >= page.DisplayRectangle.Height * 0.9);
                }

                Assert.Equal(256, Find<DataGridView>(form, "recordsGrid").Rows.Count);
                Assert.NotEmpty(Find<DataGridView>(form, "headerGrid").Rows.Cast<DataGridViewRow>());
                Assert.NotEmpty(Find<DataGridView>(form, "hexGrid").Rows.Cast<DataGridViewRow>());
                Assert.NotEmpty(Find<ListView>(form, "validationList").Columns.Cast<ColumnHeader>());

                tabs.SelectedIndex = 4;
                Application.DoEvents();
                foreach (string text in new[]
                         { "Refresh Ports", "38400", "8", "None", "1", "Connect", "Start OTMR Live", "Stop / Disconnect", "Clear", "Save Capture", "Copy Hex" })
                    AssertVisibleText(tabs.SelectedTab!, text);
                Assert.Equal("DISCONNECTED", Find<Label>(form, "liveStateLabel").Text);
                Assert.False(Find<Button>(form, "startLiveButton").Enabled);

                tabs.SelectedIndex = 5;
                Application.DoEvents();
                foreach (string text in new[]
                {
                    "RCM: Class 171", "Import Pin Map...", "Refresh CCF", "New RCM Profile From CCF",
                    "Open RCM Profile", "Save RCM Profile", "Save RCM Profile As", "Add Connector",
                    "Rename Connector", "Edit Pin Sequence", "Delete Connector", "Add Input / Pin", "Edit Selected Input",
                    "Assign Next Pin",
                    "Delete Input / Pin", "Capture Voltage Removed", "Capture +24V Applied",
                    "Compare States", "Reset Test Evidence"
                }) AssertVisibleText(tabs.SelectedTab!, text);

                DataGridView benchGrid = Find<DataGridView>(form, "rcmGrid");
                Assert.True(benchGrid.Width >= 600, $"The RCM table is collapsed to {benchGrid.Width}px.");
                Find<Button>(form, "createRcmProfileButton").PerformClick();
                Application.DoEvents();
                Assert.NotEmpty(benchGrid.Rows.Cast<DataGridViewRow>());
                Assert.Equal(RcmInputFilter.All, Find<ComboBox>(form, "connectorComboBox").SelectedItem);
                Assert.Equal(new[]
                {
                    "Connector", "Pin", "Function", "Expected CCF", "Voltage Removed", "+24V Applied",
                    "State Difference", "Decoder", "RCM Result"
                }, benchGrid.Columns.Cast<DataGridViewColumn>().Select(column => column.HeaderText));

                ToolStripStatusLabel status = FindToolStripItem<ToolStripStatusLabel>(form, "fileStatusLabel");
                Assert.Contains("26,600 bytes", status.Text, StringComparison.Ordinal);
                Assert.Contains("256 records", status.Text, StringComparison.Ordinal);
            }
            finally { File.Delete(ccfPath); }
        });
    }

    [Fact]
    public void RcmWorkflowUsesJsonAssignmentsAndDisablesNonTestablePins()
    {
        RunInStaThread(() =>
        {
            string ccfPath = FindFromRoot("TestData", "CLASS171_GUI_TEST.ccf");
            byte[] sourceBefore = File.ReadAllBytes(ccfPath);
            using var form = new MainForm();
            form.Show();
            Application.DoEvents();
            InvokeLoadCcf(form, ccfPath);
            TabControl tabs = Find<TabControl>(form, "tabs");
            tabs.SelectedIndex = 5;
            Application.DoEvents();

            Find<Button>(form, "createRcmProfileButton").PerformClick();
            Application.DoEvents();
            DataGridView grid = Find<DataGridView>(form, "rcmGrid");
            Assert.NotEmpty(grid.Rows.Cast<DataGridViewRow>());
            Assert.All(grid.Rows.Cast<DataGridViewRow>(), row =>
                Assert.Equal(string.Empty, Convert.ToString(row.Cells["pinColumn"].Value)));
            Assert.False(Find<Button>(form, "captureVoltageRemovedButton").Enabled);
            Assert.False(Find<Button>(form, "captureVoltageAppliedButton").Enabled);

            OtmrBenchControl bench = Find<OtmrBenchControl>(form, "otmrBenchControl");
            RcmProfile profile = GetPrivateField<RcmProfile>(bench, "_rcmProfile");
            RcmPinProfile testable = profile.Pins.Single(pin => pin.CcfReference?.RecordA == 0);
            RcmInputEdit edit = RcmInputEdit.From(testable);
            edit.Connector = "J1";
            edit.Pin = "A";
            edit.Testable = true;
            RcmProfileEditor.UpdateInput(profile, testable.Id, edit);
            RcmProfileEditor.AddInput(profile, new RcmInputEdit
            {
                Connector = "J1", Pin = "L", Function = "Input return", Role = "return",
                SafetyClassification = "Return; do not apply +V", Testable = false
            });
            InvokePrivate(bench, "PopulateConnectors");
            Find<ComboBox>(form, "connectorComboBox").SelectedItem = "J1";
            InvokePrivate(bench, "RenderTable", testable.Id);
            Application.DoEvents();

            Label selected = Find<Label>(form, "selectedPinLabel");
            Assert.Contains("J1-A", selected.Text, StringComparison.Ordinal);
            Assert.Contains("Throttle 1", selected.Text, StringComparison.Ordinal);
            Assert.Contains("Expected CCF records: 0", selected.Text, StringComparison.Ordinal);
            Assert.False(Find<Button>(form, "captureVoltageRemovedButton").Enabled);
            Assert.False(Find<Button>(form, "captureVoltageAppliedButton").Enabled);
            InvokePrivate(bench, "SetOtmrLiveState", OtmrLiveState.ConnectedIdle);
            Application.DoEvents();
            Assert.False(Find<Button>(form, "captureVoltageRemovedButton").Enabled);
            Assert.False(Find<Button>(form, "captureVoltageAppliedButton").Enabled);
            InvokePrivate(bench, "SetOtmrLiveState", OtmrLiveState.WaitingForLiveFrames);
            Application.DoEvents();
            Assert.False(Find<Button>(form, "captureVoltageRemovedButton").Enabled);
            Assert.False(Find<Button>(form, "captureVoltageAppliedButton").Enabled);
            InvokePrivate(bench, "SetOtmrLiveState", OtmrLiveState.LiveActive);
            Application.DoEvents();
            Assert.True(Find<Button>(form, "captureVoltageRemovedButton").Enabled);
            Assert.True(Find<Button>(form, "captureVoltageAppliedButton").Enabled);
            Assert.False(Find<Button>(form, "compareStatesButton").Enabled);

            DataGridViewRow returnRow = grid.Rows.Cast<DataGridViewRow>()
                .Single(row => Convert.ToString(row.Cells["pinColumn"].Value) == "L");
            grid.CurrentCell = returnRow.Cells[0];
            returnRow.Selected = true;
            Application.DoEvents();
            Assert.Contains("J1-L", selected.Text, StringComparison.Ordinal);
            Assert.False(Find<Button>(form, "captureVoltageRemovedButton").Enabled);
            Assert.False(Find<Button>(form, "captureVoltageAppliedButton").Enabled);
            Assert.Equal("NOT TESTABLE", Convert.ToString(returnRow.Cells["voltageRemovedColumn"].Value));
            Assert.Contains("NOT TESTABLE", Find<TextBox>(form, "evidenceTextBox").Text, StringComparison.Ordinal);
            Assert.Equal(sourceBefore, File.ReadAllBytes(ccfPath));
        });
    }

    [Fact]
    public void RcmGridEditsAssignExistingLogicalRowAndFiltersRemainAccurate()
    {
        RunInStaThread(() =>
        {
            string ccfPath = FindFromRoot("TestData", "CLASS171_GUI_TEST.ccf");
            using var form = new MainForm();
            form.Show();
            Application.DoEvents();
            InvokeLoadCcf(form, ccfPath);
            TabControl tabs = Find<TabControl>(form, "tabs");
            tabs.SelectedIndex = 5;
            Application.DoEvents();
            Find<Button>(form, "createRcmProfileButton").PerformClick();
            Application.DoEvents();

            OtmrBenchControl bench = Find<OtmrBenchControl>(form, "otmrBenchControl");
            RcmProfile profile = GetPrivateField<RcmProfile>(bench, "_rcmProfile");
            DataGridView grid = Find<DataGridView>(form, "rcmGrid");
            ComboBox filter = Find<ComboBox>(form, "connectorComboBox");
            int originalCount = profile.Pins.Count;
            RcmPinProfile logical = profile.Pins.First();
            Guid id = logical.Id;
            logical.VoltageRemoved.CandidateRawSignature = "retained-evidence";
            RcmStateEvidence originalEvidence = logical.VoltageRemoved;
            (int? A, int? B, int? Card, int? Channel) mapping =
                (logical.CcfReference!.RecordA, logical.CcfReference.RecordB,
                    logical.CcfReference.LogicalCard, logical.CcfReference.LogicalChannel);

            RcmProfileEditor.AddConnector(profile, "J1");
            InvokePrivate(bench, "PopulateConnectors");
            Application.DoEvents();
            Assert.Equal(RcmInputFilter.All, filter.SelectedItem);
            Assert.Equal(originalCount, grid.Rows.Count);

            DataGridViewRow row = grid.Rows.Cast<DataGridViewRow>().Single(candidate => Equals(candidate.Tag, id));
            grid.CurrentCell = row.Cells["connectorColumn"];
            Assert.True(grid.BeginEdit(true));
            Assert.IsType<DataGridViewComboBoxEditingControl>(grid.EditingControl).Text = "J1";
            Assert.True(grid.EndEdit());
            Assert.Same(row, grid.Rows.Cast<DataGridViewRow>().Single(candidate => Equals(candidate.Tag, id)));
            Application.DoEvents();

            Assert.Same(row, grid.Rows.Cast<DataGridViewRow>().Single(candidate => Equals(candidate.Tag, id)));
            grid.CurrentCell = row.Cells["pinColumn"];
            Assert.True(grid.BeginEdit(true));
            Assert.IsType<DataGridViewTextBoxEditingControl>(grid.EditingControl).Text = "A";
            Assert.True(grid.EndEdit());
            Assert.Same(row, grid.Rows.Cast<DataGridViewRow>().Single(candidate => Equals(candidate.Tag, id)));
            Application.DoEvents();

            RcmPinProfile assigned = profile.GetInput(id);
            Assert.Equal(id, assigned.Id);
            Assert.Equal("J1", assigned.Connector);
            Assert.Equal("A", assigned.Pin);
            Assert.Equal(mapping, (assigned.CcfReference!.RecordA, assigned.CcfReference.RecordB,
                assigned.CcfReference.LogicalCard, assigned.CcfReference.LogicalChannel));
            Assert.Same(originalEvidence, assigned.VoltageRemoved);
            Assert.Equal("retained-evidence", assigned.VoltageRemoved.CandidateRawSignature);
            Assert.Equal(originalCount, profile.Pins.Count);
            Assert.Contains("J1-A", Find<Label>(form, "selectedPinLabel").Text, StringComparison.Ordinal);

            grid.CurrentCell = row.Cells["pinColumn"];
            Assert.True(grid.BeginEdit(true));
            Assert.IsType<DataGridViewTextBoxEditingControl>(grid.EditingControl).Text = "B";
            Assert.True(grid.EndEdit());
            Assert.Same(row, grid.Rows.Cast<DataGridViewRow>().Single(candidate => Equals(candidate.Tag, id)));
            Application.DoEvents();
            Assert.Equal(id, assigned.Id);
            Assert.Equal("J1", assigned.Connector);
            Assert.Equal("B", assigned.Pin);
            Assert.Same(originalEvidence, assigned.VoltageRemoved);
            Assert.Equal(mapping, (assigned.CcfReference!.RecordA, assigned.CcfReference.RecordB,
                assigned.CcfReference.LogicalCard, assigned.CcfReference.LogicalChannel));
            Assert.Contains("J1-B", Find<Label>(form, "selectedPinLabel").Text, StringComparison.Ordinal);

            RcmPinProfile secondLogical = profile.Pins[1];
            row = grid.Rows.Cast<DataGridViewRow>().Single(candidate => Equals(candidate.Tag, secondLogical.Id));
            grid.CurrentCell = row.Cells["connectorColumn"];
            Assert.True(grid.BeginEdit(true));
            Assert.IsType<DataGridViewComboBoxEditingControl>(grid.EditingControl).Text = "J2";
            Assert.True(grid.EndEdit());
            Application.DoEvents();
            row = grid.Rows.Cast<DataGridViewRow>().Single(candidate => Equals(candidate.Tag, secondLogical.Id));
            grid.CurrentCell = row.Cells["pinColumn"];
            Assert.True(grid.BeginEdit(true));
            Assert.IsType<DataGridViewTextBoxEditingControl>(grid.EditingControl).Text = "B";
            Assert.True(grid.EndEdit());
            Application.DoEvents();
            Assert.Equal("J2", secondLogical.Connector);
            Assert.Equal("B", secondLogical.Pin);
            Assert.Contains(profile.Connectors, connector => connector.Name == "J2");

            filter.SelectedItem = "J1";
            Application.DoEvents();
            Assert.Single(grid.Rows.Cast<DataGridViewRow>());
            filter.SelectedItem = RcmInputFilter.Unassigned;
            Application.DoEvents();
            Assert.Equal(originalCount - 2, grid.Rows.Count);
            filter.SelectedItem = RcmInputFilter.All;
            Application.DoEvents();
            Assert.Equal(originalCount, grid.Rows.Count);
            Assert.Contains("Assigned physical inputs: 2", Find<Label>(form, "progressLabel").Text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void CompletedAssignmentLeavesUnassignedFilterOnlyAfterEditTransactionUnwinds()
    {
        RunInStaThread(() =>
        {
            using var form = new MainForm();
            form.Show();
            InvokeLoadCcf(form, FindFromRoot("TestData", "CLASS171_GUI_TEST.ccf"));
            Find<TabControl>(form, "tabs").SelectedIndex = 5;
            Application.DoEvents();
            Find<Button>(form, "createRcmProfileButton").PerformClick();
            Application.DoEvents();

            OtmrBenchControl bench = Find<OtmrBenchControl>(form, "otmrBenchControl");
            RcmProfile profile = GetPrivateField<RcmProfile>(bench, "_rcmProfile");
            RcmProfileEditor.AddConnector(profile, "J1");
            InvokePrivate(bench, "PopulateConnectors");
            ComboBox filter = Find<ComboBox>(form, "connectorComboBox");
            filter.SelectedItem = RcmInputFilter.Unassigned;
            Application.DoEvents();

            DataGridView grid = Find<DataGridView>(form, "rcmGrid");
            DataGridViewRow row = grid.Rows[0];
            Guid id = Assert.IsType<Guid>(row.Tag);
            RcmPinProfile input = profile.GetInput(id);

            grid.CurrentCell = row.Cells["connectorColumn"];
            Assert.True(grid.BeginEdit(true));
            Assert.IsType<DataGridViewComboBoxEditingControl>(grid.EditingControl).Text = "J1";
            Assert.True(grid.EndEdit());
            Assert.Contains(row, grid.Rows.Cast<DataGridViewRow>());
            Application.DoEvents();

            row = grid.Rows.Cast<DataGridViewRow>().Single(candidate => Equals(candidate.Tag, id));
            grid.CurrentCell = row.Cells["pinColumn"];
            Assert.True(grid.BeginEdit(true));
            Assert.IsType<DataGridViewTextBoxEditingControl>(grid.EditingControl).Text = "A";
            Assert.True(grid.EndEdit());
            Assert.Contains(row, grid.Rows.Cast<DataGridViewRow>());
            Assert.Equal(id, input.Id);
            Application.DoEvents();

            Assert.DoesNotContain(grid.Rows.Cast<DataGridViewRow>(), candidate => Equals(candidate.Tag, id));
            Assert.Equal("J1", input.Connector);
            Assert.Equal("A", input.Pin);
        });
    }

    [Fact]
    public void RcmWorkflowPanelWrapsUnassignedDetailsAtDesktopAndSmallerSizes()
    {
        RunInStaThread(() =>
        {
            using var form = new MainForm
            {
                StartPosition = FormStartPosition.Manual,
                Location = Point.Empty,
                Size = new Size(1920, 1000)
            };
            form.Show();
            InvokeLoadCcf(form, FindFromRoot("TestData", "CLASS171_GUI_TEST.ccf"));
            Find<TabControl>(form, "tabs").SelectedIndex = 5;
            Application.DoEvents();
            Find<Button>(form, "createRcmProfileButton").PerformClick();
            Application.DoEvents();

            DataGridView grid = Find<DataGridView>(form, "rcmGrid");
            DataGridViewRow brake = grid.Rows.Cast<DataGridViewRow>()
                .Single(row => Convert.ToString(row.Cells["functionColumn"].Value) == "Brake 1");
            grid.CurrentCell = brake.Cells["functionColumn"];
            brake.Selected = true;
            Application.DoEvents();

            SplitContainer split = Find<SplitContainer>(form, "mainSplit");
            Label summary = Find<Label>(form, "selectedPinLabel");
            Label removedInstruction = Find<Label>(form, "voltageRemovedInstructionLabel");
            Label appliedInstruction = Find<Label>(form, "voltageAppliedInstructionLabel");
            TextBox evidence = Find<TextBox>(form, "evidenceTextBox");
            Assert.Equal(480, split.Panel2MinSize);
            Assert.False(split.IsSplitterFixed);
            Assert.True(split.Panel2.Width >= 480, $"Workflow panel is only {split.Panel2.Width}px wide.");
            double workflowRatio = (double)split.Panel2.Width / split.Width;
            Assert.InRange(workflowRatio, 0.27, 0.36);
            Assert.Contains("UNASSIGNED", summary.Text, StringComparison.Ordinal);
            Assert.Contains("Brake 1", summary.Text, StringComparison.Ordinal);
            Assert.Contains("Expected CCF records: 10 ↔ 22", summary.Text, StringComparison.Ordinal);
            Assert.Contains("Card 0 / Channel 10", summary.Text, StringComparison.Ordinal);
            Assert.Contains("Connector: NOT ASSIGNED", summary.Text, StringComparison.Ordinal);
            Assert.Contains("Pin: NOT ASSIGNED", summary.Text, StringComparison.Ordinal);
            Assert.Equal("PHYSICAL MAPPING REQUIRED", removedInstruction.Text);
            Assert.Equal("PHYSICAL MAPPING REQUIRED", appliedInstruction.Text);
            Assert.False(Find<Button>(form, "captureVoltageRemovedButton").Enabled);
            Assert.False(Find<Button>(form, "captureVoltageAppliedButton").Enabled);
            Assert.False(Find<Button>(form, "compareStatesButton").Enabled);
            Assert.True(Find<Button>(form, "editSelectedWorkflowButton").Enabled);
            Assert.True(evidence.WordWrap);
            Assert.Equal(ScrollBars.Vertical, evidence.ScrollBars);
            Assert.Contains("Assign Connector and Pin before performing an RCM electrical test.", evidence.Text,
                StringComparison.Ordinal);

            form.Size = new Size(1200, 760);
            Application.DoEvents();
            Assert.True(split.Panel2.Width >= 480, $"Smaller workflow panel is only {split.Panel2.Width}px wide.");
            Assert.True(split.Panel1.Width >= split.Panel1MinSize,
                $"Smaller grid panel is only {split.Panel1.Width}px wide.");
            Assert.True(summary.Height >= summary.PreferredHeight,
                $"Selected input summary is clipped: {summary.Height}px versus preferred {summary.PreferredHeight}px.");
        });
    }

    private static void InvokeLoadCcf(MainForm form, string path) =>
        typeof(MainForm).GetMethod("LoadCcf", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, new object[] { path });

    private static void InvokePrivate(object target, string name, params object?[] arguments)
    {
        MethodInfo method = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate => candidate.Name == name && candidate.GetParameters().Length == arguments.Length);
        method.Invoke(target, arguments);
    }

    private static T GetPrivateField<T>(object target, string name) where T : class =>
        (T)(target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)
            ?? throw new Xunit.Sdk.XunitException($"Field '{name}' is null."));

    private static T Find<T>(Control root, string name) where T : Control =>
        root.Controls.Find(name, true).OfType<T>().SingleOrDefault()
        ?? throw new Xunit.Sdk.XunitException($"Control '{name}' was not found.");

    private static void AssertImported(
        RcmProfile profile,
        int recordA,
        int recordB,
        int card,
        int channel,
        string function)
    {
        RcmPinProfile pin = profile.Pins.Single(candidate => candidate.CcfReference?.RecordA == recordA);
        Assert.Equal(recordB, pin.CcfReference!.RecordB);
        Assert.Equal(card, pin.CcfReference.LogicalCard);
        Assert.Equal(channel, pin.CcfReference.LogicalChannel);
        Assert.Equal(function, pin.Function);
        Assert.Equal(string.Empty, pin.Connector);
        Assert.Equal(string.Empty, pin.Pin);
        Assert.Equal(RcmResultStates.Unassigned, pin.RcmResult);
    }

    private static T FindToolStripItem<T>(Control root, string name) where T : ToolStripItem
    {
        foreach (ToolStrip strip in Descendants(root).OfType<ToolStrip>())
        {
            T? match = strip.Items.Find(name, true).OfType<T>().SingleOrDefault();
            if (match is not null) return match;
        }
        throw new Xunit.Sdk.XunitException($"ToolStrip item '{name}' was not found.");
    }

    private static void AssertVisibleText(Control root, string text)
    {
        Control? match = Descendants(root).FirstOrDefault(control =>
            control.Visible && string.Equals(control.Text, text, StringComparison.Ordinal));
        Assert.NotNull(match);
        Assert.True(match.Width > 0 && match.Height > 0, $"'{text}' has empty bounds.");
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control descendant in Descendants(child)) yield return descendant;
        }
    }

    private static string FindFromRoot(params string[] parts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new Xunit.Sdk.XunitException($"Fixture not found: {Path.Combine(parts)}");
    }

    private static void RunInStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { ApplicationConfiguration.Initialize(); action(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The UI smoke test timed out.");
        if (failure is not null) throw new AggregateException(failure);
    }
}
