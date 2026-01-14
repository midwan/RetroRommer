using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
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
public partial class MainWindow : INotifyPropertyChanged
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
    private bool _abortedDueToRateLimit;

    private bool _isDownloading;

    public bool IsDownloading
    {
        get => _isDownloading;
        private set
        {
            if (_isDownloading == value) return;
            _isDownloading = value;

            ButtonDownload.IsEnabled = !value;
            ButtonAbort.IsEnabled = value;

            OnPropertyChanged();
        }
    }

    public ObservableCollection<DownloadItemProgressViewModel> DownloadProgressCollection { get; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        if (string.IsNullOrWhiteSpace(propertyName)) return;

        if (Dispatcher.CheckAccess())
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        else
        {
            // avoid potential deadlock if called while UI thread is awaiting
            Dispatcher.BeginInvoke(new Action(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName))));
        }
    }

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

        DownloadProgressCollection.CollectionChanged += DownloadsCollection_CollectionChanged;

        _service = new RetroRommerService(_logger);

        TextBoxWebsite.Text = configuration.GetValue("Website", string.Empty);
        TextBoxFilename.Text = configuration.GetValue("MissFile", string.Empty);
        TextBoxDestination.Text = configuration.GetValue("Destination", string.Empty);
        TextBoxUsername.Text = configuration.GetValue("Username", string.Empty);
        PBoxPassword.Password = configuration.GetValue("Password", string.Empty);
        CheckBoxCleanup.IsChecked = configuration.GetValue("CleanupReport", true);

        // initialize state
        IsDownloading = false;
    }

    private static string NormalizeDownloadError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error)) return "Failed";

        if (error.Contains("404", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("NotFound", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("Not Found", StringComparison.OrdinalIgnoreCase))
        {
            return "Not Found";
        }

        if (error.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            return "Timeout";
        }

        if (error.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("401", StringComparison.OrdinalIgnoreCase))
        {
            return "Unauthorized";
        }

        if (error.Contains("too many attempts", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("too many", StringComparison.OrdinalIgnoreCase) && error.Contains("attempt", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("429", StringComparison.OrdinalIgnoreCase))
        {
            return "Rate limit reached";
        }

        return error;
    }

    private void DownloadsCollection_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add) return;

        var last = e.NewItems?[^1];
        if (last == null) return;

        if (Dispatcher.CheckAccess())
        {
            LvDownloads.ScrollIntoView(last);
        }
        else
        {
            Dispatcher.BeginInvoke(new Action(() => LvDownloads.ScrollIntoView(last)));
        }
    }

    private void ButtonSelectFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            DefaultExt = ".txt",
            Filter = "TXT Files (*.txt)|*.txt"
        };

        var result = dlg.ShowDialog();
        if (result != true) return;
        TextBoxFilename.Text = dlg.FileName;
    }

    private void ButtonSelectDestination_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new VistaFolderBrowserDialog();
        if (dialog.ShowDialog() == true) TextBoxDestination.Text = dialog.SelectedPath;
    }

    private async void ButtonDownload_Click(object sender, RoutedEventArgs e)
    {
        try
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
            _abortedDueToRateLimit = false;
            _downloadCts?.Cancel();
            _downloadCts?.Dispose();
            _downloadCts = new CancellationTokenSource();

            await PrepareAndDownloadFiles(_downloadCts.Token);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Unexpected error starting download");
            MessageBox.Show(ex.Message, "Error starting download", MessageBoxButton.OK, MessageBoxImage.Error);
            IsDownloading = false;
            _downloadCts?.Dispose();
            _downloadCts = null;
        }
    }

    private async Task PrepareAndDownloadFiles(CancellationToken cancellationToken)
    {
        _logger.Information("Beginning to download files...");
        IsDownloading = true;

        try
        {
            ProgressBarDownload.Value = 0;
            DownloadProgressCollection.Clear();

            var downloadItems = _service.ParseReport(_filename).ToList();
            ProgressBarDownload.Maximum = downloadItems.Count;

            try
            {
                if (downloadItems.Count == 0)
                {
                    DownloadProgressCollection.Add(new DownloadItemProgressViewModel
                    {
                        FileName = "System",
                        Status = "No missing items found to download.",
                        BytesReceived = 0,
                        TotalBytes = null,
                        BytesPerSecond = null
                    });
                }

                foreach (var item in downloadItems)
                {
                    if (_abortRequested || cancellationToken.IsCancellationRequested) break;

                    var vm = new DownloadItemProgressViewModel
                    {
                        FileName = item.FileName,
                        Status = "Starting...",
                        StatusDetail = string.Empty,
                        IsSuccess = null,
                        IsCompleted = false,
                        BytesReceived = 0,
                        TotalBytes = null,
                        BytesPerSecond = null
                    };

                    DownloadProgressCollection.Add(vm);

                    var uiProgress = new Progress<DownloadProgressInfo>(p =>
                    {
                        vm.TotalBytes = p.TotalBytes;
                        vm.BytesReceived = p.BytesReceived;
                        vm.BytesPerSecond = p.BytesPerSecond;
                        vm.Status = "Downloading";
                    });

                    try
                    {
                        var result = await _service.GetFile(_website, item, _username, _password, _destinationPath, cancellationToken, uiProgress);

                        vm.IsCompleted = true;
                        vm.BytesPerSecond = null;

                        vm.IsSuccess = string.Equals(result, "OK", StringComparison.OrdinalIgnoreCase);
                        vm.Status = vm.IsSuccess == true ? "Done" : "Failed";
                        vm.StatusDetail = vm.IsSuccess == true ? "OK" : NormalizeDownloadError(result);

                        if (result == "Unauthorized")
                        {
                            vm.IsSuccess = false;
                            vm.Status = "Unauthorized";
                            vm.StatusDetail = "Unauthorized";
                            break;
                        }
                    }
                    catch (TooManyAttemptsException ex)
                    {
                        vm.IsCompleted = true;
                        vm.BytesPerSecond = null;
                        vm.IsSuccess = false;
                        vm.Status = "Aborted";
                        vm.StatusDetail = "Rate limit reached";
                        _abortedDueToRateLimit = true;
                        _abortRequested = true;
                        break;
                    }
                    catch (OperationCanceledException)
                    {
                        vm.IsCompleted = true;
                        vm.BytesPerSecond = null;
                        vm.IsSuccess = false;
                        vm.Status = "Canceled";
                        vm.StatusDetail = "Canceled";
                        throw;
                    }
                    catch (Exception ex)
                    {
                        vm.IsCompleted = true;
                        vm.BytesPerSecond = null;
                        vm.IsSuccess = false;
                        vm.Status = "Failed";
                        vm.StatusDetail = NormalizeDownloadError(ex.Message);
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

            // Cleanup successfully downloaded items even if we aborted, if option is enabled.
            if (CheckBoxCleanup.IsChecked == true)
            {
                try
                {
                    var successfulItems = downloadItems.Where(x =>
                        DownloadProgressCollection.Any(p => p.FileName == x.FileName && p.Status == "Done")).ToList();

                    if (successfulItems.Count > 0)
                    {
                        await Task.Run(() => _service.CleanupReport(_filename, successfulItems));

                        DownloadProgressCollection.Add(new DownloadItemProgressViewModel
                        {
                            FileName = "System",
                            Status = _abortRequested
                                ? $"Report file cleaned up ({successfulItems.Count} downloaded item(s) removed)."
                                : "Report file cleaned up.",
                            BytesReceived = 0,
                            TotalBytes = null,
                            BytesPerSecond = null
                        });
                    }
                }
                catch (Exception ex)
                {
                    DownloadProgressCollection.Add(new DownloadItemProgressViewModel
                    {
                        FileName = "System",
                        Status = $"Failed to clean up report file: {ex.Message}",
                        BytesReceived = 0,
                        TotalBytes = null,
                        BytesPerSecond = null
                    });
                }
            }

            if (_abortRequested)
            {
                _abortRequested = false;
                DownloadProgressCollection.Add(new DownloadItemProgressViewModel
                {
                    FileName = "System",
                    Status = "Aborted!",
                    IsCompleted = true,
                    IsSuccess = false,
                    StatusDetail = _abortedDueToRateLimit
                        ? "Aborted: server rate limit reached (too many attempts)."
                        : "Aborted.",
                    BytesReceived = 0,
                    TotalBytes = null,
                    BytesPerSecond = null
                });
            }
            else
            {
                DownloadProgressCollection.Add(new DownloadItemProgressViewModel
                {
                    FileName = "System",
                    Status = "All downloads finished.",
                    BytesReceived = 0,
                    TotalBytes = null,
                    BytesPerSecond = null
                });

                ProgressBarDownload.Value = 100;
            }

            _downloadCts?.Dispose();
            _downloadCts = null;
        }
        finally
        {
            IsDownloading = false;
        }
    }

    private void ButtonAbort_OnClick(object sender, RoutedEventArgs e)
    {
        _abortRequested = true;
        ButtonAbort.IsEnabled = false;
        _downloadCts?.Cancel();
    }

    protected override void OnClosed(EventArgs e)
    {
        SaveSettings();
        base.OnClosed(e);
    }

    private void SaveSettings()
    {
        try
        {
            var jsonPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
            string json = File.Exists(jsonPath) ? File.ReadAllText(jsonPath) : "{}";

            var options = new System.Text.Json.Nodes.JsonObject();

            try
            {
                var node = System.Text.Json.Nodes.JsonNode.Parse(json);
                if (node is System.Text.Json.Nodes.JsonObject obj) options = obj;
            }
            catch
            {
            }

            options["MissFile"] = TextBoxFilename.Text;
            options["Website"] = TextBoxWebsite.Text;
            options["Username"] = TextBoxUsername.Text;
            options["Password"] = PBoxPassword.Password;
            options["Destination"] = TextBoxDestination.Text;
            options["CleanupReport"] = CheckBoxCleanup.IsChecked == true;

            File.WriteAllText(jsonPath, options.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to save settings: {ex.Message}");
        }
    }
}