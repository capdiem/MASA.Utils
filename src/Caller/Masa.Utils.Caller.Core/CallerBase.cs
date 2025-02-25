﻿namespace Masa.Utils.Caller.Core;

public abstract class CallerBase
{
    public abstract string Name { get; set; }

    protected CallerOptions CallerOptions { get; private set; } = default!;

    private ICallerProvider? _callerProvider;

    protected ICallerProvider CallerProvider => _callerProvider ??= ServiceProvider.GetRequiredService<ICallerFactory>().CreateClient(Name);

    private IServiceProvider ServiceProvider { get; }

    protected CallerBase(IServiceProvider serviceProvider)
    {
        ServiceProvider = serviceProvider;
    }

    public abstract void UseCallerExtension();

    public void SetCallerOptions(CallerOptions options)
    {
        CallerOptions = options;
    }
}
