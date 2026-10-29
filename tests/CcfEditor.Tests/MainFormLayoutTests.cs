using System.Reflection;
using CcfEditor.Core;
using CcfEditor.Otmr.Capture;
using CcfEditor.Otmr.Live;
using CcfEditor.Otmr.Rcm;
using CcfEditor.Otmr.Sync;
using CcfEditor.WinForms;

namespace CcfEditor.Tests;

public sealed class MainFormLayoutTests
{
    [Fact]
    public void DesktopApplicationVersionMetadataAndWindowTitleAreV030()
    {
        Assembly desktopAssembly = typeof(MainForm).Assembly;
        AssemblyInformationalVersionAttribute informational = Assert.IsType<AssemblyInformationalVersionAttribute>(
            desktopAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>());

        Assert.Equal(new Version(0, 3, 0, 0), desktopAssembly.GetName().Version);
        Assert.Equal("0.3.0.0", System.Diagnostics.FileVersionInfo.GetVersionInfo(desktopAssembly.Location).FileVersion);
        Assert.Equal("0.3.0", informational.InformationalVersion);

        RunInStaThread(() =>
        {
            using var form = new MainForm();
            Assert.Equal("OTMR CCF Editor / Creator v0.3.0 - test OTMR M1 + I/O Bench", form.Text);
            Assert.Equal(
                "No CCF loaded | v0.3.0 test / OTMR M1 + I/O Bench",
                FindToolStripItem<ToolStripStatusLabel>(form, "fileStatusLabel").Text);
        });
    }

    [Fact]
    public void OtmrLiveRawCommunicationLogProvidesRealHorizontalAndVerticalScrolling()
    {
        RunInStaThread(() =>
        {
            using var form = new MainForm
            {
                StartPosition = FormStartPosition.Manual,
                Location = Point.Empty,
                Size = new Size(1200, 760)
            };
            form.Show();
            TabControl tabs = Find<TabControl>(form, "tabs");
            tabs.SelectedIndex = 4;
            Application.DoEvents();

            DataGridView capture = Find<DataGridView>(form, "captureGrid");
            DataGridViewColumn time = capture.Columns["timeColumn"]!;
            DataGridViewColumn direction = capture.Columns["directionColumn"]!;
            DataGridViewColumn rawBytes = capture.Columns["bytesColumn"]!;
            DataGridViewColumn interpretation = capture.Columns["interpretationColumn"]!;

            Assert.Equal(ScrollBars.Both, capture.ScrollBars);
            Assert.Equal(DataGridViewAutoSizeColumnsMode.None, capture.AutoSizeColumnsMode);
            Assert.All(capture.Columns.Cast<DataGridViewColumn>(), column =>
                Assert.Equal(DataGridViewAutoSizeColumnMode.None, column.AutoSizeMode));
            Assert.Equal(110, time.Width);
            Assert.Equal(65, direction.Width);
            Assert.Equal(1100, rawBytes.Width);
            Assert.True(rawBytes.MinimumWidth >= 900);
            Assert.Equal(1000, interpretation.Width);
            Assert.True(interpretation.MinimumWidth >= 800);
            Assert.True(time.Frozen);
            Assert.True(direction.Frozen);
            Assert.False(rawBytes.Frozen);
            Assert.False(interpretation.Frozen);

            int totalColumnWidth = capture.Columns.Cast<DataGridViewColumn>().Sum(column => column.Width);
            Assert.Equal(2275, totalColumnWidth);
            Assert.True(totalColumnWidth > 1500);
            Assert.True(totalColumnWidth > capture.ClientSize.Width,
                $"Column width {totalColumnWidth}px must overflow the {capture.ClientSize.Width}px viewport.");

            const string exactRawBytes =
                "FB FB 0C 00 0C 00 0C FF D2 07 FB FB 00 FF D2 28";
            const string exactInterpretation =
                "VERIFIED: J1-A Throttle 1=ACTIVE [12] | Payload: 0C 00 0C 00 0C | Trailing: D2 07 (UNKNOWN)";
            capture.Rows.Add("12:34:56.789", "RX", exactRawBytes, exactInterpretation);
            for (int index = 0; index < 80; index++)
                capture.Rows.Add("12:34:56.789", "RX", $"ROW {index:X2}", $"Interpretation row {index}");
            Application.DoEvents();

            Assert.Equal(exactRawBytes, Convert.ToString(capture.Rows[0].Cells["bytesColumn"].Value));
            Assert.Equal(exactInterpretation,
                Convert.ToString(capture.Rows[0].Cells["interpretationColumn"].Value));

            HScrollBar horizontal = Assert.Single(capture.Controls.OfType<HScrollBar>());
            VScrollBar vertical = Assert.Single(capture.Controls.OfType<VScrollBar>());
            Assert.True(horizontal.Visible, "The overflowing fixed-width columns must show a horizontal scrollbar.");
            Assert.True(vertical.Visible, "The populated grid must retain its vertical scrollbar.");

            capture.HorizontalScrollingOffset = 500;
            capture.FirstDisplayedScrollingRowIndex = 50;
            Application.DoEvents();
            Assert.True(capture.HorizontalScrollingOffset > 0);
            Assert.True(capture.FirstDisplayedScrollingRowIndex > 0);
        });
    }

    [Fact]
    public void OtmrLiveLayoutKeepsCaptureGridUsableAndCaptureUpdatesSurviveTemporaryNoRoomState()
    {
        RunInStaThread(() =>
        {
            using var form = new MainForm
            {
                StartPosition = FormStartPosition.Manual,
                Location = Point.Empty,
                Size = new Size(1500, 900)
            };
            form.Show();
            TabControl tabs = Find<TabControl>(form, "tabs");
            tabs.SelectedIndex = 4;
            Application.DoEvents();

            OtmrLiveControl live = Find<OtmrLiveControl>(form, "otmrLiveControl");
            OtmrLiveService liveService = GetPrivateField<OtmrLiveService>(live, "_liveService");
            DataGridView capture = Find<DataGridView>(live, "captureGrid");

            Assert.Empty(live.Controls.Find("syncServerUrlTextBox", true));
            Assert.Empty(live.Controls.Find("syncPendingRecordingsButton", true));
            Assert.Empty(live.Controls.Find("decodedSignalsGrid", true));
            Assert.Empty(live.Controls.Find("decodedSignalsGroupBox", true));
            Assert.True(capture.ClientSize.Height >= 300,
                $"Desktop capture viewport is only {capture.ClientSize.Height}px high; live={live.Height}, root={Find<TableLayoutPanel>(live, "rootLayout").Height}, connection={Find<GroupBox>(live, "connectionGroupBox").Height}, database={Find<GroupBox>(live, "databaseRecordingGroupBox").Height}, captureGroup={Find<GroupBox>(live, "captureGroupBox").Height}.");
            Assert.True(capture.ClientSize.Height >= live.ClientSize.Height * 0.40,
                $"Raw communication log ({capture.ClientSize.Height}px) does not receive most remaining OTMR Live space ({live.ClientSize.Height}px total)." );
            int desktopCaptureHeight = capture.ClientSize.Height;
            Console.WriteLine($"OTMR Live desktop layout: capture={desktopCaptureHeight}px.");

            for (int index = 0; index < 12; index++)
            {
                InvokePrivate(liveService, "AddCapture", new OtmrCaptureEntry(
                    DateTimeOffset.UtcNow.AddMilliseconds(index),
                    OtmrDirection.Rx,
                    new byte[] { 0xFB, 0xFB, (byte)index, 0xFF },
                    "layout regression"));
            }
            Application.DoEvents();
            Assert.Equal(12, capture.Rows.Count);
            Assert.True(DataGridViewViewport.TryScrollToRow(capture, capture.Rows.Count - 1));

            // Reproduce the runtime failure: the async capture callback arrives
            // while the lower grid has no row display area beneath its headers.
            Size minimum = capture.MinimumSize;
            Control parent = capture.Parent!;
            parent.SuspendLayout();
            capture.MinimumSize = Size.Empty;
            capture.Size = new Size(Math.Max(1, capture.Width), capture.ColumnHeadersHeight + 1);
            Assert.False(DataGridViewViewport.CanDisplayRows(capture));
            InvokePrivate(liveService, "AddCapture", new OtmrCaptureEntry(
                DateTimeOffset.UtcNow,
                OtmrDirection.Rx,
                new byte[] { 0xFB, 0xFB, 0x7E, 0xFF },
                "arrived during layout"));
            Assert.Equal(13, capture.Rows.Count);
            Assert.True(GetPrivateValue<bool>(live, "_captureScrollPending"));

            capture.MinimumSize = minimum;
            parent.ResumeLayout(performLayout: true);
            form.PerformLayout();
            Application.DoEvents();
            Assert.True(DataGridViewViewport.CanDisplayRows(capture));
            Assert.False(GetPrivateValue<bool>(live, "_captureScrollPending"));
            Assert.True(capture.FirstDisplayedScrollingRowIndex >= 0);

            InvokePrivate(live, "RefreshCaptureGrid");
            Application.DoEvents();
            Assert.Equal(13, capture.Rows.Count);

            form.Size = new Size(1200, 760);
            Application.DoEvents();
            Assert.True(capture.ClientSize.Height >= 250,
                $"Smaller-window capture viewport is only {capture.ClientSize.Height}px high.");
            int smallerCaptureHeight = capture.ClientSize.Height;
            Console.WriteLine($"OTMR Live smaller layout: capture={smallerCaptureHeight}px.");

            form.Size = new Size(1800, 1000);
            Application.DoEvents();
            Assert.True(capture.ClientSize.Height > desktopCaptureHeight,
                $"Expanding/maximizing the window did not increase the raw log: expanded={capture.ClientSize.Height}px, desktop={desktopCaptureHeight}px.");
            Console.WriteLine($"OTMR Live expanded layout: capture={capture.ClientSize.Height}px.");
        });
    }

    [Fact]
    public void RcmLiveHasFullSizeDecodedGridAndTabSelectionDoesNotChangeLiveStateOrInvokeSync()
    {
        RunInStaThread(() =>
        {
            using var form = new MainForm
            {
                StartPosition = FormStartPosition.Manual,
                Location = Point.Empty,
                Size = new Size(1500, 900)
            };
            form.Show();
            TabControl tabs = Find<TabControl>(form, "tabs");
            OtmrLiveControl live = Find<OtmrLiveControl>(form, "otmrLiveControl");
            OtmrLiveService service = GetPrivateField<OtmrLiveService>(live, "_liveService");
            InvokePrivate(service, "SetState", OtmrLiveState.LiveReady);
            Application.DoEvents();
            OtmrLiveState before = service.State;

            TabPage rcmTab = tabs.TabPages.Cast<TabPage>().Single(page => page.Text == "RCM LIVE");
            tabs.SelectedTab = rcmTab;
            Application.DoEvents();

            OtmrRcmLiveControl rcm = Find<OtmrRcmLiveControl>(rcmTab, "otmrRcmLiveControl");
            DataGridView grid = Find<DataGridView>(rcm, "decodedSignalsGrid");
            Assert.Equal(before, service.State);
            Assert.Equal(OtmrLiveState.LiveReady, before);
            Assert.True(grid.Visible && grid.ClientSize.Height >= 550,
                $"RCM LIVE decoded grid is only {grid.ClientSize.Height}px high.");
            Console.WriteLine($"RCM LIVE desktop layout: decoded grid={grid.ClientSize.Height}px, tab={rcmTab.ClientSize.Height}px.");
            Assert.Equal(new[] { "Physical", "Function", "Logical", "State", "Raw", "Verification" },
                grid.Columns.Cast<DataGridViewColumn>().Select(column => column.HeaderText));
            Assert.Empty(grid.Rows.Cast<DataGridViewRow>());
            Assert.Equal("No RCM profile loaded.",
                Find<Label>(rcm, "decodedSignalsStatusLabel").Text);
            Assert.Contains("OTMR LIVE READY", Find<Label>(rcm, "liveStateStatusLabel").Text,
                StringComparison.Ordinal);
            Assert.DoesNotContain(rcm.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic),
                field => typeof(OtmrManualSyncService).IsAssignableFrom(field.FieldType));
        });
    }

    [Fact]
    public void RcmLiveFollowsAuthoritativeLoadedJsonAcrossA_B_CAndCcfChanges()
    {
        RunInStaThread(() =>
        {
            string folder = Path.Combine(Path.GetTempPath(), $"CcfEditor.RcmLiveProfiles.{Guid.NewGuid():N}");
            Directory.CreateDirectory(folder);
            string pathA = Path.Combine(folder, "profile-A.json");
            string pathB = Path.Combine(folder, "profile-B.json");
            string pathC = Path.Combine(folder, "profile-C.json");
            try
            {
                RcmProfile profileA = ProfileWithVerifiedMappings("A", 3);
                RcmProfile profileB = ProfileWithVerifiedMappings("B", 7);
                RcmProfile profileC = ProfileWithVerifiedMappings("C", 0);
                RcmProfileJson.SaveAsync(pathA, profileA, DateTimeOffset.UtcNow).GetAwaiter().GetResult();
                RcmProfileJson.SaveAsync(pathB, profileB, DateTimeOffset.UtcNow).GetAwaiter().GetResult();
                RcmProfileJson.SaveAsync(pathC, profileC, DateTimeOffset.UtcNow).GetAwaiter().GetResult();

                using var form = new MainForm();
                form.Show();
                Application.DoEvents();
                OtmrBenchControl bench = Find<OtmrBenchControl>(form, "otmrBenchControl");
                OtmrRcmLiveControl rcm = Find<OtmrRcmLiveControl>(form, "otmrRcmLiveControl");
                OtmrLiveControl live = Find<OtmrLiveControl>(form, "otmrLiveControl");
                DataGridView grid = Find<DataGridView>(rcm, "decodedSignalsGrid");
                Label profileLabel = Find<Label>(rcm, "rcmProfileStatusLabel");
                Label countLabel = Find<Label>(rcm, "verifiedMappingsStatusLabel");

                Assert.Equal("No RCM profile loaded.", Find<Label>(rcm, "decodedSignalsStatusLabel").Text);
                InvokeLoadCcf(form, FindFromRoot("TestData", "CLASS171_GUI_TEST.ccf"));
                Application.DoEvents();
                Assert.Equal("No RCM profile loaded.", Find<Label>(rcm, "decodedSignalsStatusLabel").Text);
                InvokePrivate(bench, "CreateRcmProfileButton_Click", null, EventArgs.Empty);
                Application.DoEvents();
                Assert.NotNull(GetPrivateField<RcmProfile>(bench, "_rcmProfile"));
                Assert.Null(bench.GetType().GetField("_rcmProfilePath", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(bench));
                Assert.Equal("No RCM profile loaded.", Find<Label>(rcm, "decodedSignalsStatusLabel").Text);

                InvokePrivateAsync(bench, "LoadRcmProfileAsync", pathA, CancellationToken.None);
                Application.DoEvents();
                Assert.Empty(grid.Rows.Cast<DataGridViewRow>());
                Assert.Contains("profile-A.json", profileLabel.Text, StringComparison.Ordinal);
                Assert.Equal("Verified mappings: 3", countLabel.Text);
                Assert.Contains("Observed this session: 0",
                    Find<Label>(rcm, "decodedSignalsStatusLabel").Text, StringComparison.Ordinal);
                Assert.Contains("Verified mappings available: 3",
                    Find<Label>(rcm, "decodedSignalsStatusLabel").Text, StringComparison.Ordinal);

                InvokePrivateAsync(bench, "LoadRcmProfileAsync", pathB, CancellationToken.None);
                Application.DoEvents();
                Assert.Empty(grid.Rows.Cast<DataGridViewRow>());
                Assert.Contains("profile-B.json", profileLabel.Text, StringComparison.Ordinal);
                Assert.DoesNotContain("profile-A.json", profileLabel.Text, StringComparison.Ordinal);
                Assert.Equal("Verified mappings: 7", countLabel.Text);
                rcm.SetSourceConnectionId(Guid.NewGuid().ToString("D"));

                byte[] liveBytes = new byte[11];
                liveBytes[0] = 0xFB;
                liveBytes[1] = 0xFB;
                liveBytes[2] = 0x38;
                Array.Fill(liveBytes, (byte)0x01, 3, 7);
                liveBytes[^1] = 0xFF;
                OtmrLiveFrame currentProfileFrame = Assert.Single(new OtmrLiveFrameAssembler().Append(liveBytes));
                InvokePrivate(live, "LiveService_FrameReceived", null,
                    new OtmrLiveFrameEventArgs(DateTimeOffset.UtcNow, currentProfileFrame));
                Application.DoEvents();
                Assert.Equal(7, grid.Rows.Count);
                Assert.All(grid.Rows.Cast<DataGridViewRow>(), row =>
                    Assert.Equal("ACTIVE", Convert.ToString(row.Cells["decodedStateColumn"].Value)));

                TabControl tabs = Find<TabControl>(form, "tabs");
                tabs.SelectedTab = tabs.TabPages.Cast<TabPage>().Single(page => page.Text == "OTMR Live");
                Application.DoEvents();
                tabs.SelectedTab = tabs.TabPages.Cast<TabPage>().Single(page => page.Text == "RCM LIVE");
                Application.DoEvents();
                Assert.Equal(Path.GetFullPath(pathB), GetPrivateField<string>(bench, "_rcmProfilePath"));
                Assert.Contains("profile-B.json", profileLabel.Text, StringComparison.Ordinal);
                Assert.Equal(7, grid.Rows.Count);

                InvokeLoadCcf(form, FindFromRoot("TestData", "CLASS171_GUI_TEST.ccf"));
                Application.DoEvents();
                Assert.Equal(Path.GetFullPath(pathB), GetPrivateField<string>(bench, "_rcmProfilePath"));
                Assert.Equal(7, grid.Rows.Count);
                Assert.Contains("profile-B.json", profileLabel.Text, StringComparison.Ordinal);

                InvokePrivateAsync(bench, "LoadRcmProfileAsync", pathC, CancellationToken.None);
                Application.DoEvents();
                Assert.Empty(grid.Rows.Cast<DataGridViewRow>());
                Assert.Contains("profile-C.json", profileLabel.Text, StringComparison.Ordinal);
                Assert.Equal("Verified mappings: 0", countLabel.Text);
                Assert.Equal(
                    "Observed this session: 0 | Verified mappings available: 0 | Latest frame decoded: 0",
                    Find<Label>(rcm, "decodedSignalsStatusLabel").Text);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        });
    }

    [Fact]
    public void ServerSyncHasDedicatedSpaciousTabAndOpeningItPerformsNoExplicitNetworkAction()
    {
        RunInStaThread(() =>
        {
            using var form = new MainForm
            {
                StartPosition = FormStartPosition.Manual,
                Location = Point.Empty,
                Size = new Size(1200, 760)
            };
            form.Show();
            TabControl tabs = Find<TabControl>(form, "tabs");
            TabPage syncTab = tabs.TabPages.Cast<TabPage>().Single(page => page.Text == "SERVER SYNC");
            tabs.SelectedTab = syncTab;
            Application.DoEvents();

            OtmrServerSyncControl sync = Find<OtmrServerSyncControl>(syncTab, "otmrServerSyncControl");
            GroupBox connection = Find<GroupBox>(sync, "serverConnectionGroupBox");
            GroupBox status = Find<GroupBox>(sync, "uploadStatusGroupBox");
            TextBox serverUrl = Find<TextBox>(sync, "syncServerUrlTextBox");
            TextBox token = Find<TextBox>(sync, "syncApiTokenTextBox");
            CheckBox enabled = Find<CheckBox>(sync, "syncEnabledCheckBox");
            CheckBox testHttp = Find<CheckBox>(sync, "syncAllowHttpTestServerCheckBox");
            Button save = Find<Button>(sync, "saveSyncSettingsButton");
            Button testServer = Find<Button>(sync, "testServerButton");
            Button upload = Find<Button>(sync, "syncPendingRecordingsButton");

            Assert.True(connection.Visible && connection.Height >= 180);
            Assert.True(status.Visible && status.Height >= 220);
            Assert.True(serverUrl.Visible && serverUrl.Width >= 500);
            Assert.True(token.Visible && token.UseSystemPasswordChar && token.Text.Length == 0);
            Assert.True(enabled.Visible && testHttp.Visible && save.Visible);
            Assert.True(testServer.Visible && testServer.Height > 0);
            Assert.True(upload.Visible && upload.Height > 0);
            Assert.True(Find<Label>(sync, "syncPendingCountLabel").Visible);
            Assert.True(Find<Label>(sync, "syncCurrentStateLabel").Visible);
            Assert.True(Find<Label>(sync, "syncLastAttemptLabel").Visible);
            Assert.True(Find<Label>(sync, "syncLastResultLabel").Visible);
            Assert.True(Find<Label>(sync, "syncLastErrorLabel").Visible);
            Assert.NotNull(GetPrivateField<OtmrManualSyncService>(sync, "_manualSyncService"));
            Assert.False(GetPrivateValue<bool>(sync, "_syncBusy"));
            Assert.StartsWith("Last attempt:", Find<Label>(sync, "syncLastAttemptLabel").Text,
                StringComparison.Ordinal);
        });
    }

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
                    "OTMR I/O Bench",
                    "SERVER SYNC",
                    "RCM LIVE"
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
                         { "Refresh Ports", "38400", "8", "None", "1", "Connect", "Start OTMR Live", "Stop Live", "Stop + Restore", "Disconnect", "Clear", "Save Capture", "Copy Hex" })
                    AssertVisibleText(tabs.SelectedTab!, text);
                Assert.Equal("DISCONNECTED", Find<Label>(form, "liveStateLabel").Text);
                Assert.False(Find<Button>(form, "startLiveButton").Enabled);
                OtmrLiveControl liveControl = Find<OtmrLiveControl>(form, "otmrLiveControl");
                OtmrLiveService liveService = GetPrivateField<OtmrLiveService>(liveControl, "_liveService");
                InvokePrivate(liveService, "SetState", OtmrLiveState.LiveReady);
                Application.DoEvents();
                Assert.Equal("OTMR LIVE READY — waiting for input events", Find<Label>(form, "liveStateLabel").Text);
                Assert.DoesNotContain("WAITING FOR FB FB", Find<Label>(form, "liveStateLabel").Text,
                    StringComparison.Ordinal);
                Assert.True(Find<Button>(form, "stopLiveButton").Enabled);

                tabs.SelectedIndex = 5;
                Application.DoEvents();
                foreach (string text in new[]
                {
                    "RCM: Class 171", "Import Pin Map...", "Refresh CCF", "New RCM Profile From CCF",
                    "Open RCM Profile", "Save RCM Profile", "Save RCM Profile As", "Add Connector",
                    "Rename Connector", "Edit Pin Sequence", "Delete Connector", "Add Input / Pin", "Edit Selected Input",
                    "Assign Next Pin",
                    "Delete Input / Pin", "START INPUT TEST",
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
            Assert.False(Find<Button>(form, "startInputTestButton").Enabled);

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
            Assert.False(Find<Button>(form, "startInputTestButton").Enabled);
            InvokePrivate(bench, "SetOtmrLiveState", OtmrLiveState.ConnectedIdle);
            Application.DoEvents();
            Assert.False(Find<Button>(form, "startInputTestButton").Enabled);
            InvokePrivate(bench, "SetOtmrLiveState", OtmrLiveState.LiveReady);
            Application.DoEvents();
            Assert.True(Find<Button>(form, "startInputTestButton").Enabled);
            InvokePrivate(bench, "SetOtmrLiveState", OtmrLiveState.LiveActive);
            Application.DoEvents();
            Assert.True(Find<Button>(form, "startInputTestButton").Enabled);
            Assert.False(Find<Button>(form, "compareStatesButton").Enabled);

            DataGridViewRow returnRow = grid.Rows.Cast<DataGridViewRow>()
                .Single(row => Convert.ToString(row.Cells["pinColumn"].Value) == "L");
            grid.CurrentCell = returnRow.Cells[0];
            returnRow.Selected = true;
            Application.DoEvents();
            Assert.Contains("J1-L", selected.Text, StringComparison.Ordinal);
            Assert.False(Find<Button>(form, "startInputTestButton").Enabled);
            Assert.Equal("NOT TESTABLE", Convert.ToString(returnRow.Cells["voltageRemovedColumn"].Value));
            Assert.Contains("NOT TESTABLE", Find<TextBox>(form, "evidenceTextBox").Text, StringComparison.Ordinal);
            Assert.Equal(sourceBefore, File.ReadAllBytes(ccfPath));
        });
    }

    [Fact]
    public void GuidedInputTestAcceptsWaitingStateFramesAndAutomaticallyProgressesAndCompares()
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
            Find<Button>(form, "createRcmProfileButton").PerformClick();
            Application.DoEvents();

            OtmrBenchControl bench = Find<OtmrBenchControl>(form, "otmrBenchControl");
            RcmProfile profile = GetPrivateField<RcmProfile>(bench, "_rcmProfile");
            RcmPinProfile pin = profile.Pins.Single(candidate => candidate.CcfReference?.RecordA == 0);
            RcmInputEdit edit = RcmInputEdit.From(pin);
            edit.Connector = "J1";
            edit.Pin = "A";
            edit.Testable = true;
            RcmProfileEditor.UpdateInput(profile, pin.Id, edit);
            InvokePrivate(bench, "PopulateConnectors");
            Find<ComboBox>(form, "connectorComboBox").SelectedItem = "J1";
            InvokePrivate(bench, "RenderTable", pin.Id);
            InvokePrivate(bench, "SetOtmrLiveState", OtmrLiveState.LiveReady);
            Application.DoEvents();

            Button start = Find<Button>(form, "startInputTestButton");
            Button cancel = Find<Button>(form, "cancelInputTestButton");
            Assert.True(start.Enabled);
            start.PerformClick();
            Application.DoEvents();

            var coordinator = GetPrivateField<RcmInputTestCoordinator>(bench, "_inputTestCoordinator");
            var armTimer = GetPrivateField<System.Windows.Forms.Timer>(bench, "armTimeoutTimer");
            var captureTimer = GetPrivateField<System.Windows.Forms.Timer>(bench, "captureWindowTimer");
            Assert.Equal(RcmInputTestState.WaitingFor24VApplied, coordinator.State);
            Assert.True(armTimer.Enabled);
            Assert.False(captureTimer.Enabled);
            Assert.True(cancel.Visible);
            Assert.False(start.Visible);
            Assert.Empty(pin.VoltageApplied24V.CompleteRawFrames);
            Assert.Empty(pin.VoltageRemoved.CompleteRawFrames);

            OtmrLiveFrame applied = Assert.Single(new OtmrLiveFrameAssembler().Append(
                new byte[] { 0xFB, 0xFB, 0x38, 0x4A, 0xFF }));
            bench.ReportRawLiveFrame(DateTimeOffset.UtcNow, applied);
            Application.DoEvents();

            Assert.Equal(RcmInputTestState.Capturing24VApplied, coordinator.State);
            Assert.False(armTimer.Enabled);
            Assert.True(captureTimer.Enabled);
            Assert.Single(pin.VoltageApplied24V.CompleteRawFrames);
            Assert.Empty(pin.VoltageRemoved.CompleteRawFrames);
            Assert.Contains("+24 V DETECTED", Find<Label>(form, "voltageAppliedInstructionLabel").Text,
                StringComparison.Ordinal);

            InvokePrivate(bench, "CaptureWindowTimer_Tick", null, EventArgs.Empty);
            Application.DoEvents();
            Assert.Equal(RcmInputTestState.WaitingForVoltageRemoved, coordinator.State);
            Assert.True(pin.VoltageApplied24V.Tested);
            Assert.False(pin.VoltageRemoved.Tested);
            Assert.True(armTimer.Enabled);
            Assert.False(captureTimer.Enabled);
            Assert.Contains("NOW REMOVE +24 V", Find<Label>(form, "voltageRemovedInstructionLabel").Text,
                StringComparison.Ordinal);

            OtmrLiveFrame removed = Assert.Single(new OtmrLiveFrameAssembler().Append(
                new byte[] { 0xFB, 0xFB, 0x38, 0x19, 0xFF }));
            bench.ReportRawLiveFrame(DateTimeOffset.UtcNow, removed);
            Application.DoEvents();
            Assert.Equal(RcmInputTestState.CapturingVoltageRemoved, coordinator.State);
            Assert.Single(pin.VoltageRemoved.CompleteRawFrames);
            Assert.Equal(0x4A, pin.VoltageApplied24V.CompleteRawFrames.Single().RawFrameBytes[3]);
            Assert.Equal(0x19, pin.VoltageRemoved.CompleteRawFrames.Single().RawFrameBytes[3]);

            InvokePrivate(bench, "CaptureWindowTimer_Tick", null, EventArgs.Empty);
            Application.DoEvents();
            Assert.Equal(RcmInputTestState.Complete, coordinator.State);
            Assert.True(pin.VoltageRemoved.Tested);
            Assert.NotNull(pin.Comparison.ComparedAt);
            Assert.Equal(RcmResultStates.BothStatesCaptured, pin.RcmResult);
            Assert.Contains("TEST COMPLETE", Find<Label>(bench, "statusLabel").Text, StringComparison.Ordinal);
            Assert.Contains("CANDIDATE", Find<Label>(bench, "statusLabel").Text, StringComparison.Ordinal);
            Assert.Equal("REPEAT INPUT TEST", start.Text);
            Assert.False(Find<Button>(bench, "verifyMappingButton").Visible);

            for (int run = 2; run <= 3; run++)
            {
                DateTimeOffset runStart = DateTimeOffset.UtcNow.AddMinutes(run);
                var repeated = new RcmInputTestCoordinator(new RcmCaptureWindowCoordinator());
                repeated.Start(pin, runStart);
                repeated.AddFrame(runStart.AddSeconds(1), applied);
                repeated.CompleteCapture(runStart.AddSeconds(3));
                repeated.AddFrame(runStart.AddSeconds(4), removed);
                repeated.CompleteCapture(runStart.AddSeconds(6), profile.RequiredVerificationRuns);
            }
            InvokePrivate(bench, "RenderTable", pin.Id);
            Application.DoEvents();
            Button verify = Find<Button>(bench, "verifyMappingButton");
            Assert.True(verify.Visible);
            Assert.True(verify.Enabled);
            verify.PerformClick();
            Application.DoEvents();
            Assert.True(pin.Comparison.DecoderVerified);
            Assert.Equal(RcmVerificationStates.Verified, pin.DecoderVerification.Status);
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
            Assert.False(Find<Button>(form, "startInputTestButton").Enabled);
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

    [Fact]
    public void GenuineLiveFrameUpdatesExplicitlyVerifiedSignalInRcmLiveUiThroughExistingLiveStream()
    {
        RunInStaThread(() =>
        {
            string profilePath = Path.Combine(Path.GetTempPath(), $"current-live-{Guid.NewGuid():N}.json");
            RcmPinProfile pin = RcmVerifiedLiveDecoderTests.VerifiedPin(
                "A", position: 3, bit: 0, removed: 0x00, applied: 0x01);
            pin.Testable = false;
            pin.RcmResult = RcmResultStates.NotTestable;
            MakePersistableVerified(pin);
            RcmProfile profile = RcmVerifiedLiveDecoderTests.Profile(pin);
            try
            {
                RcmProfileJson.SaveAsync(profilePath, profile, DateTimeOffset.UtcNow).GetAwaiter().GetResult();
                using var form = new MainForm();
                form.Show();
                TabControl tabs = Find<TabControl>(form, "tabs");
                tabs.SelectedIndex = 4;
                Application.DoEvents();

                OtmrLiveControl live = Find<OtmrLiveControl>(form, "otmrLiveControl");
                OtmrBenchControl bench = Find<OtmrBenchControl>(form, "otmrBenchControl");
                OtmrRcmLiveControl rcmLive = Find<OtmrRcmLiveControl>(form, "otmrRcmLiveControl");
                InvokePrivateAsync(bench, "LoadRcmProfileAsync", profilePath, CancellationToken.None);
                Application.DoEvents();

                DataGridView grid = Find<DataGridView>(rcmLive, "decodedSignalsGrid");
                Assert.Empty(grid.Rows.Cast<DataGridViewRow>());
                Assert.Contains("DISCONNECTED", Find<Label>(rcmLive, "liveStateStatusLabel").Text,
                    StringComparison.Ordinal);
                Assert.Contains("Verified mappings: 1", Find<Label>(rcmLive, "verifiedMappingsStatusLabel").Text,
                    StringComparison.Ordinal);
                rcmLive.SetSourceConnectionId(Guid.NewGuid().ToString("D"));

                var assembler = new OtmrLiveFrameAssembler();
                OtmrLiveFrame activeFrame = Assert.Single(assembler.Append(
                    new byte[] { 0xFB, 0xFB, 0x38, 0x01, 0xFF }));
                InvokePrivate(live, "LiveService_FrameReceived", null,
                    new OtmrLiveFrameEventArgs(DateTimeOffset.UtcNow, activeFrame));
                Application.DoEvents();

                tabs.SelectedTab = tabs.TabPages.Cast<TabPage>().Single(page => page.Text == "RCM LIVE");
                Application.DoEvents();
                DataGridViewRow row = Assert.Single(grid.Rows.Cast<DataGridViewRow>());
                Assert.Equal("J1-A", Convert.ToString(row.Cells["decodedPhysicalColumn"].Value));
                Assert.Equal("Throttle 1", Convert.ToString(row.Cells["decodedFunctionColumn"].Value));
                Assert.Equal("Card 0 / Ch 0", Convert.ToString(row.Cells["decodedLogicalColumn"].Value));
                Assert.Equal("ACTIVE", Convert.ToString(row.Cells["decodedStateColumn"].Value));
                Assert.Contains("pos 3 bit 0 = 1", Convert.ToString(row.Cells["decodedRawColumn"].Value),
                    StringComparison.Ordinal);
                Assert.Equal("VERIFIED", Convert.ToString(row.Cells["decodedVerificationColumn"].Value));

                OtmrLiveFrame inactiveFrame = Assert.Single(assembler.Append(
                    new byte[] { 0xFB, 0xFB, 0x38, 0x00, 0xFF }));
                InvokePrivate(live, "LiveService_FrameReceived", null,
                    new OtmrLiveFrameEventArgs(DateTimeOffset.UtcNow, inactiveFrame));
                Application.DoEvents();

                row = Assert.Single(grid.Rows.Cast<DataGridViewRow>());
                Assert.Equal("INACTIVE", Convert.ToString(row.Cells["decodedStateColumn"].Value));
                Assert.Contains("pos 3 bit 0 = 0", Convert.ToString(row.Cells["decodedRawColumn"].Value),
                    StringComparison.Ordinal);
            }
            finally
            {
                if (File.Exists(profilePath)) File.Delete(profilePath);
            }
        });
    }

    private static void InvokeLoadCcf(MainForm form, string path) =>
        typeof(MainForm).GetMethod("LoadCcf", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, new object[] { path });

    private static RcmProfile ProfileWithVerifiedMappings(string prefix, int count)
    {
        RcmPinProfile[] pins = Enumerable.Range(0, count)
            .Select(index =>
            {
                RcmPinProfile pin = RcmVerifiedLiveDecoderTests.VerifiedPin(
                    $"{prefix}{index}", position: 3 + index, bit: 0, removed: 0x00, applied: 0x01);
                pin.Function = $"{prefix} Function {index}";
                pin.CcfReference!.LogicalChannel = index;
                pin.DecoderVerification.Function = pin.Function;
                pin.DecoderVerification.ExpectedCcf.LogicalChannel = index;
                MakePersistableVerified(pin);
                return pin;
            })
            .ToArray();
        RcmProfile profile = RcmVerifiedLiveDecoderTests.Profile(pins);
        profile.SourceCcfFilename = $"{prefix}-source.ccf";
        return profile;
    }

    private static void MakePersistableVerified(RcmPinProfile pin)
    {
        RcmObservedTransition observed = pin.DecoderVerification.ObservedMapping!;
        pin.VerificationRuns.Clear();
        for (int runNumber = 1; runNumber <= 3; runNumber++)
        {
            var run = new RcmPhysicalVerificationRun
            {
                RunNumber = runNumber,
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(runNumber),
                CompletedAt = DateTimeOffset.UtcNow.AddMinutes(runNumber).AddSeconds(6),
                Connector = pin.Connector,
                Pin = pin.Pin,
                Function = pin.Function,
                ExpectedCcf = new RcmExpectedMappingSnapshot
                {
                    LogicalCard = pin.CcfReference!.LogicalCard,
                    LogicalChannel = pin.CcfReference.LogicalChannel,
                    RecordA = pin.CcfReference.RecordA,
                    RecordB = pin.CcfReference.RecordB
                },
                CandidateTransitions = new List<RcmObservedTransition>
                {
                    new()
                    {
                        RawPosition = observed.RawPosition,
                        Bit = observed.Bit,
                        RemovedValue = observed.RemovedValue,
                        AppliedValue = observed.AppliedValue,
                        TransitionPolarity = observed.TransitionPolarity
                    }
                }
            };
            pin.VerificationRuns.Add(run);
        }
        pin.DecoderVerification.RequiredRunCount = 3;
        pin.DecoderVerification.SuccessfulRepetitionCount = 3;
        pin.DecoderVerification.QualifyingRunIds = pin.VerificationRuns.Select(run => run.RunId).ToList();
        pin.Comparison.DecoderVerified = true;
    }

    private static void InvokePrivateAsync(object target, string name, params object?[] arguments)
    {
        MethodInfo method = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate => candidate.Name == name && candidate.GetParameters().Length == arguments.Length);
        Task task = Assert.IsAssignableFrom<Task>(method.Invoke(target, arguments));
        task.GetAwaiter().GetResult();
    }

    private static void InvokePrivate(object target, string name, params object?[] arguments)
    {
        MethodInfo method = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate => candidate.Name == name && candidate.GetParameters().Length == arguments.Length);
        method.Invoke(target, arguments);
    }

    private static T GetPrivateField<T>(object target, string name) where T : class =>
        (T)(target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)
            ?? throw new Xunit.Sdk.XunitException($"Field '{name}' is null."));

    private static T GetPrivateValue<T>(object target, string name) where T : struct =>
        Assert.IsType<T>(target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target));

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
