namespace CcfEditor.WinForms;

public partial class OtmrBenchControl
{
    private bool _finalInitialSplitterApplied;

    internal void ApplyFinalInitialLayout()
    {
        if (_finalInitialSplitterApplied || IsDisposed || mainSplit.Width <= 0)
            return;

        // OtmrBenchControl.OnLoad can run before the host form receives its final
        // dimensions. MainForm calls this once from OnShown, when the real tab width
        // is known, so the intended ~70/30 grid/workflow split is based on the actual
        // window size. It is not applied again, leaving the splitter user-adjustable.
        _initialSplitterPositionApplied = false;
        ApplyInitialSplitterPosition();
        _finalInitialSplitterApplied = true;
    }
}
