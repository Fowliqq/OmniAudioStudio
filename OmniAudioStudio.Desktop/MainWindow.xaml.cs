using System.Windows;
using OmniAudioStudio.Desktop.Services;
using OmniAudioStudio.Desktop.ViewModels;
using Wpf.Ui.Controls;

namespace OmniAudioStudio.Desktop;

public partial class MainWindow : FluentWindow
{
    public MainViewModel ViewModel { get; }

    public MainWindow()
    {
        var locator = new FFmpegLocator();
        var metadata = new AudioMetadataService();
        var queue = new QueueManager(locator, metadata);

        ViewModel = new MainViewModel(locator, queue);
        DataContext = ViewModel;

        InitializeComponent();
    }

    private void OnWindowDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private async void OnWindowDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Handled = true;
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                await ViewModel.AddPathsAsync(files);
            }
        }
    }
}