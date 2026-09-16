namespace CcfEditor.WinForms;

/// <summary>
/// Guards programmatic scrolling while WinForms is creating, hiding, suspending,
/// or compressing a DataGridView during layout.
/// </summary>
public static class DataGridViewViewport
{
    public static bool CanDisplayRows(DataGridView? grid)
    {
        if (grid is null || grid.IsDisposed || grid.Disposing || !grid.IsHandleCreated || !grid.Created || !grid.Visible)
            return false;
        if (grid.Rows.Count == 0 || grid.Columns.GetColumnCount(DataGridViewElementStates.Visible) == 0)
            return false;
        if (grid.ClientSize.Width <= 0 || grid.ClientSize.Height <= 0)
            return false;

        int headerHeight = grid.ColumnHeadersVisible ? grid.ColumnHeadersHeight : 0;
        int usableHeight = grid.ClientSize.Height - headerHeight - 2;
        int minimumRowHeight = Math.Max(1, grid.RowTemplate.Height);
        if (usableHeight < minimumRowHeight)
            return false;

        Form? form = grid.FindForm();
        if (form is not null && (!form.Visible || form.WindowState == FormWindowState.Minimized))
            return false;

        // This returns zero while column headers/borders consume the available
        // viewport, including the layout state that throws from the setter.
        return grid.DisplayedRowCount(includePartialRow: true) > 0;
    }

    public static bool TryScrollToRow(DataGridView? grid, int rowIndex)
    {
        if (!CanDisplayRows(grid) || grid is null)
            return false;
        if ((uint)rowIndex >= (uint)grid.Rows.Count)
            return false;

        DataGridViewRow row = grid.Rows[rowIndex];
        if (!row.Visible || row.IsNewRow)
            return false;

        grid.FirstDisplayedScrollingRowIndex = rowIndex;
        return true;
    }
}
