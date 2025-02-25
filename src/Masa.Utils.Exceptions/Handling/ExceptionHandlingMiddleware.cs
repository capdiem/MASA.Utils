using Masa.Utils.Exceptions.Internal;

namespace Masa.Utils.Exceptions.Handling;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly MasaExceptionHandlingOptions _options;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IOptions<MasaExceptionHandlingOptions> options)
    {
        _next = next;
        _logger = logger;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext httpContext)
    {
        try
        {
            await _next(httpContext);
        }
        catch (Exception exception)
        {
            if (_options.CustomExceptionHandler is not null)
            {
                var handlerResult = _options.CustomExceptionHandler.Invoke(exception);

                if (handlerResult.ExceptionHandled) return;

                if (handlerResult.OverrideException is not null) exception = handlerResult.OverrideException;
            }
            if (exception is UserFriendlyException)
            {
                var message = exception.Message;
                _logger.LogError(exception, message);
                await httpContext.Response.WriteTextAsync((int)MasaHttpStatusCode.UserFriendlyException, message);
            }
            else if (exception is MasaException || _options.CatchAllException)
            {
                var message = "An error occur in masa framework";
                _logger.LogError(exception, message);
                await httpContext.Response.WriteTextAsync((int)HttpStatusCode.InternalServerError, message);
            }
            else
            {
                throw;
            }
        }
    }
}
