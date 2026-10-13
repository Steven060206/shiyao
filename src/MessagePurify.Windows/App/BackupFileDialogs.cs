using System.Windows;
using Microsoft.Win32;

namespace MessagePurify.Windows.App;

public interface IBackupFileDialogs
{
    string? SelectExportPath(Window owner, string suggestedName);
    string? SelectImportPath(Window owner);
}

public sealed class BackupFileDialogs : IBackupFileDialogs
{
    public string? SelectExportPath(Window owner, string suggestedName)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出当前账号备份", Filter = "消息提纯备份 (*.zip)|*.zip", DefaultExt = ".zip",
            AddExtension = true, FileName = suggestedName, OverwritePrompt = true
        };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public string? SelectImportPath(Window owner)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "选择账号备份", Filter = "消息提纯备份 (*.zip)|*.zip", CheckFileExists = true };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }
}
