using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using KapeIR.Core.Services.Help;

namespace KapeIR.Builder.ViewModels;

public sealed partial class HelpWindowViewModel : ObservableObject
{
    private readonly IReadOnlyList<OperatorHelpContent.Section> _all;

    public ObservableCollection<OperatorHelpContent.Section> VisibleSections { get; } = new();

    [ObservableProperty]
    private OperatorHelpContent.Section? _selectedSection;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private string _markdown = "";

    public HelpWindowViewModel()
    {
        _all = OperatorHelpContent.GetSections();
        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedSectionChanged(OperatorHelpContent.Section? value)
        => Markdown = value?.BodyMarkdown ?? "";

    private void ApplyFilter()
    {
        var keepId = SelectedSection?.Id;
        var filtered = OperatorHelpContent.Filter(SearchText);

        VisibleSections.Clear();
        foreach (var s in filtered)
            VisibleSections.Add(s);

        if (VisibleSections.Count == 0)
        {
            SelectedSection = null;
            return;
        }

        var match = VisibleSections.FirstOrDefault(s => s.Id == keepId);
        SelectedSection = match ?? VisibleSections[0];
    }
}
