using CcfEditor.Otmr.Storage;

namespace CcfEditor.WinForms;

public partial class MainForm
{
    private SqliteOtmrRecordingStore? _recordingStore;

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_recordingStore is not null)
            return;

        _recordingStore = new SqliteOtmrRecordingStore(OtmrDatabasePaths.DefaultDatabasePath);
        otmrLiveControl.SetRecordingStore(_recordingStore);
        otmrBenchControl.SetRecordingStore(_recordingStore);
    }

    internal OtmrRecordingSessionContext CreateRecordingSessionContext(string comPort)
    {
        RcmProfileRecordingContext rcm = otmrBenchControl.GetRecordingProfileContext();
        return new OtmrRecordingSessionContext
        {
            SoftwareVersion = Application.ProductVersion,
            ComPort = comPort,
            SerialSettings = "38400/8/N/1; RTS=LOW; DTR=LOW on Connect, HIGH after controlled live-start reopen",
            VehicleIdentifier = rcm.VehicleIdentifier,
            VehicleType = rcm.VehicleType,
            CcfFilename = _document?.SourcePath is null ? null : Path.GetFileName(_document.SourcePath),
            CcfSha256 = _document?.WorkingSha256,
            RcmProfileFilename = rcm.Filename,
            RcmProfileSha256 = rcm.Sha256,
            RcmProfileJsonSnapshot = rcm.JsonSnapshot
        };
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
        }
        _recordingStore = null;
        base.OnFormClosed(e);
    }
}
