namespace Masa.Utils.Development.Dapr;

public class DaprProvider : IDaprProvider
{
    private readonly ILogger<DaprProvider>? _logger;
    private ProcessUtils _processUtils;

    public DaprProvider(ILoggerFactory? loggerFactory)
    {
        _logger = loggerFactory?.CreateLogger<DaprProvider>();
        _processUtils = new ProcessUtils(loggerFactory);
    }

    public List<DaprRuntimeOptions> GetDaprList(string appId)
    {
        var stringBuilder = new StringBuilder();
        _processUtils.OutputDataReceived += delegate(object? sender, DataReceivedEventArgs args)
        {
            if (args.Data != null)
            {
                lock (stringBuilder)
                {
                    stringBuilder.AppendLine(args.Data);
                }
            }
        };
        _processUtils.Exit += delegate
        {
            _logger?.LogInformation("{Name} process has exited", Const.DEFAULT_FILE_NAME);
        };
        _processUtils.Run(Const.DEFAULT_FILE_NAME, "list -o json", true, true);
        string response = stringBuilder.ToString().Trim();
        List<DaprRuntimeOptions> daprList = new();
        try
        {
            if (response.StartsWith("["))
            {
                daprList = System.Text.Json.JsonSerializer.Deserialize<List<DaprRuntimeOptions>>(response) ?? new();
            }
            else if (response.StartsWith("{"))
            {
                var option = System.Text.Json.JsonSerializer.Deserialize<DaprRuntimeOptions>(response);
                if (option != null)
                {
                    daprList.Add(option);
                }
            }
            else
            {
                _logger?.LogWarning("----- Failed to get currently running dapr");
            }
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "----- Error getting list of running dapr, response message is {response}", response);
            return new List<DaprRuntimeOptions>();
        }
        return daprList.Where(dapr => dapr.AppId == appId).ToList();
    }

    public bool IsExist(string appId)
    {
        return GetDaprList(appId).Any();
    }
}
