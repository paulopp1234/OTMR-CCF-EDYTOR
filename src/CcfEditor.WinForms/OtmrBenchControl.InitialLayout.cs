namespace CcfEditor.WinForms;

public partial class OtmrBenchControl
{
    private bool _finalInitialSplitterApplied;
    private bool _finalInitialSplitterScheduled;

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);

        if (!Visible || _finalInitialSplitterApplied || _finalInitialSplitterScheduled ||
            !IsHandleCreated || IsDisposed)
        {
            return;
        }

        // The bench control lives on an initially hidden tab. Its OnLoad dimensions
        // are therefore not the final visible tab dimensions. Wait one UI turn after
        // the tab becomes visible, then apply the intended initial split exactly once.
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
