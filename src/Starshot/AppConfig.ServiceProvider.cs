using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Starshot.Features.Screenshot;

namespace Starshot;

public static partial class AppConfig
{
    private static IServiceProvider _serviceProvider;

    private static void BuildServiceProvider()
    {
        if (_serviceProvider == null)
        {
            var minLevel = AppConfig.LogLevel switch
            {
                1 => Serilog.Events.LogEventLevel.Error,
                2 => Serilog.Events.LogEventLevel.Warning,
                4 => Serilog.Events.LogEventLevel.Debug,
                _ => Serilog.Events.LogEventLevel.Information,
            };
            var cfg = new LoggerConfiguration().Enrich.FromLogContext().MinimumLevel.Is(minLevel);
            if (AppConfig.LogLevel != 0)
            {
                cfg.WriteTo.File(
                    path: LogFile,
                    shared: true,
                    outputTemplate: $$"""[{Timestamp:HH:mm:ss.fff}] [{Level:u4}] [{{Environment.ProcessId}}] {SourceContext}{NewLine}{Message}{NewLine}{Exception}{NewLine}"""
                );
            }
            Log.Logger = cfg.CreateLogger();
            Log.Information(
                $"Welcome to Starshot v{AppVersion}\r\nRuntime: {Environment.Version}\r\nCommand Line: {Environment.CommandLine}"
            );

            var sc = new ServiceCollection();
            sc.AddMemoryCache();
            sc.AddLogging(c => c.AddSerilog(Log.Logger));

            sc.AddSingleton<ScreenCaptureService>();

            _serviceProvider = sc.BuildServiceProvider();
        }
    }

    public static T GetService<T>()
    {
        BuildServiceProvider();
        return _serviceProvider.GetService<T>()!;
    }

    public static ILogger<T> GetLogger<T>()
    {
        BuildServiceProvider();
        return _serviceProvider.GetService<ILogger<T>>()!;
    }
}
