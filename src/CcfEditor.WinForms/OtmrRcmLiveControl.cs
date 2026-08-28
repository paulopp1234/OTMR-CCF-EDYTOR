using System.Security.Cryptography;
using CcfEditor.Otmr.Live;
using CcfEditor.Otmr.Rcm;

namespace CcfEditor.WinForms;

/// <summary>
/// Read-only presentation of explicitly verified RCM mappings decoded from
/// the single OTMR live stream owned by <see cref="OtmrLiveControl"/>.
/// This control never opens, closes, starts, or restarts serial transport.
/// </summary>
public partial class OtmrRcmLiveControl : UserControl
{
    private readonly RcmVerifiedLiveDecoder _verifiedLiveDecoder = new();
    private RcmProfile? _activeRcmProfile;
    private string? _activeRcmProfilePath;
    private OtmrLiveState _liveState = OtmrLiveState.Disconnected;
    private IReadOnlyList<RcmVerifiedLiveSignal> _decodedSignals = Array.Empty<RcmVerifiedLiveSignal>();
    private DateTimeOffset? _latestFrameTimestamp;
    private string? _activeRcmProfileSha256;

    internal event EventHandler<VerifiedLiveStateDecodedEventArgs>? VerifiedLiveStateDecoded;

    public OtmrRcmLiveControl()
    {
        InitializeComponent();
        RefreshStatusAndGrid();
    }

    internal void SetActiveRcmProfile(RcmProfile? profile, string? profilePath)
    {
        if (InvokeRequired)
        {
            BeginInvoke((Action)(() => SetActiveRcmProfile(profile, profilePath)));
            return;
        }

        bool hasLoadedJson = profile is not null && !string.IsNullOrWhiteSpace(profilePath);
        _activeRcmProfile = hasLoadedJson ? profile : null;
        _activeRcmProfilePath = hasLoadedJson ? Path.GetFullPath(profilePath!) : null;
        _activeRcmProfileSha256 = _activeRcmProfilePath is not null && File.Exists(_activeRcmProfilePath)
            ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(_activeRcmProfilePath)))
            : null;
        _decodedSignals = _verifiedLiveDecoder.DescribeVerifiedMappings(_activeRcmProfile);
        _latestFrameTimestamp = null;
        RefreshStatusAndGrid();
    }

    internal void SetOtmrLiveState(OtmrLiveState state)
    {
        if (InvokeRequired)
        {
            BeginInvoke((Action)(() => SetOtmrLiveState(state)));
            return;
        }

        _liveState = state;
        RefreshHeaderStatus();
    }

    internal void ReportRawLiveFrame(DateTimeOffset timestamp, OtmrLiveFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (InvokeRequired)
        {
            BeginInvoke((Action)(() => ReportRawLiveFrame(timestamp, frame)));
            return;
        }

        _decodedSignals = _verifiedLiveDecoder.Decode(frame, _activeRcmProfile);
        _latestFrameTimestamp = timestamp;
        RefreshStatusAndGrid();
        VerifiedLiveStateDecoded?.Invoke(this, new VerifiedLiveStateDecodedEventArgs(
            timestamp,
            _activeRcmProfilePath is null ? null : Path.GetFileName(_activeRcmProfilePath),
            _activeRcmProfileSha256,
            _decodedSignals.ToArray()));
    }

    internal IReadOnlyList<RcmVerifiedLiveSignal> GetDecodedSignalSnapshot() =>
        _decodedSignals.ToArray();

    private void RefreshStatusAndGrid()
    {
        RefreshDecodedSignalsGrid();
        RefreshHeaderStatus();
    }

    private void RefreshHeaderStatus()
    {
        rcmProfileStatusLabel.Text = _activeRcmProfile is null
            ? "RCM profile: No RCM profile loaded"
            : $"RCM profile: {Path.GetFileName(_activeRcmProfilePath)}";
        sourceCcfStatusLabel.Text = _activeRcmProfile is null
            ? "Source CCF: -"
            : $"Source CCF: {_activeRcmProfile.SourceCcfFilename}";
        verifiedMappingsStatusLabel.Text = $"Verified mappings: {_decodedSignals.Count:N0}" +
            (_latestFrameTimestamp is null
                ? string.Empty
                : $" | frame {_latestFrameTimestamp.Value.ToLocalTime():HH:mm:ss.fff}");
        liveStateStatusLabel.Text = _liveState switch
        {
            OtmrLiveState.LiveReady => "Live state: OTMR LIVE READY - waiting for input events",
            OtmrLiveState.LiveActive => "Live state: OTMR LIVE STREAM ACTIVE",
            OtmrLiveState.Disconnected => "Live state: DISCONNECTED",
            _ => $"Live state: {_liveState}"
        };
    }

    private void RefreshDecodedSignalsGrid()
    {
        decodedSignalsGrid.SuspendLayout();
        try
        {
            decodedSignalsGrid.Rows.Clear();
            foreach (RcmVerifiedLiveSignal signal in _decodedSignals)
            {
                string physical = $"{signal.Connector}-{signal.Pin}";
                string logical = signal.LogicalCard.HasValue && signal.LogicalChannel.HasValue
                    ? $"Card {signal.LogicalCard} / Ch {signal.LogicalChannel}"
                    : "-";
                string state = signal.State switch
                {
                    RcmDecodedElectricalState.Active => "ACTIVE",
                    RcmDecodedElectricalState.Inactive => "INACTIVE",
                    _ => "UNKNOWN"
                };
                string raw = signal.RawObservedValue.HasValue
                    ? signal.BitIndex.HasValue
                        ? $"pos {signal.RawPosition} bit {signal.BitIndex} = {signal.ObservedBitValue} (raw {signal.RawObservedValue:X2})"
                        : $"pos {signal.RawPosition} = {signal.RawObservedValue:X2}"
                    : signal.Detail;
                int rowIndex = decodedSignalsGrid.Rows.Add(
                    physical,
                    signal.Function,
                    logical,
                    state,
                    raw,
                    signal.VerificationStatus);
                DataGridViewRow row = decodedSignalsGrid.Rows[rowIndex];
                row.DefaultCellStyle.ForeColor = signal.State switch
                {
                    RcmDecodedElectricalState.Active => Color.DarkGreen,
                    RcmDecodedElectricalState.Inactive => SystemColors.ControlText,
                    _ => Color.DarkRed
                };
                row.Cells[decodedRawColumn.Index].ToolTipText = signal.Detail;
            }
        }
        finally
        {
            decodedSignalsGrid.ResumeLayout();
        }

        decodedSignalsStatusLabel.Text = _activeRcmProfile is null
            ? "No RCM profile loaded."
            : _decodedSignals.Count == 0
                ? "No verified RCM mappings available for decoded live signals."
                : _latestFrameTimestamp is null
                    ? $"{_decodedSignals.Count} explicitly verified RCM mapping(s) loaded; awaiting genuine live data."
                    : $"Decoded {_decodedSignals.Count} explicitly verified RCM mapping(s) from the latest genuine live frame.";
    }
}

internal sealed class VerifiedLiveStateDecodedEventArgs(
    DateTimeOffset timestampUtc,
    string? profileFilename,
    string? profileSha256,
    IReadOnlyList<RcmVerifiedLiveSignal> signals) : EventArgs
{
    public DateTimeOffset TimestampUtc { get; } = timestampUtc.ToUniversalTime();
    public string? ProfileFilename { get; } = profileFilename;
    public string? ProfileSha256 { get; } = profileSha256;
    public IReadOnlyList<RcmVerifiedLiveSignal> Signals { get; } = signals;
}
