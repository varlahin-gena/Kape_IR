using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using KapeIR.Triage.ViewModels;

namespace KapeIR.Triage;

public partial class MainWindow : Window
{
    private readonly TriageViewModel _vm;
    private DispatcherTimer? _copyPulseTimer;

    public MainWindow(TriageViewModel vm)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        InitializeComponent();
        DataContext = _vm;
        _vm.LogFragmentAppended += OnLogFragmentAppended;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        StartPulseTimer();
        await _vm.InitializeCommand.ExecuteAsync(null);
    }

    private void StartPulseTimer()
    {
        _copyPulseTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _copyPulseTimer.Tick -= CopyPulse_Tick;
        _copyPulseTimer.Tick += CopyPulse_Tick;
        _copyPulseTimer.Start();
    }

    private void CopyPulse_Tick(object? sender, EventArgs e)
    {
        if (_vm.IsCopyPulseActive)
            _vm.OnCopyPulseTick();
    }

    private void OnLogFragmentAppended(string fragment)
    {
        // AppendText keeps viewport stable; assigning Text (or OneWay binding) jumps to top first.
        LogBox.AppendText(fragment);
        LogBox.CaretIndex = LogBox.Text.Length;
        LogBox.ScrollToEnd();
    }

    private void DriveCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Win11 theme sometimes leaves editable ComboBox text blank after selection.
        try
        {
            if (sender is ComboBox combo && _vm.SelectedDrive is { } drive
                && !string.Equals(combo.Text, drive.Label, StringComparison.Ordinal))
                combo.Text = drive.Label;
        }
        catch
        {
            /* never crash the pack UI on drive pick */
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        if (_copyPulseTimer is not null)
        {
            _copyPulseTimer.Stop();
            _copyPulseTimer.Tick -= CopyPulse_Tick;
        }
        _vm.LogFragmentAppended -= OnLogFragmentAppended;
        base.OnClosed(e);
    }
}
