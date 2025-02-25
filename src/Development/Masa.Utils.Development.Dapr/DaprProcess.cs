namespace Masa.Utils.Development.Dapr;

public class DaprProcess : IDaprProcess
{
    private readonly object _lock = new();

    private readonly IDaprProvider _daprProvider;
    private readonly IProcessProvider _processProvider;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly ILogger<DaprProcess>? _logger;
    private IProcess? _process;
    private DaprProcessStatus Status { get; set; }
    private System.Timers.Timer? _heartBeatTimer;
    private DaprCoreOptions? _successDaprOptions;

    /// <summary>
    /// record whether dapr is initialized for the first time
    /// </summary>
    private bool _isFirst = true;

    public DaprProcess(IDaprProvider daprProvider, IProcessProvider processProvider, ILoggerFactory? loggerFactory)
    {
        _daprProvider = daprProvider;
        _processProvider = processProvider;
        _loggerFactory = loggerFactory;
        _logger = _loggerFactory?.CreateLogger<DaprProcess>();
    }

    public void Start(DaprOptions options, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            StartCore(GetDaprOptions(options), cancellationToken);
        }
    }

    private void StartCore(DaprCoreOptions options, CancellationToken cancellationToken = default)
    {
        UpdateStatus(DaprProcessStatus.Starting);
        var commandLineBuilder = Initialize(options, cancellationToken);
        StopCore(_successDaprOptions, cancellationToken);

        var utils = new ProcessUtils(_loggerFactory);

        utils.OutputDataReceived += delegate(object? sender, DataReceivedEventArgs args)
        {
            if (_isFirst)
            {
                CompleteDaprOptions();
                _isFirst = false;
            }
            DaprProcess_OutputDataReceived(sender, args);
        };
        utils.ErrorDataReceived += DaprProcess_ErrorDataReceived;
        utils.Exit += delegate
        {
            UpdateStatus(DaprProcessStatus.Stopped);
            _logger?.LogInformation("{Name} process has exited", Const.DEFAULT_FILE_NAME);
        };
        var process = utils.Run(Const.DEFAULT_FILE_NAME, $"run {commandLineBuilder}", options.CreateNoWindow);
        _process = new SystemProcess(process);
        if (_heartBeatTimer == null && options.EnableHeartBeat)
        {
            _heartBeatTimer = new System.Timers.Timer
            {
                AutoReset = true,
                Interval = options.HeartBeatInterval
            };
            _heartBeatTimer.Elapsed += (sender, args) => HeartBeat(cancellationToken);
            _heartBeatTimer.Start();
        }
        UpdateStatus(DaprProcessStatus.Started);
    }

    private static void DaprProcess_OutputDataReceived(object? sender, DataReceivedEventArgs e)
    {
        if (e.Data == null) return;

        var dataSpan = e.Data.AsSpan();
        var levelStartIndex = e.Data.IndexOf("level=", StringComparison.Ordinal) + 6;
        var level = "information";
        if (levelStartIndex > 5)
        {
            var levelLength = dataSpan.Slice(levelStartIndex).IndexOf(' ');
            level = dataSpan.Slice(levelStartIndex, levelLength).ToString();
        }

        var color = Console.ForegroundColor;
        switch (level)
        {
            case "warning":
                Console.ForegroundColor = ConsoleColor.Yellow;
                break;
            case "error":
            case "critical":
            case "fatal":
                Console.ForegroundColor = ConsoleColor.Red;
                break;
            default:
                break;
        }

        Console.WriteLine(e.Data);
        Console.ForegroundColor = color;
    }

    private static void DaprProcess_ErrorDataReceived(object? sender, DataReceivedEventArgs e)
    {
        if (e.Data == null) return;

        var color = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(e.Data);
        Console.ForegroundColor = color;
    }

    public void Stop(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            StopCore(_successDaprOptions, cancellationToken);
        }
    }

    private void StopCore(DaprCoreOptions? options, CancellationToken cancellationToken = default)
    {
        _process?.WaitForExit();
        if (options != null)
        {
            List<DaprRuntimeOptions> daprList = _daprProvider.GetDaprList(options.AppId);
            if (daprList.Any())
            {
                foreach (var dapr in daprList)
                {
                    _process = _processProvider.GetProcess(dapr.PId);
                    _process.Kill();
                }
            }
            if (options.DaprHttpPort != null)
                CheckPortAndKill(options.DaprHttpPort.Value);
            if (options.DaprGrpcPort != null)
                CheckPortAndKill(options.DaprGrpcPort.Value);
        }
    }

    /// <summary>
    /// Refresh the dapr configuration, the source dapr process will be killed and the new dapr process will be restarted
    /// todo: At present, there are no restrictions on HttpPort and GrpcPort, but if the configuration update changes HttpPort and GrpcPort, the port obtained by DaprClient will be inconsistent with the actual operation, which needs to be adjusted later.
    /// </summary>
    /// <param name="options"></param>
    /// <param name="cancellationToken"></param>
    public void Refresh(DaprOptions options, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _logger?.LogInformation("Dapr configuration refresh, appid is {appid}, please wait...", _successDaprOptions!.AppId);

            if (_successDaprOptions != null)
            {
                UpdateStatus(DaprProcessStatus.Restarting);
                _logger?.LogInformation("Dapr configuration refresh, appid is {appid}, closing dapr, please wait...",
                    _successDaprOptions!.AppId);
                StopCore(_successDaprOptions, cancellationToken);
            }

            _isFirst = true;
            _successDaprOptions = null;
            _process = null;
            _logger?.LogInformation("Dapr configuration refresh, appid is {appid}, restarting dapr, please wait...", options.AppId);
            StartCore(GetDaprOptions(options), cancellationToken);
        }
    }

    private void CheckPortAndKill(ushort port)
    {
        if (!_processProvider.IsAvailablePorts(port))
        {
            var pIdList = _processProvider.GetPidByPort(port);
            foreach (var pId in pIdList)
            {
                var process = _processProvider.GetProcess(pId);
                _logger?.LogWarning("Port {Port} is used, PId: {PId}, PName: {PName} , Process has been killed by {Name}",
                    port,
                    pId,
                    process.Name,
                    nameof(Masa.Utils.Development.Dapr));
                process.Kill();
            }
        }
    }

    private void HeartBeat(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (!_daprProvider.IsExist(_successDaprOptions!.AppId))
            {
                if (Status == DaprProcessStatus.Started || Status == DaprProcessStatus.Stopped)
                {
                    _logger?.LogWarning("Dapr stopped, restarting, please wait...");
                    StartCore(_successDaprOptions, cancellationToken);
                }
                else
                {
                    _logger?.LogWarning("Dapr is restarting, the current state is {State}, please wait...", Status);
                }
            }
        }
    }

    private DaprCoreOptions GetDaprOptions(DaprOptions options)
    {
        string appId = GetAppId(options);
        ushort appPort = GetAppPort(options);
        return new(
            appId,
            appPort,
            options.AppProtocol,
            options.EnableSsl,
            options.DaprGrpcPort,
            options.DaprHttpPort,
            options.EnableHeartBeat,
            options.HeartBeatInterval!.Value,
            options.CreateNoWindow)
        {
            MaxConcurrency = options.MaxConcurrency,
            Config = options.Config,
            ComponentPath = options.ComponentPath,
            EnableProfiling = options.EnableProfiling,
            Image = options.Image,
            LogLevel = options.LogLevel,
            PlacementHostAddress = options.PlacementHostAddress,
            SentryAddress = options.PlacementHostAddress,
            MetricsPort = options.MetricsPort,
            ProfilePort = options.ProfilePort,
            UnixDomainSocket = options.UnixDomainSocket,
            DaprMaxRequestSize = options.DaprMaxRequestSize
        };
    }

    private string GetAppId(DaprOptions options) =>
        options.AppIdSuffix.Trim() == string.Empty ? options.AppId : $"{options.AppId}{options.AppIdDelimiter}{options.AppIdSuffix}";

    private ushort GetAppPort(DaprOptions options) =>
        options.AppPort ?? throw new ArgumentNullException(nameof(options.AppPort));

    private CommandLineBuilder Initialize(DaprCoreOptions options, CancellationToken cancellationToken)
    {
        var commandLineBuilder = new CommandLineBuilder(Const.DEFAULT_ARGUMENT_PREFIX);
        commandLineBuilder
            .Add("app-id", options.AppId)
            .Add("app-port", options.AppPort.ToString())
            .Add("app-protocol", options.AppProtocol?.ToString().ToLower() ?? string.Empty, options.AppProtocol == null)
            .Add("app-ssl", options.EnableSsl?.ToString().ToLower() ?? "", options.EnableSsl == null)
            .Add("components-path", options.ComponentPath ?? string.Empty, options.ComponentPath == null)
            .Add("app-max-concurrency", options.MaxConcurrency?.ToString() ?? string.Empty, options.MaxConcurrency == null)
            .Add("config", options.Config ?? string.Empty, options.Config == null)
            .Add("dapr-grpc-port", options.DaprGrpcPort?.ToString() ?? string.Empty, options.DaprGrpcPort == null)
            .Add("dapr-http-port", options.DaprHttpPort?.ToString() ?? string.Empty, options.DaprHttpPort == null)
            .Add("enable-profiling", options.EnableProfiling?.ToString().ToLower() ?? string.Empty, options.EnableProfiling == null)
            .Add("image", options.Image ?? string.Empty, options.Image == null)
            .Add("log-level", options.LogLevel?.ToString().ToLower() ?? string.Empty, options.LogLevel == null)
            .Add("placement-host-address", options.PlacementHostAddress ?? string.Empty, options.PlacementHostAddress == null)
            .Add("sentry-address", options.SentryAddress ?? string.Empty, options.SentryAddress == null)
            .Add("metrics-port", options.MetricsPort?.ToString() ?? string.Empty, options.MetricsPort == null)
            .Add("profile-port", options.ProfilePort?.ToString() ?? string.Empty, options.ProfilePort == null)
            .Add("unix-domain-socket", options.UnixDomainSocket ?? string.Empty, options.UnixDomainSocket == null)
            .Add("dapr-http-max-request-size", options.DaprMaxRequestSize?.ToString() ?? string.Empty, options.DaprMaxRequestSize == null);

        _successDaprOptions ??= options;
        return commandLineBuilder;
    }

    /// <summary>
    /// Improve the information of HttpPort and GrpcPort successfully configured.
    /// When Port is specified or Dapr is closed for other reasons after startup, the HttpPort and GrpcPort are the same as the Port assigned at the first startup.
    /// </summary>
    private void CompleteDaprOptions()
    {
        int retry = 0;
        if (_successDaprOptions!.DaprHttpPort == null || _successDaprOptions.DaprGrpcPort == null)
        {
            again:
            var daprList = _daprProvider.GetDaprList(_successDaprOptions.AppId);
            if (daprList.Any())
            {
                var currentDapr = daprList.FirstOrDefault()!;
                _successDaprOptions.SetPort(currentDapr.HttpPort, currentDapr.GrpcPort);
            }
            else
            {
                if (retry < 3)
                {
                    retry++;
                    goto again;
                }
                _logger?.LogWarning("Dapr failed to start, appid is {Appid}", _successDaprOptions!.AppId);
                return;
            }
        }
        CompleteDaprEnvironment(_successDaprOptions.DaprHttpPort.ToString()!, _successDaprOptions.DaprGrpcPort!.ToString()!);
    }

    private void UpdateStatus(DaprProcessStatus status)
    {
        if (status != Status)
        {
            _logger?.LogInformation($"Dapr Process Status Change: {Status} -> {status}");
            Status = status;
        }
    }

    private static void CompleteDaprEnvironment(string daprHttpPort, string daprGrpcPort)
    {
        EnvironmentUtils.TryAdd("DAPR_GRPC_PORT", () => daprGrpcPort);
        EnvironmentUtils.TryAdd("DAPR_HTTP_PORT", () => daprHttpPort);
    }

    public void Dispose()
    {
        Stop();
    }
}
