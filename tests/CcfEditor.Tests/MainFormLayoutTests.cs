using System.Reflection;
using CcfEditor.Otmr.Live;
using CcfEditor.WinForms;

namespace CcfEditor.Tests;

public sealed class MainFormLayoutTests
{
    [Fact]
    public void EveryTabRendersItsExpectedControlsAndLoadedCcfData()
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
                Assert.Equal(
                    new[]
                    {
                        "Records - edit known fields in grid",
                        "Header - edit supported Decoded cells",
                        "Hex",
                        "Validation",
                        "OTMR Live - M1",
                        "OTMR I/O Bench"
                    },
                    tabs.TabPages.Cast<TabPage>().Select(page => page.Text));

                foreach (TabPage page in tabs.TabPages)
                {
                    tabs.SelectedTab = page;
                    Application.DoEvents();

                    Control content = Assert.Single(page.Controls.Cast<Control>());
                    Assert.True(content.Visible, $"{page.Text}: root content is not visible.");
                    Assert.True(content.Width >= page.DisplayRectangle.Width * 0.9,
                        $"{page.Text}: root content width {content.Width} does not fill {page.DisplayRectangle.Width}.");
                    Assert.True(content.Height >= page.DisplayRectangle.Height * 0.9,
                        $"{page.Text}: root content height {content.Height} does not fill {page.DisplayRectangle.Height}.");
                }

                Assert.Equal(256, Find<DataGridView>(form, "recordsGrid").Rows.Count);
                Assert.NotEmpty(Find<DataGridView>(form, "headerGrid").Rows.Cast<DataGridViewRow>());
                Assert.NotEmpty(Find<DataGridView>(form, "hexGrid").Rows.Cast<DataGridViewRow>());
                Assert.True(Find<ListView>(form, "validationList").Columns.Count > 0);

                tabs.SelectedIndex = 4;
                Application.DoEvents();
                AssertVisibleText(tabs.SelectedTab!, "Refresh Ports");
                AssertVisibleText(tabs.SelectedTab!, "38400");
                AssertVisibleText(tabs.SelectedTab!, "8");
                AssertVisibleText(tabs.SelectedTab!, "None");
                AssertVisibleText(tabs.SelectedTab!, "1");
                AssertVisibleText(tabs.SelectedTab!, "Connect");
                AssertVisibleText(tabs.SelectedTab!, "Disconnect");
                AssertVisibleText(tabs.SelectedTab!, "Clear");
                AssertVisibleText(tabs.SelectedTab!, "Save Capture");
                AssertVisibleText(tabs.SelectedTab!, "Copy Hex");

                tabs.SelectedIndex = 5;
                Application.DoEvents();
                AssertVisibleText(tabs.SelectedTab!, "Profile: Class 171");
                AssertVisibleText(tabs.SelectedTab!, "Load Pin Map...");
                AssertVisibleText(tabs.SelectedTab!, "Refresh CCF");
                AssertVisibleText(tabs.SelectedTab!, "Arm Selected Pin");
                AssertVisibleText(tabs.SelectedTab!, "Stop Observation");
                AssertVisibleText(tabs.SelectedTab!, "Save Observation Session...");
                AssertVisibleText(tabs.SelectedTab!, "Clear Observations");

                DataGridView benchGrid = Find<DataGridView>(form, "benchGrid");
                Assert.True(benchGrid.Width >= 600,
                    $"The bench pin table is collapsed to {benchGrid.Width}px and cannot display its columns.");
                Assert.NotEmpty(benchGrid.Rows.Cast<DataGridViewRow>());

                ToolStripStatusLabel fileStatus = FindToolStripItem<ToolStripStatusLabel>(form, "fileStatusLabel");
                Assert.Contains("26,600 bytes", fileStatus.Text, StringComparison.Ordinal);
                Assert.Contains("256 records", fileStatus.Text, StringComparison.Ordinal);
            }
            finally
            {
                File.Delete(ccfPath);
            }
        });
    }

    [Fact]
    public void ArmedPin_ImmediatelyShowsWaitingAndRealFramesBecomeRawEvidenceOnly()
    {
        RunInStaThread(() =>
        {
            string ccfPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..", "TestData", "CLASS171_GUI_TEST.ccf"));
            Assert.True(File.Exists(ccfPath), $"GUI verification fixture not found: {ccfPath}");
            byte[] sourceBefore = File.ReadAllBytes(ccfPath);

            using var form = new MainForm();
            form.Show();
            Application.DoEvents();
            InvokeLoadCcf(form, ccfPath);

            TabControl tabs = Find<TabControl>(form, "tabs");
            tabs.SelectedIndex = 5;
            Application.DoEvents();

            DataGridView benchGrid = Find<DataGridView>(form, "benchGrid");
            DataGridViewRow j1a = benchGrid.Rows.Cast<DataGridViewRow>()
                .Single(row => string.Equals(Convert.ToString(row.Cells["pinColumn"].Value), "A", StringComparison.Ordinal));
            benchGrid.CurrentCell = j1a.Cells[0];
            j1a.Selected = true;
            Assert.False(Find<Button>(form, "saveObservationButton").Enabled);
            Find<Button>(form, "armSelectedButton").PerformClick();
            Application.DoEvents();

            TextBox details = Find<TextBox>(form, "detailsTextBox");
            Assert.Contains("ARMED J1-A", details.Text, StringComparison.Ordinal);
            Assert.Contains("Throttle 1", details.Text, StringComparison.Ordinal);
            Assert.Contains("Expected records: 0 ↔ 12", details.Text, StringComparison.Ordinal);
            Assert.Contains("Waiting for live transition", details.Text, StringComparison.Ordinal);
            j1a = benchGrid.Rows.Cast<DataGridViewRow>()
                .Single(row => string.Equals(Convert.ToString(row.Cells[0].Value), "A", StringComparison.Ordinal));
            Assert.Equal("ARMED - WAITING", Convert.ToString(j1a.Cells[12].Value));

            var assembler = new OtmrLiveFrameAssembler();
            OtmrLiveFrame first = Assert.Single(assembler.Append(new byte[] { 0xFB, 0xFB, 0x38, 0x4A, 0xFF }));
            ReportFrame(form, new DateTimeOffset(2026, 8, 21, 8, 50, 17, TimeSpan.Zero), first);
            OtmrLiveFrame second = Assert.Single(assembler.Append(new byte[] { 0xFB, 0xFB, 0x38, 0x4B, 0xFF }));
            ReportFrame(form, new DateTimeOffset(2026, 8, 21, 8, 50, 18, TimeSpan.Zero), second);
            Application.DoEvents();

            j1a = benchGrid.Rows.Cast<DataGridViewRow>()
                .Single(row => string.Equals(Convert.ToString(row.Cells[0].Value), "A", StringComparison.Ordinal));
            Assert.Equal("No decoded record | raw frames: 2", Convert.ToString(j1a.Cells[10].Value));
            Assert.Equal("RAW #2", Convert.ToString(j1a.Cells[11].Value));
            Assert.Equal("ARMED - RAW OBSERVATION", Convert.ToString(j1a.Cells[12].Value));
            Assert.DoesNotContain("LIVE MATCH", Convert.ToString(j1a.Cells[12].Value), StringComparison.Ordinal);
            Assert.Contains("Last raw frame: FB FB 38 4B FF", details.Text, StringComparison.Ordinal);
            Assert.Contains("CANDIDATE RAW DELTA: @03:4A→4B", details.Text, StringComparison.Ordinal);
            Assert.Contains("Raw frames since arming: 2", details.Text, StringComparison.Ordinal);

            Find<Button>(form, "stopObservationButton").PerformClick();
            Application.DoEvents();
            Assert.True(Find<Button>(form, "saveObservationButton").Enabled);
            Assert.True(Find<Button>(form, "armSelectedButton").Enabled);
            j1a = benchGrid.Rows.Cast<DataGridViewRow>()
                .Single(row => string.Equals(Convert.ToString(row.Cells[0].Value), "A", StringComparison.Ordinal));
            Assert.Equal("RAW OBSERVATION STOPPED", Convert.ToString(j1a.Cells[12].Value));
            Assert.Contains(
                "2 complete raw frame(s) retained",
                Find<Label>(tabs.SelectedTab!, "statusLabel").Text,
                StringComparison.Ordinal);
            Assert.Equal(sourceBefore, File.ReadAllBytes(ccfPath));
        });
    }

    private static void InvokeLoadCcf(MainForm form, string path) =>
        typeof(MainForm)
            .GetMethod("LoadCcf", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, new object[] { path });

    private static void ReportFrame(MainForm form, DateTimeOffset timestamp, OtmrLiveFrame frame) =>
        typeof(MainForm)
            .GetMethod("ReportOtmrLiveFrame", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, new object[] { timestamp, frame });

    private static T Find<T>(Control root, string name) where T : Control
    {
        T? match = root.Controls.Find(name, true).OfType<T>().SingleOrDefault();
        return match ?? throw new Xunit.Sdk.XunitException($"Control '{name}' was not found.");
    }

    private static T FindToolStripItem<T>(Control root, string name) where T : ToolStripItem
    {
        foreach (ToolStrip strip in Descendants(root).OfType<ToolStrip>())
        {
            T? match = strip.Items.Find(name, true).OfType<T>().SingleOrDefault();
            if (match is not null)
                return match;
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
            foreach (Control descendant in Descendants(child))
                yield return descendant;
        }
    }

    private static void RunInStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                ApplicationConfiguration.Initialize();
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The UI smoke test timed out.");

        if (failure is not null)
            throw new AggregateException(failure);
    }
}
