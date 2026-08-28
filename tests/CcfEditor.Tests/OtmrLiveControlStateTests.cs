using System.Reflection;
using CcfEditor.Otmr.Live;
using CcfEditor.WinForms;

namespace CcfEditor.Tests;

public sealed class OtmrLiveControlStateTests
{
    [Fact]
    public void RuntimeConnectionStatesDriveOperationalButtons()
    {
        RunInStaThread(() =>
        {
            using var live = new OtmrLiveControl();
            ComboBox ports = Find<ComboBox>(live, "portComboBox");
            Button refresh = Find<Button>(live, "refreshPortsButton");
            Button connect = Find<Button>(live, "connectButton");
            Button start = Find<Button>(live, "startLiveButton");
            Button stop = Find<Button>(live, "stopLiveButton");
            Button restore = Find<Button>(live, "stopRestoreButton");
            Button disconnect = Find<Button>(live, "disconnectButton");

            ports.Items.Clear();
            InvokePrivate(live, "UpdateStateUi");
            ports.Items.Add("COM1");
            ports.SelectedIndex = 0;
            Application.DoEvents();

            AssertButtons(
                refresh, connect, start, stop, restore, disconnect,
                refreshEnabled: true,
                connectEnabled: true,
                startEnabled: false,
                stopEnabled: false,
                restoreEnabled: false,
                disconnectEnabled: false);

            SetLiveState(live, OtmrLiveState.ConnectedIdle);
            AssertButtons(
                refresh, connect, start, stop, restore, disconnect,
                refreshEnabled: false,
                connectEnabled: false,
                startEnabled: true,
                stopEnabled: false,
                restoreEnabled: false,
                disconnectEnabled: true);

            SetLiveState(live, OtmrLiveState.LiveActive);
            AssertButtons(
                refresh, connect, start, stop, restore, disconnect,
                refreshEnabled: false,
                connectEnabled: false,
                startEnabled: false,
                stopEnabled: true,
                restoreEnabled: true,
                disconnectEnabled: true);

            SetLiveState(live, OtmrLiveState.Disconnected);
            AssertButtons(
                refresh, connect, start, stop, restore, disconnect,
                refreshEnabled: true,
                connectEnabled: true,
                startEnabled: false,
                stopEnabled: false,
                restoreEnabled: false,
                disconnectEnabled: false);
        });
    }

    [Fact]
    public void ChangingOperationalTabsDoesNotDisableConnect()
    {
        RunInStaThread(() =>
        {
            using var form = new MainForm();
            form.Show();
            Application.DoEvents();

            TabControl tabs = Find<TabControl>(form, "tabs");
            OtmrLiveControl live = Find<OtmrLiveControl>(form, "otmrLiveControl");
            ComboBox ports = Find<ComboBox>(live, "portComboBox");
            Button connect = Find<Button>(live, "connectButton");

            ports.Items.Clear();
            InvokePrivate(live, "UpdateStateUi");
            ports.Items.Add("COM1");
            ports.SelectedIndex = 0;
            Application.DoEvents();

            foreach (string tabText in new[] { "OTMR Live", "RCM LIVE", "SERVER SYNC", "OTMR Live" })
            {
                tabs.SelectedTab = tabs.TabPages.Cast<TabPage>()
                    .Single(page => page.Text == tabText);
                Application.DoEvents();
                Assert.True(connect.Enabled, $"Connect became disabled on the '{tabText}' tab.");
            }
        });
    }

    private static void SetLiveState(OtmrLiveControl live, OtmrLiveState state)
    {
        OtmrLiveService service = GetPrivateField<OtmrLiveService>(live, "_liveService");
        InvokePrivate(service, "SetState", state);
        Application.DoEvents();
    }

    private static void AssertButtons(
        Button refresh,
        Button connect,
        Button start,
        Button stop,
        Button restore,
        Button disconnect,
        bool refreshEnabled,
        bool connectEnabled,
        bool startEnabled,
        bool stopEnabled,
        bool restoreEnabled,
        bool disconnectEnabled)
    {
        Assert.Equal(refreshEnabled, refresh.Enabled);
        Assert.Equal(connectEnabled, connect.Enabled);
        Assert.Equal(startEnabled, start.Enabled);
        Assert.Equal(stopEnabled, stop.Enabled);
        Assert.Equal(restoreEnabled, restore.Enabled);
        Assert.Equal(disconnectEnabled, disconnect.Enabled);
    }

    private static T Find<T>(Control root, string name) where T : Control
    {
        T? match = Descendants(root).OfType<T>().SingleOrDefault(control => control.Name == name);
        return match ?? throw new Xunit.Sdk.XunitException($"Control '{name}' was not found.");
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

    private static T GetPrivateField<T>(object instance, string name) where T : class =>
        Assert.IsType<T>(instance.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(instance));

    private static void InvokePrivate(object instance, string methodName, params object?[]? arguments) =>
        instance.GetType()
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(instance, arguments);

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
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The UI state test timed out.");
        if (failure is not null)
            throw new AggregateException(failure);
    }
}
