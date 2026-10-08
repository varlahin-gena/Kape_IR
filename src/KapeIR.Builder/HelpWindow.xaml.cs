using System.Windows;
using KapeIR.Builder.ViewModels;

namespace KapeIR.Builder;

public partial class HelpWindow : Window
{
    public HelpWindow()
    {
        InitializeComponent();
        DataContext = new HelpWindowViewModel();
        // Ensure dark style wins even if MdXaml resets MarkdownStyleName during init.
        Loaded += (_, _) =>
        {
            if (TryFindResource("HelpMarkdownDark") is Style style)
                HelpMarkdown.MarkdownStyle = style;
        };
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
