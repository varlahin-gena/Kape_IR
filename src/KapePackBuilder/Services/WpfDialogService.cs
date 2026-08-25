using System.Windows;
using Microsoft.Win32;

namespace KapePackBuilder.Services;

public sealed class WpfDialogService : IDialogService
{
    public void ShowMessage(string message, string title, DialogIcon icon = DialogIcon.Info)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, Map(icon));
    }

    public bool Confirm(string message, string title, DialogIcon icon = DialogIcon.Question)
    {
        return MessageBox.Show(message, title, MessageBoxButton.YesNo, Map(icon)) == MessageBoxResult.Yes;
    }

    public string? PickFolder(string title, string? initialDirectory = null)
    {
        var dlg = new OpenFolderDialog { Title = title };
        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
            dlg.InitialDirectory = initialDirectory;
        return dlg.ShowDialog() == true ? dlg.FolderName : null;
    }

    public string? PickOpenFile(string title, string filter, string? initialDirectory = null)
    {
        var dlg = new OpenFileDialog { Title = title, Filter = filter };
        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
            dlg.InitialDirectory = initialDirectory;
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    private static MessageBoxImage Map(DialogIcon icon) => icon switch
    {
        DialogIcon.Warning => MessageBoxImage.Warning,
        DialogIcon.Error => MessageBoxImage.Error,
        DialogIcon.Question => MessageBoxImage.Question,
        DialogIcon.None => MessageBoxImage.None,
        _ => MessageBoxImage.Information
    };
}
