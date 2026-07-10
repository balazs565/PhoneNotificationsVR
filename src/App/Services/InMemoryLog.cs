using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Extensions.Logging;

namespace PhoneNotificationsVR.App.Services;

/// <summary>A single log line for the in-app Log window.</summary>
public sealed record LogEntry(DateTimeOffset Time, LogLevel Level, string Category, string Message)
{
    public string Display => $"{Time.LocalDateTime:HH:mm:ss} [{Level.ToString().ToUpperInvariant()[..3]}] {Category}: {Message}";
}

/// <summary>
/// In-memory log sink that both the Log window binds to and (optionally) the debugger receives.
/// Registered as an <see cref="ILoggerProvider"/> so every component's logs land here.
/// </summary>
public sealed class InMemoryLogStore
{
    private readonly ObservableCollection<LogEntry> _entries = new();
    public ReadOnlyObservableCollection<LogEntry> Entries { get; }
    public int Capacity { get; set; } = 1000;

    public InMemoryLogStore() => Entries = new ReadOnlyObservableCollection<LogEntry>(_entries);

    public void Add(LogEntry entry)
    {
        var dispatcher = Application.Current?.Dispatcher;
        void Do()
        {
            _entries.Add(entry);
            while (_entries.Count > Capacity) _entries.RemoveAt(0);
        }
        if (dispatcher is null || dispatcher.CheckAccess()) Do();
        else dispatcher.BeginInvoke(Do);
    }

    public void Clear()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) _entries.Clear();
        else dispatcher.BeginInvoke(_entries.Clear);
    }
}

public sealed class InMemoryLoggerProvider : ILoggerProvider
{
    private readonly InMemoryLogStore _store;
    public InMemoryLoggerProvider(InMemoryLogStore store) => _store = store;
    public ILogger CreateLogger(string categoryName) => new InMemoryLogger(_store, categoryName);
    public void Dispose() { }

    private sealed class InMemoryLogger : ILogger
    {
        private readonly InMemoryLogStore _store;
        private readonly string _category;
        public InMemoryLogger(InMemoryLogStore store, string category)
        {
            _store = store;
            // Shorten "PhoneNotificationsVR.Ancs.AncsNotificationSource" → "AncsNotificationSource".
            var dot = category.LastIndexOf('.');
            _category = dot >= 0 ? category[(dot + 1)..] : category;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var message = formatter(state, exception);
            if (exception is not null) message += $" — {exception.Message}";
            _store.Add(new LogEntry(DateTimeOffset.Now, logLevel, _category, message));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
