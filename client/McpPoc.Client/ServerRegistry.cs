using System.Diagnostics;
using System.Text.Json;

namespace McpPoc.Client;

public sealed class ServerRegistry : IDisposable
{
    private readonly object _sync = new();
    private readonly string _registryFilePath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    private readonly Dictionary<string, ServerMetrics> _metrics = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ServerHealthSnapshot> _healthSnapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly HttpClient _httpClient = new();
    private FileSystemWatcher? _watcher;
    private Timer? _reloadTimer;
    private List<ServerRegistryEntry> _entries = [];

    private ServerRegistry(string registryFilePath)
    {
        _registryFilePath = Path.GetFullPath(registryFilePath);
    }

    public IReadOnlyList<ServerRegistryEntry> Entries
    {
        get
        {
            lock (_sync)
            {
                return _entries.ToArray();
            }
        }
    }

    public static ServerRegistry LoadFromFile(string registryFilePath, bool enableHotReload)
    {
        var registry = new ServerRegistry(registryFilePath);
        registry.ReloadFromFile();
        if (enableHotReload)
        {
            registry.StartHotReload();
        }

        return registry;
    }

    public IReadOnlyList<ServerRegistryEntry> FindByTags(IEnumerable<string> requiredTags)
    {
        var required = requiredTags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .ToArray();

        lock (_sync)
        {
            return _entries
                .Where(entry => HasAllTags(entry, required))
                .OrderBy(entry => IsKnownUnhealthy(entry.ServerId))
                .ThenByDescending(entry => entry.Priority)
                .ThenBy(entry => GetErrorRate(entry.ServerId))
                .ThenBy(entry => GetAverageLatencyMs(entry.ServerId))
                .ToArray();
        }
    }

    public async Task<ServerHealthCheckResult> CheckHealthAsync(ServerRegistryEntry entry, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        if (!string.Equals(entry.Transport, "http", StringComparison.OrdinalIgnoreCase))
        {
            return new ServerHealthCheckResult(
                entry.ServerId,
                IsHealthy: null,
                Attempts: 0,
                LatencyMs: null,
                Error: "Health checks are supported only for HTTP transport."
            );
        }

        if (string.IsNullOrWhiteSpace(entry.BaseUrl))
        {
            return new ServerHealthCheckResult(
                entry.ServerId,
                IsHealthy: false,
                Attempts: 0,
                LatencyMs: null,
                Error: "Missing baseUrl for HTTP server."
            );
        }

        var attempts = 0;
        Exception? lastError = null;
        var endpoint = BuildHealthEndpoint(entry);

        for (var attempt = 1; attempt <= entry.Health.RetryCount + 1; attempt++)
        {
            attempts = attempt;
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(entry.Health.TimeoutMs));

                var stopwatch = Stopwatch.StartNew();
                using var response = await _httpClient.GetAsync(endpoint, timeoutCts.Token);
                stopwatch.Stop();

                if (response.IsSuccessStatusCode)
                {
                    RecordCall(entry.ServerId, success: true, stopwatch.Elapsed.TotalMilliseconds);
                    SetHealthSnapshot(entry.ServerId, true, startedAt, attempts, null);
                    return new ServerHealthCheckResult(
                        entry.ServerId,
                        IsHealthy: true,
                        Attempts: attempts,
                        LatencyMs: stopwatch.Elapsed.TotalMilliseconds,
                        Error: null
                    );
                }

                lastError = new HttpRequestException($"Status {(int)response.StatusCode}");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                lastError = ex;
            }

            RecordCall(entry.ServerId, success: false, latencyMs: null);

            // TODO: Add circuit breaker state here when repeated failures cross a threshold.
            if (attempt <= entry.Health.RetryCount)
            {
                await Task.Delay(entry.Health.RetryDelayMs, cancellationToken);
            }
        }

        SetHealthSnapshot(entry.ServerId, false, startedAt, attempts, lastError?.Message);
        return new ServerHealthCheckResult(
            entry.ServerId,
            IsHealthy: false,
            Attempts: attempts,
            LatencyMs: null,
            Error: lastError?.Message ?? "Unknown health check error."
        );
    }

    public IReadOnlyList<ServerMetricsSnapshot> GetMetricsSnapshots()
    {
        lock (_sync)
        {
            return _metrics
                .Select(pair => new ServerMetricsSnapshot(
                    pair.Key,
                    pair.Value.TotalCalls,
                    pair.Value.SuccessCalls,
                    pair.Value.ErrorCalls,
                    pair.Value.AverageLatencyMs,
                    pair.Value.LastErrorAtUtc
                ))
                .OrderBy(snapshot => snapshot.ServerId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public IReadOnlyList<ServerHealthSnapshot> GetHealthSnapshots()
    {
        lock (_sync)
        {
            return _healthSnapshots.Values
                .OrderBy(snapshot => snapshot.ServerId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public void RecordCall(string serverId, bool success, double? latencyMs)
    {
        lock (_sync)
        {
            if (!_metrics.TryGetValue(serverId, out var metrics))
            {
                metrics = new ServerMetrics();
                _metrics[serverId] = metrics;
            }

            metrics.TotalCalls++;
            if (success)
            {
                metrics.SuccessCalls++;
            }
            else
            {
                metrics.ErrorCalls++;
                metrics.LastErrorAtUtc = DateTimeOffset.UtcNow;
            }

            if (latencyMs.HasValue)
            {
                metrics.ObservedLatencyCount++;
                var count = metrics.ObservedLatencyCount;
                metrics.AverageLatencyMs = ((metrics.AverageLatencyMs * (count - 1)) + latencyMs.Value) / count;
            }
        }
    }

    public void ReloadFromFile()
    {
        if (!File.Exists(_registryFilePath))
        {
            lock (_sync)
            {
                _entries = [];
            }

            return;
        }

        var json = File.ReadAllText(_registryFilePath);
        var document = JsonSerializer.Deserialize<ServerRegistryFileDocument>(json, _jsonOptions)
            ?? throw new InvalidDataException("Registry JSON is empty or invalid.");

        var loadedEntries = document.Servers ?? [];
        ValidateEntries(loadedEntries);

        lock (_sync)
        {
            _entries = loadedEntries;
            foreach (var entry in loadedEntries)
            {
                if (!_metrics.ContainsKey(entry.ServerId))
                {
                    _metrics[entry.ServerId] = new ServerMetrics();
                }
            }
        }
    }

    private void StartHotReload()
    {
        var directory = Path.GetDirectoryName(_registryFilePath);
        var fileName = Path.GetFileName(_registryFilePath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        _watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.Size
        };
        _watcher.Changed += OnRegistryFileChanged;
        _watcher.Created += OnRegistryFileChanged;
        _watcher.Renamed += OnRegistryFileChanged;
        _watcher.EnableRaisingEvents = true;
    }

    private void OnRegistryFileChanged(object sender, FileSystemEventArgs e)
    {
        _reloadTimer?.Dispose();
        _reloadTimer = new Timer(_ =>
        {
            try
            {
                ReloadFromFile();
                Console.WriteLine($"[registry] Reloaded '{_registryFilePath}'");
                // TODO: capability discovery can be cached and refreshed only on reconnect when needed.
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[registry] Reload failed: {ex.Message}");
            }
        }, null, dueTime: 250, period: Timeout.Infinite);
    }

    private static bool HasAllTags(ServerRegistryEntry entry, IReadOnlyList<string> requiredTags)
    {
        if (requiredTags.Count == 0)
        {
            return true;
        }

        var entryTokens = new HashSet<string>(
            entry.Tags.Concat(entry.Capabilities),
            StringComparer.OrdinalIgnoreCase
        );

        return requiredTags.All(entryTokens.Contains);
    }

    private static Uri BuildHealthEndpoint(ServerRegistryEntry entry)
    {
        var baseUrl = entry.BaseUrl ?? throw new InvalidOperationException("BaseUrl was not set.");
        var healthPath = string.IsNullOrWhiteSpace(entry.Health.Path) ? "/health" : entry.Health.Path.Trim();
        return new Uri(new Uri(baseUrl.EndsWith('/') ? baseUrl : $"{baseUrl}/"), healthPath.TrimStart('/'));
    }

    private static void ValidateEntries(IEnumerable<ServerRegistryEntry> entries)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.ServerId))
            {
                throw new InvalidDataException("Every registry entry must have a non-empty serverId.");
            }

            if (entry.ServerId.Contains("://", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"serverId '{entry.ServerId}' looks like a URL. Use a stable logical ID.");
            }

            if (!seen.Add(entry.ServerId))
            {
                throw new InvalidDataException($"Duplicate serverId found: '{entry.ServerId}'.");
            }

            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                throw new InvalidDataException($"Entry '{entry.ServerId}' is missing logical name.");
            }

            var requiresBaseUrl =
                string.Equals(entry.Transport, "http", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(entry.Transport, "mcp-http", StringComparison.OrdinalIgnoreCase);

            if (requiresBaseUrl && string.IsNullOrWhiteSpace(entry.BaseUrl))
            {
                throw new InvalidDataException($"Entry '{entry.ServerId}' transport '{entry.Transport}' requires baseUrl.");
            }
        }
    }

    private bool IsKnownUnhealthy(string serverId)
    {
        return _healthSnapshots.TryGetValue(serverId, out var snapshot) && snapshot.IsHealthy is false;
    }

    private double GetErrorRate(string serverId)
    {
        if (!_metrics.TryGetValue(serverId, out var metrics) || metrics.TotalCalls == 0)
        {
            return 0;
        }

        return (double)metrics.ErrorCalls / metrics.TotalCalls;
    }

    private double GetAverageLatencyMs(string serverId)
    {
        return _metrics.TryGetValue(serverId, out var metrics) ? metrics.AverageLatencyMs : 0;
    }

    private void SetHealthSnapshot(string serverId, bool? isHealthy, DateTimeOffset checkedAtUtc, int attempts, string? error)
    {
        lock (_sync)
        {
            _healthSnapshots[serverId] = new ServerHealthSnapshot(serverId, isHealthy, checkedAtUtc, attempts, error);
        }
    }

    public void Dispose()
    {
        _reloadTimer?.Dispose();
        _watcher?.Dispose();
        _httpClient.Dispose();
    }

    private sealed class ServerMetrics
    {
        public long TotalCalls { get; set; }
        public long SuccessCalls { get; set; }
        public long ErrorCalls { get; set; }
        public long ObservedLatencyCount { get; set; }
        public double AverageLatencyMs { get; set; }
        public DateTimeOffset? LastErrorAtUtc { get; set; }
    }
}

public sealed record ServerRegistryFileDocument
{
    public string Version { get; init; } = "1";
    public List<ServerRegistryEntry> Servers { get; init; } = [];
}

public sealed record ServerRegistryEntry
{
    public string ServerId { get; init; } = "";
    public string Name { get; init; } = "";
    public string Transport { get; init; } = "http";
    public string? BaseUrl { get; init; }
    public string? Command { get; init; }
    public string Author { get; init; } = "";
    public List<string> Capabilities { get; init; } = [];
    public int Priority { get; init; } = 100;
    public HealthCheckConfig Health { get; init; } = new();
    public List<string> Tags { get; init; } = [];
    public string Version { get; init; } = "1.0.0";
}

public sealed record HealthCheckConfig
{
    public string Path { get; init; } = "/health";
    public int RetryCount { get; init; } = 2;
    public int RetryDelayMs { get; init; } = 250;
    public int TimeoutMs { get; init; } = 2000;
}

public sealed record ServerHealthCheckResult(
    string ServerId,
    bool? IsHealthy,
    int Attempts,
    double? LatencyMs,
    string? Error
);

public sealed record ServerHealthSnapshot(
    string ServerId,
    bool? IsHealthy,
    DateTimeOffset CheckedAtUtc,
    int Attempts,
    string? Error
);

public sealed record ServerMetricsSnapshot(
    string ServerId,
    long TotalCalls,
    long SuccessCalls,
    long ErrorCalls,
    double AverageLatencyMs,
    DateTimeOffset? LastErrorAtUtc
)
{
    public double ErrorRate => TotalCalls == 0 ? 0 : (double)ErrorCalls / TotalCalls;
}
