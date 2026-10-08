using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace KapeIR.Ui.Dialogs;

/// <summary>Production WPF implementation of <see cref="IDialogService"/>.</summary>
public class WpfDialogService : IDialogService
{
    public virtual void ShowMessage(string message, string title, DialogIcon icon = DialogIcon.Info)
        => MessageBox.Show(message, title, MessageBoxButton.OK, Map(icon));

    public virtual bool Confirm(string message, string title, DialogIcon icon = DialogIcon.Question)
        => MessageBox.Show(message, title, MessageBoxButton.YesNo, Map(icon)) == MessageBoxResult.Yes;

    public virtual string? PickFolder(string title, string? initialDirectory = null)
    {
        var dlg = new OpenFolderDialog { Title = title };
        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
            dlg.InitialDirectory = initialDirectory;
        return dlg.ShowDialog() == true ? dlg.FolderName : null;
    }

    public virtual string? PickOpenFile(string title, string filter, string? initialDirectory = null)
    {
        var dlg = new OpenFileDialog { Title = title, Filter = filter };
        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
            dlg.InitialDirectory = initialDirectory;
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public virtual string? PickSaveFile(string title, string filter, string defaultFileName, string? initialDirectory = null)
    {
        var dlg = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            FileName = defaultFileName
        };
        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
            dlg.InitialDirectory = initialDirectory;
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    protected static MessageBoxImage Map(DialogIcon icon) => icon switch
    {
        DialogIcon.Warning => MessageBoxImage.Warning,
        DialogIcon.Error => MessageBoxImage.Error,
        DialogIcon.Question => MessageBoxImage.Question,
        DialogIcon.None => MessageBoxImage.None,
        _ => MessageBoxImage.Information
    };
}
