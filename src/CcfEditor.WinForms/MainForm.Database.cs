using System.Text;
using CcfEditor.Otmr.Storage;

namespace CcfEditor.WinForms;

public partial class MainForm
{
    private SqliteOtmrRecordingStore? _recordingStore;
    private bool _databaseTabLayoutHookInstalled;

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        if (!_databaseTabLayoutHookInstalled)
        {
            tabs.SelectedIndexChanged += DatabaseTabs_SelectedIndexChanged;
            _databaseTabLayoutHookInstalled = true;
        }
        DatabaseTabs_SelectedIndexChanged(tabs, EventArgs.Empty);

        if (_recordingStore is not null)
            return;

        SqliteOtmrRecordingStore? store = null;
        try
        {
            store = new SqliteOtmrRecordingStore(OtmrDatabasePaths.DefaultDatabasePath);

            // Finish initialization/version checking/recovery before OnShown returns.
            // This prevents an async initialization continuation from racing form
            // disposal and guarantees controls only receive a ready recording store.
            store.InitializeAsync().GetAwaiter().GetResult();

            _recordingStore = store;
            otmrLiveControl.SetRecordingStore(store);
            otmrBenchControl.SetRecordingStore(store);
            store = null;
        }
        catch (Exception ex)
        {
            try
            {
                store?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch
            {
                // Initialization already failed; preserve the original error below.
            }

            MessageBox.Show(
                this,
                $"The local OTMR database could not be initialized.\r\n\r\n{ex.Message}\r\n\r\nDatabase: {OtmrDatabasePaths.DefaultDatabasePath}",
                "OTMR database initialization failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void DatabaseTabs_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (ReferenceEquals(tabs.SelectedTab, otmrBenchTab))
            otmrBenchControl.ScheduleFinalInitialLayout();
    }

    internal OtmrRecordingSessionContext CreateRecordingSessionContext(string comPort)
    {
        RcmProfileRecordingContext rcm = otmrBenchControl.GetRecordingProfileContext();
        return new OtmrRecordingSessionContext
        {
            SoftwareVersion = Application.ProductVersion,
            ComPort = comPort,
            SerialSettings = "38400/8/N/1; RTS=LOW; DTR=LOW on Connect, HIGH after controlled live-start reopen",
            VehicleIdentifier = rcm.VehicleIdentifier ?? ReadVehicleIdentifierFromCcf(),
            VehicleType = rcm.VehicleType,
            CcfFilename = _document?.SourcePath is null ? null : Path.GetFileName(_document.SourcePath),
            CcfSha256 = _document?.WorkingSha256,
            RcmProfileFilename = rcm.Filename,
            RcmProfileSha256 = rcm.Sha256,
            RcmProfileJsonSnapshot = rcm.JsonSnapshot
        };
    }

    private string? ReadVehicleIdentifierFromCcf()
    {
        if (_document is null)
            return null;

        byte[] raw = _document.Header.GetRawBytes(0x01C0, 10);
        int terminator = Array.FindIndex(raw, value => value is 0x00 or 0xFF);
        int length = terminator >= 0 ? terminator : raw.Length;
        string value = Encoding.ASCII.GetString(raw, 0, length).Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (_databaseTabLayoutHookInstalled)
        {
            tabs.SelectedIndexChanged -= DatabaseTabs_SelectedIndexChanged;
            _databaseTabLayoutHookInstalled = false;
        }

        try
        {
            _recordingStore?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch
        {
            // The form must still close if local storage cleanup reports an error.
            // Any session left as RECORDING is recovered at the next startup.
        }
        _recordingStore = null;
        base.OnFormClosed(e);
    }
}
