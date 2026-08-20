CCF Editor - Designer GUI build

This version uses a standard Visual Studio WinForms Designer form.

Expected Visual Studio structure under CcfEditor.WinForms:
  MainForm.cs
    MainForm.Designer.cs
    MainForm.resx

MainForm.cs contains application logic/event handlers only.
MainForm.Designer.cs contains all control creation and layout.
MainForm.resx is linked to MainForm.cs.

There are no WPF/XAML files and no runtime GUI construction helper methods.

To open the GUI designer:
  1. Open CcfEditor.sln in Visual Studio 2022.
  2. Expand CcfEditor.WinForms.
  3. Right-click MainForm.cs.
  4. Choose View Designer.

If View Designer is not shown, close Visual Studio, delete the .vs folder next to the solution, reopen CcfEditor.sln, then right-click MainForm.cs again.
