using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Threading;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Win32;
using Ookii.Dialogs.Wpf;
using RetroRommer.Domain;
using Serilog;
using Serilog.Core;

namespace RetroRommer.Core;

/// <summary>
///     Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow
{
    private readonly RetroRommerService _service;
    private string _destinationPath;
    private string _filename;
    private readonly Logger _logger;
    private string _password;
    private string _username;
    private bool _abortRequested;
    private string _website;
    private CancellationTokenSource? _downloadCts;

    public MainWindow()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json")
            .Build();

        _logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .CreateLogger();

        InitializeComponent();
        ((INotifyCollectionChanged)LvLog.Items).CollectionChanged += ListView_CollectionChanged;

        _service = new RetroRommerService(_logger);

        TextBoxWebsite.Text = configuration.GetValue("Website", string.Empty);
        TextBoxFilename.Text = configuration.GetValue("MissFile", string.Empty);
        TextBoxDestination.Text = configuration.GetValue("Destination", string.Empty);
        TextBoxUsername.Text = configuration.GetValue("Username", string.Empty);
        PBoxPassword.Password = configuration.GetValue("Password", string.Empty);
    }

    public ObservableCollection<LogDto> LogCollection { get; } =
        [];

    private void ListView_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            // scroll the new item into view
            if (e.NewItems?[0] != null)
                LvLog.ScrollIntoView(e.NewItems[0]);
        }
    }

    private void ButtonSelectFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            DefaultExt = ".txt",
            Filter =
                "TXT Files (*.txt)|*.txt"
        };

        var result = dlg.ShowDialog();
        if (result != true) return;
        var filename = dlg.FileName;
        TextBoxFilename.Text = filename;
    }

    private void ButtonSelectDestination_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new VistaFolderBrowserDialog();
        if (dialog.ShowDialog() == true) TextBoxDestination.Text = dialog.SelectedPath;
    }

    private async void ButtonDownload_Click(object sender, RoutedEventArgs e)
    {
        _website = TextBoxWebsite.Text;
        _filename = TextBoxFilename.Text;
        _destinationPath = TextBoxDestination.Text;
        _username = TextBoxUsername.Text;
        _password = PBoxPassword.Password;

        if (string.IsNullOrEmpty(_website) || string.IsNullOrEmpty(_filename) ||
            string.IsNullOrEmpty(_destinationPath))
        {
            MessageBox.Show(
                "Missing required information! Make sure the Website, filename and destination fields are filled in.",
                "Missing required information", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _abortRequested = false;
        _downloadCts?.Cancel();
        _downloadCts?.Dispose();
        _downloadCts = new CancellationTokenSource();

        await PrepareAndDownloadFiles(_downloadCts.Token);
    }

    private async Task PrepareAndDownloadFiles(CancellationToken cancellationToken)
    {
        _logger.Information("Beginning to download files...");
        ButtonAbort.IsEnabled = true;
        ProgressBarDownload.Value = 0;
        
        var downloadItems = _service.ParseReport(_filename).ToList();
        ProgressBarDownload.Maximum = downloadItems.Count;

        try
        {
            if (downloadItems.Count == 0)
            {
                LogCollection.Add(new LogDto
                {
                     Filename = "System",
                     Result = "No missing items found to download.",
                     Status = LogStatus.Warning
                });
            }

            foreach (var item in downloadItems)
            {
                if (_abortRequested || cancellationToken.IsCancellationRequested) break;
                
                try
                {
                    var result = await _service.GetFile(_website, item, _username, _password, _destinationPath, cancellationToken);
                    var logRow = new LogDto
                    {
                        Filename = item.FileName,
                        Result = result,
                        Status = result == "OK" ? LogStatus.Success : LogStatus.Warning
                    };
                    
                    if (result == "Unauthorized")
                    {
                        logRow.Status = LogStatus.Error;
                        LogCollection.Add(logRow);
                        break;
                    }
                    
                    LogCollection.Add(logRow);
                }
                catch (TooManyAttemptsException ex)
                {
                    LogCollection.Add(new LogDto
                    {
                        Filename = item.FileName,
                        Result = ex.Message,
                        Status = LogStatus.Error
                    });
                    _abortRequested = true;
                    break;
                }
                catch (Exception ex)
                {
                    LogCollection.Add(new LogDto
                    {
                        Filename = item.FileName,
                        Result = ex.Message,
                        Status = LogStatus.Error
                    });
                }
                finally
                {
                    ProgressBarDownload.Value++;
                }
            }
        }
        catch (OperationCanceledException)
        {
            _abortRequested = true;
        }

        if (_abortRequested)
        {
            _abortRequested = false;
            var logRow = new LogDto
            {
                Filename = "System",
                Result = "Aborted!",
                Status = LogStatus.Warning
            };
            LogCollection.Add(logRow);
        }
        else
        {
            var logRow = new LogDto
            {
                Filename = "System",
                Result = "All downloads finished.",
                Status = LogStatus.Success
            };
            LogCollection.Add(logRow);
            ProgressBarDownload.Value = 100; // ensure full bar on completion
        }
        ButtonAbort.IsEnabled = false;
        _downloadCts?.Dispose();
        _downloadCts = null;
    }

    private void ButtonAbort_OnClick(object sender, RoutedEventArgs e)
    {
        _abortRequested = true;
        ButtonAbort.IsEnabled = false;
        _downloadCts?.Cancel();
    }
}