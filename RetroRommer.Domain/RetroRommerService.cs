using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using Serilog;

namespace RetroRommer.Domain;

public enum DownloadType
{
    Rom,
    Bios,
    Chd,
    Sample
}

public class DownloadItem
{
    public string SetName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DownloadType Type { get; set; }
}

public sealed class TooManyAttemptsException : Exception
{
    public TooManyAttemptsException(string message) : base(message)
    {
    }
}

public class RetroRommerService
{
    private readonly ILogger _logger;

    public RetroRommerService(ILogger logger)
    {
        _logger = logger;
    }

    public IEnumerable<DownloadItem> ParseReport(string file)
    {
        var results = new List<DownloadItem>();
        if (string.IsNullOrEmpty(file) || !File.Exists(file)) return results;

        try
        {
            var lines = File.ReadAllLines(file);
            string currentSet = string.Empty;
            
            var processedSetsRom = new HashSet<string>();
            var processedSetsSample = new HashSet<string>();

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;

                if (string.IsNullOrWhiteSpace(trimmed)) continue;

                // Check for missing items first
                if (trimmed.StartsWith("missing rom:"))
                {
                     if (string.IsNullOrEmpty(currentSet)) continue;
                     
                     var content = trimmed.Substring("missing rom:".Length).Trim();
                     var crcIndex = content.IndexOf("[", StringComparison.OrdinalIgnoreCase);
                     if (crcIndex > 0) content = content.Substring(0, crcIndex).Trim();
                     
                     if (string.IsNullOrWhiteSpace(content)) continue;

                     if (content.EndsWith(".chd", StringComparison.OrdinalIgnoreCase))
                     {
                         results.Add(new DownloadItem 
                         { 
                             SetName = currentSet, 
                             FileName = content, 
                             Type = DownloadType.Chd 
                         });
                     }
                     else
                     {
                         if (!processedSetsRom.Contains(currentSet))
                         {
                             results.Add(new DownloadItem 
                             { 
                                 SetName = currentSet, 
                                 FileName = $"{currentSet}.zip", 
                                 Type = DownloadType.Rom 
                             });
                             processedSetsRom.Add(currentSet);
                         }
                     }
                     continue;
                }
                
                if (trimmed.StartsWith("missing sample:"))
                {
                    if (string.IsNullOrEmpty(currentSet)) continue;

                    if (!processedSetsSample.Contains(currentSet))
                    {
                        results.Add(new DownloadItem 
                        { 
                            SetName = currentSet, 
                            FileName = $"{currentSet}.zip", 
                            Type = DownloadType.Sample 
                        });
                        processedSetsSample.Add(currentSet);
                    }
                    continue;
                }

                if (trimmed.StartsWith("missing disk:"))
                {
                    if (string.IsNullOrEmpty(currentSet)) continue;
                    
                    var content = trimmed.Substring("missing disk:".Length).Trim();
                    var crcIndex = content.IndexOf("[", StringComparison.OrdinalIgnoreCase);
                    if (crcIndex > 0) content = content.Substring(0, crcIndex).Trim();
                    
                    if (string.IsNullOrWhiteSpace(content)) continue;

                    if (!content.EndsWith(".chd", StringComparison.OrdinalIgnoreCase))
                    {
                        content += ".chd";
                    }

                    results.Add(new DownloadItem 
                    { 
                        SetName = currentSet, 
                        FileName = content, 
                        Type = DownloadType.Chd 
                    });
                    continue;
                }

                // Set detection: "Game Name [setname]"
                if (trimmed.EndsWith("]") && trimmed.Contains("["))
                {
                    // Exclude lines that start with "missing" or contain "rom:"
                    // Although we checked for "missing rom" above, "missing machine" etc might fall through.
                    if (!trimmed.StartsWith("missing", StringComparison.OrdinalIgnoreCase))
                    {
                        var lastOpen = trimmed.LastIndexOf('[');
                        if (lastOpen != -1)
                        {
                            var possibleSet = trimmed.Substring(lastOpen + 1, trimmed.Length - lastOpen - 2);
                            // Ignore metadata tags like 'sampleof:', 'cloneof:'
                            if (!possibleSet.Contains(':'))
                            {
                                currentSet = possibleSet;
                            }
                        }
                    }
                    continue;
                }
            }
        }
        catch (Exception e)
        {
             _logger.Fatal($"Failed to parse file {file}\n{e}");
        }

        return results;
    }

    public async Task<string> GetFile(string website, DownloadItem item, string userName, string passwd, string destination, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var client = new HttpClient();
        var authToken = Encoding.ASCII.GetBytes($"{userName}:{passwd}");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authToken));

        string urlPath;
        string localFolder;
        
        if (!website.EndsWith("/")) website += "/";
        
        // Base logic
        switch (item.Type)
        {
            case DownloadType.Chd:
                urlPath = $"CHDs/{item.SetName}/{item.FileName}";
                localFolder = Path.Combine(destination, "CHDs", item.SetName);
                break;
            case DownloadType.Sample:
                 urlPath = $"samples/{item.FileName}";
                 localFolder = Path.Combine(destination, "samples");
                 break;
            case DownloadType.Bios:
                 urlPath = $"bios/{item.FileName}";
                 localFolder = Path.Combine(destination, "bios");
                 break;
            case DownloadType.Rom:
            default:
                 urlPath = $"currentroms/{item.FileName}";
                 localFolder = Path.Combine(destination, "currentroms");
                 break;
        }

        try 
        {
            return await TryDownload(client, website + urlPath, localFolder, item.FileName, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.Information($"Download canceled for {item.FileName}");
            throw;
        }
        catch (HttpRequestException ex) when (item.Type == DownloadType.Rom) 
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.Warning($"Failed to find {item.FileName} in currentroms, trying bios folder...");
            var biosUrl = $"bios/{item.FileName}";
            var biosFolder = Path.Combine(destination, "bios");
            try 
            {
                return await TryDownload(client, website + biosUrl, biosFolder, item.FileName, cancellationToken);
             }
             catch (TooManyAttemptsException)
             {
                 throw;
             }
             catch (OperationCanceledException)
             {
                 _logger.Information($"Download canceled for {item.FileName}");
                 throw;
             }
             catch (Exception innerEx)
             {
                 return HandleException(item.FileName, innerEx);
             }
        }
        catch (TooManyAttemptsException)
        {
            throw;
        }
        catch (Exception ex)
        {
             return HandleException(item.FileName, ex);
        }
    }

    private async Task<string> TryDownload(HttpClient client, string url, string folder, string fileName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
        
        _logger.Information($"Downloading {fileName} from {url} to {folder}");
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        // Check for HTML content even if status code is OK
        // This handles cases where the server returns a 200 OK with an HTML error page (e.g. rate limit, maintenance)
        if (response.Content.Headers.ContentType?.MediaType?.Equals("text/html", StringComparison.OrdinalIgnoreCase) == true)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (IsTooManyAttempts(response.StatusCode, response.ReasonPhrase, responseBody))
            {
                _logger.Fatal("Server reported too many attempts (HTML response). Aborting remaining downloads to avoid an IP ban.");
                throw new TooManyAttemptsException("Server reported too many attempts. Aborting downloads to avoid IP ban.");
            }

            // If it's HTML but we expected a file download, treat it as an error
            var preview = responseBody.Length > 200 ? responseBody[..200] : responseBody;
            throw new HttpRequestException($"Received unexpected HTML content instead of binary file. Response preview: {preview}");
        }
        
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (IsTooManyAttempts(response.StatusCode, response.ReasonPhrase, responseBody))
            {
                _logger.Fatal("Server reported too many attempts. Aborting remaining downloads to avoid an IP ban.");
                throw new TooManyAttemptsException("Server reported too many attempts. Aborting downloads to avoid IP ban.");
            }
            throw new HttpRequestException($"HTTP {response.StatusCode}: {response.ReasonPhrase}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var fileStream = new FileStream(Path.Combine(folder, fileName), FileMode.Create, FileAccess.Write, FileShare.None);
        await stream.CopyToAsync(fileStream, cancellationToken);
        
        return "OK";
    }

    private static bool IsTooManyAttempts(HttpStatusCode statusCode, string? reasonPhrase, string? responseBody)
    {
        if (statusCode == HttpStatusCode.TooManyRequests)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(reasonPhrase) && reasonPhrase.Contains("too many attempts", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(responseBody) && responseBody.Contains("too many attempts", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private string HandleException(string file, Exception ex)
    {
        _logger.Fatal($"File failed to download: {file}.\n{ex}");
        // Return a more readable error if possible, otherwise the exception message
        return ex.Message;
    }
}