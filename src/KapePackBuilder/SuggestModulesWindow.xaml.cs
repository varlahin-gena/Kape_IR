using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using KapePackBuilder.Models;

namespace KapePackBuilder;

public partial class SuggestionRowVm : ObservableObject
{
    public ModuleSuggestion Suggestion { get; }
    [ObservableProperty] private bool _isSelected;

    public string Name => Suggestion.Module.DisplayName;
    public string Meta => $"{Suggestion.Score}  ·  {Suggestion.Module.Category}  ·  {Suggestion.Module.RelativePath}";
    public string Reason => Suggestion.Reason;
    public string Targets => string.Join(", ", Suggestion.MatchedTargets);
    public string Already => Suggestion.AlreadySelected ? "уже в пакете" : "";

    public SuggestionRowVm(ModuleSuggestion suggestion, bool selected)
    {
        Suggestion = suggestion;
        _isSelected = selected;
    }
}

public partial class SuggestModulesWindow : Window
{
    public ObservableCollection<SuggestionRowVm> Rows { get; } = new();
    public bool Applied { get; private set; }
    public List<CatalogItem> Chosen { get; } = new();

    public SuggestModulesWindow(IEnumerable<ModuleSuggestion> suggestions)
    {
        InitializeComponent();
        DataContext = this;
        foreach (var s in suggestions)
            Rows.Add(new SuggestionRowVm(s, selected: !s.AlreadySelected));
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        Chosen.Clear();
        Chosen.AddRange(Rows.Where(r => r.IsSelected).Select(r => r.Suggestion.Module));
        Applied = true;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void SelectNew_Click(object sender, RoutedEventArgs e)
    {
        foreach (var r in Rows)
            r.IsSelected = !r.Suggestion.AlreadySelected;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var r in Rows)
            r.IsSelected = true;
    }

    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var r in Rows)
            r.IsSelected = false;
    }
}
