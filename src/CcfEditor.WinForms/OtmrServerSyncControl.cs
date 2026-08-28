using CcfEditor.Otmr.Storage;
using CcfEditor.Otmr.Sync;

namespace CcfEditor.WinForms;

/// <summary>
/// Deliberate, operator-driven server synchronization UI. Constructing or
/// displaying this control performs local settings/status reads only; network
/// access is restricted to the two explicit operator buttons.
/// </summary>
public partial class OtmrServerSyncControl : UserControl
{
    private IOtmrRecordingStore? _recordingStore;
    private OtmrSyncUserSettingsStore? _syncSettingsStore;
    private OtmrSyncUserSettings _syncSettings = new();
    private OtmrManualSyncService? _manualSyncService;
    private HttpClient? _syncHttpClient;
    private bool _syncBusy;
    private bool _closing;

    public OtmrServerSyncControl()
    {
        InitializeComponent();
        InitializeManualSyncUi();
        Disposed += OtmrServerSyncControl_Disposed;
    }

    internal void SetRecordingStore(IOtmrRecordingStore? recordingStore)
    {
        if (ReferenceEquals(_recordingStore, recordingStore))
            return;

        if (_recordingStore is not null)
            _recordingStore.StatusChanged -= RecordingStore_StatusChanged;

        _recordingStore = recordingStore;
        if (_recordingStore is not null)
            _recordingStore.StatusChanged += RecordingStore_StatusChanged;

        ConfigureManualSyncService();
    }

    private void InitializeManualSyncUi()
    {
        _syncSettingsStore = new OtmrSyncUserSettingsStore();
        try
        {
            _syncSettings = _syncSettingsStore.Load();
            ApplySavedSyncSettingsToUi();
        }
        catch (Exception ex)
        {
            _syncSettings = new OtmrSyncUserSettings();
            ApplySavedSyncSettingsToUi();
            syncLastErrorLabel.Text = $"Last error: User settings could not be loaded: {ex.Message}";
        }
        UpdateManualSyncButtons();
    }

    private void ConfigureManualSyncService()
    {
        _syncHttpClient?.Dispose();
        _syncHttpClient = null;
        _manualSyncService = null;
        if (_recordingStore is null)
        {
            UpdateManualSyncButtons();
            return;
        }

        _syncHttpClient = new HttpClient();
        _manualSyncService = new OtmrManualSyncService(_recordingStore, _syncHttpClient);
        UpdateManualSyncButtons();
        _ = RefreshManualSyncStatusAsync(); // Local SQLite status only; never contacts the server.
    }

    private void ApplySavedSyncSettingsToUi()
    {
        syncServerUrlTextBox.Text = _syncSettings.ServerUrl;
        syncEnabledCheckBox.Checked = _syncSettings.SyncEnabled;
        syncAllowHttpTestServerCheckBox.Checked = _syncSettings.AllowInsecureKnownTestServer;
        syncApiTokenTextBox.Clear();
        syncApiTokenTextBox.PlaceholderText = _syncSettings.HasApiToken
            ? "Token saved securely - enter a new token to replace it"
            : "Enter token to save";
    }

    private OtmrSyncUserSettings SaveSyncSettingsFromUi()
    {
        string replacementToken = syncApiTokenTextBox.Text;
        var proposed = new OtmrSyncUserSettings
        {
            ServerUrl = syncServerUrlTextBox.Text.Trim(),
            ApiToken = string.IsNullOrEmpty(replacementToken) ? _syncSettings.ApiToken : replacementToken,
            SyncEnabled = syncEnabledCheckBox.Checked,
            AllowInsecureKnownTestServer = syncAllowHttpTestServerCheckBox.Checked
        };

        proposed.ToManualConfiguration().ValidateServerUri();
        _syncSettingsStore!.Save(proposed);
        _syncSettings = proposed;
        syncApiTokenTextBox.Clear();
        syncApiTokenTextBox.PlaceholderText = proposed.HasApiToken
            ? "Token saved securely - enter a new token to replace it"
            : "Enter token to save";
        UpdateManualSyncButtons();
        return proposed;
    }

    private void SaveSyncSettingsButton_Click(object? sender, EventArgs e)
    {
        try
        {
            SaveSyncSettingsFromUi();
            syncLastResultLabel.Text = "Last result: Settings saved in the current Windows user profile";
            syncLastErrorLabel.Text = "Last error: -";
        }
        catch (Exception ex)
        {
            syncLastErrorLabel.Text = $"Last error: {ex.Message}";
        }
    }

    private async void TestServerButton_Click(object? sender, EventArgs e)
    {
        if (_manualSyncService is null)
            return;

        SetManualSyncBusy(true);
        try
        {
            OtmrSyncUserSettings settings = SaveSyncSettingsFromUi();
            OtmrServerConnectivityResult result = await _manualSyncService.TestServerAsync(settings.ToManualConfiguration());
            syncLastResultLabel.Text = $"Last result: {result.Message}";
            syncLastErrorLabel.Text = result.IsAvailable ? "Last error: -" : $"Last error: {result.Message}";
            if (result.IsAvailable)
                MessageBox.Show(this, "SERVER OK", "OTMR server test", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            syncLastResultLabel.Text = "Last result: SERVER TEST FAILED";
            syncLastErrorLabel.Text = $"Last error: {ex.Message}";
        }
        finally
        {
            SetManualSyncBusy(false);
        }
    }

    private async void SyncPendingRecordingsButton_Click(object? sender, EventArgs e)
    {
        if (_manualSyncService is null)
            return;

        SetManualSyncBusy(true);
        try
        {
            OtmrSyncUserSettings settings = SaveSyncSettingsFromUi();
            IReadOnlyList<OtmrSyncItemResult> results = await _manualSyncService
                .SynchronizePendingAsync(settings.ToManualConfiguration());
            await RefreshManualSyncStatusAsync();
            int uploaded = results.Count(x => x.Uploaded);
            int failed = results.Count - uploaded;
            syncLastResultLabel.Text = results.Count == 0
                ? "Last result: No pending recordings; local SQLite evidence is unchanged."
                : $"Last result: {uploaded} uploaded, {failed} failed; local SQLite evidence remains intact.";
        }
        catch (Exception ex)
        {
            syncLastResultLabel.Text = "Last result: UPLOAD_FAILED";
            syncLastErrorLabel.Text = $"Last error: {ex.Message}";
            await RefreshManualSyncStatusAsync();
        }
        finally
        {
            SetManualSyncBusy(false);
        }
    }

    private async Task RefreshManualSyncStatusAsync()
    {
        if (_manualSyncService is null || _closing || IsDisposed)
            return;
        try
        {
            OtmrManualSyncStatus status = await _manualSyncService.GetStatusAsync();
            if (_closing || IsDisposed)
                return;
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => ApplyManualSyncStatus(status)));
                return;
            }
            ApplyManualSyncStatus(status);
        }
        catch (ObjectDisposedException)
        {
            // Normal during form shutdown.
        }
        catch (Exception ex)
        {
            if (!_closing && !IsDisposed)
                syncLastErrorLabel.Text = $"Last error: Unable to read local sync status: {ex.Message}";
        }
    }

    private void ApplyManualSyncStatus(OtmrManualSyncStatus status)
    {
        syncPendingCountLabel.Text = $"Pending recordings: {status.PendingUploadCount:N0}";
        syncCurrentStateLabel.Text = $"Current: {status.CurrentSessionSyncState}";
        syncLastAttemptLabel.Text = status.LastSyncAttemptUtc is null
            ? "Last attempt: Never"
            : $"Last attempt: {status.LastSyncAttemptUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
        syncLastResultLabel.Text = $"Last result: {status.LastSyncResult}";
        syncLastErrorLabel.Text = string.IsNullOrWhiteSpace(status.LastError)
            ? "Last error: -"
            : $"Last error: {status.LastError}";
    }

    private void RecordingStore_StatusChanged(object? sender, OtmrRecordingStatusChangedEventArgs e)
    {
        if (!_closing && !IsDisposed)
            _ = RefreshManualSyncStatusAsync();
    }

    private void SyncEnabledCheckBox_CheckedChanged(object? sender, EventArgs e) => UpdateManualSyncButtons();

    private void SetManualSyncBusy(bool busy)
    {
        _syncBusy = busy;
        UpdateManualSyncButtons();
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
    }

    private void UpdateManualSyncButtons()
    {
        if (syncPendingRecordingsButton is null)
            return;
        saveSyncSettingsButton.Enabled = !_syncBusy;
        testServerButton.Enabled = !_syncBusy && _manualSyncService is not null;
        syncPendingRecordingsButton.Enabled =
            !_syncBusy && _manualSyncService is not null && syncEnabledCheckBox.Checked;
    }

    private void OtmrServerSyncControl_Disposed(object? sender, EventArgs e)
    {
        _closing = true;
        if (_recordingStore is not null)
            _recordingStore.StatusChanged -= RecordingStore_StatusChanged;
        _manualSyncService = null;
        _syncHttpClient?.Dispose();
        _syncHttpClient = null;
    }
}
