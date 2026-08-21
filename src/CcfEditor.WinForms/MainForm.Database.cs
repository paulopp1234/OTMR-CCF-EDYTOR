using System.Text;
using CcfEditor.Otmr.Storage;

namespace CcfEditor.WinForms;

public partial class MainForm
{
    private SqliteOtmrRecordingStore? _recordingStore;

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);

        // The bench OnLoad event can run while the form is still receiving its
        // final dimensions. Correct its intended initial split once now that the
        // host form is actually shown.
        otmrBenchControl.ApplyFinalInitialLayout();

        if (_recordingStore is not null)
            return;

        _recordingStore = new SqliteOtmrRecordingStore(OtmrDatabasePaths.DefaultDatabasePath);
        otmrLiveControl.SetRecordingStore(_recordingStore);
        otmrBenchControl.SetRecordingStore(_recordingStore);

        try
        {
            // Initializes/version-checks the schema and recovers any session that
            // was left in RECORDING by a previous crash or power interruption.
            await _recordingStore.InitializeAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"The local OTMR database could not be initialized.\r\n\r\n{ex.Message}\r\n\r\nDatabase: {OtmrDatabasePaths.DefaultDatabasePath}",
                "OTMR database initialization failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
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
