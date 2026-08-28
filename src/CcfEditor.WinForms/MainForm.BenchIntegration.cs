using CcfEditor.Core;
using CcfEditor.Otmr.Live;
using CcfEditor.Otmr.Rcm;

namespace CcfEditor.WinForms;

public partial class MainForm
{
    internal CcfDocument? GetCurrentCcfForBench() => _document;

    internal void ReportOtmrLiveFrame(DateTimeOffset timestamp, OtmrLiveFrame frame)
    {
        otmrBenchControl.ReportRawLiveFrame(timestamp, frame);
        otmrRcmLiveControl.ReportRawLiveFrame(timestamp, frame);
    }

    internal void ReportOtmrLiveState(OtmrLiveState state)
    {
        otmrBenchControl.SetOtmrLiveState(state);
        otmrRcmLiveControl.SetOtmrLiveState(state);
    }

    private void OtmrBenchControl_CurrentRcmProfileChanged(
        object? sender,
        CurrentRcmProfileChangedEventArgs e) =>
        otmrRcmLiveControl.SetActiveRcmProfile(
            e.IsLoadedJson ? e.Profile : null,
            e.IsLoadedJson ? e.ProfilePath : null);
}
