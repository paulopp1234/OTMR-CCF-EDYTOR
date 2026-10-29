namespace CcfEditor.WinForms;

public partial class OtmrBenchControl
{
    private bool _finalInitialSplitterApplied;
    private bool _finalInitialSplitterScheduled;

    internal void ScheduleFinalInitialLayout()
    {
        if (!Visible || _finalInitialSplitterApplied || _finalInitialSplitterScheduled ||
            !IsHandleCreated || IsDisposed)
        {
            return;
        }

        // The bench control lives on an initially hidden tab. Wait one UI turn after
        // the host selects that tab so WinForms has applied the real visible width.
        _finalInitialSplitterScheduled = true;
        BeginInvoke((Action)(() =>
        {
            _finalInitialSplitterScheduled = false;
            ApplyFinalInitialLayout();
        }));
    }

    internal void ApplyFinalInitialLayout()
    {
        if (_finalInitialSplitterApplied || !Visible || IsDisposed || mainSplit.Width <= 0)
            return;

        _initialSplitterPositionApplied = false;
        ApplyInitialSplitterPosition();
        _finalInitialSplitterApplied = true;
    }
}
