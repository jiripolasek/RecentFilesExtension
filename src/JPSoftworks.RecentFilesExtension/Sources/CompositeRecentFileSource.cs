// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.RecentFilesExtension.Helpers;
using JPSoftworks.RecentFilesExtension.Model;

namespace JPSoftworks.RecentFilesExtension.Sources;

internal sealed partial class CompositeRecentFileSource : IRecentFileSource
{
    private const int MaxRecentFileCount = 500;

    private readonly RecentFileSourceRegistration[] _registrations;
    private readonly IRecentFilesSettings? _settings;
    private bool _isDisposed;

    public CompositeRecentFileSource(IRecentFilesSettings settings)
        : this(
        new RecentFileSourceRegistration[]
        {
            new(new RecentItemsFolderSource(), static () => true),
            new(new OpenSaveMruSource(), () => settings.IncludeOpenSaveMru),
            new(new OfficeRecentFolderSource(), () => settings.IncludeOfficeRecentFiles),
            new(new OfficeJumpListSource(), () => settings.IncludeOfficeRecentFiles),
            new(new OfficeRecentFilesSource(), () => settings.IncludeOfficeRecentFiles),
        },
        settings)
    {
    }

    internal CompositeRecentFileSource(IEnumerable<RecentFileSourceRegistration> registrations)
        : this(registrations, null)
    {
    }

    internal CompositeRecentFileSource(
        IEnumerable<RecentFileSourceRegistration> registrations,
        IRecentFilesSettings? settings)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        this._registrations = [.. registrations];
        this._settings = settings;
        foreach (var registration in this._registrations)
        {
            registration.Source.Changed += this.OnSourceChanged;
        }

        if (this._settings is not null)
        {
            this._settings.SourcesChanged += this.OnSettingsChanged;
        }
    }

    public event EventHandler? Changed;

    public IReadOnlyList<IRecentFile> GetRecentFiles(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(this._isDisposed, this);

        var files = new List<IRecentFile>();
        foreach (var registration in this._registrations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!registration.IsEnabled())
            {
                continue;
            }

            try
            {
                files.AddRange(registration.Source.GetRecentFiles(cancellationToken));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to load a recent-files source ({registration.Source.GetType().Name})", ex);
            }
        }

        return files
            .OrderByDescending(static file => file.LastUsedTimeUtc)
            .DistinctBy(
                static file => RecentFilePath.GetDeduplicationKey(file.TargetPath),
                StringComparer.OrdinalIgnoreCase)
            .Take(MaxRecentFileCount)
            .ToArray();
    }

    private void OnSourceChanged(object? sender, EventArgs e)
    {
        if (this._isDisposed ||
            !this._registrations.Any(registration =>
                ReferenceEquals(registration.Source, sender) && registration.IsEnabled()))
        {
            return;
        }

        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        if (!this._isDisposed)
        {
            this.Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        if (this._isDisposed)
        {
            return;
        }

        this._isDisposed = true;
        if (this._settings is not null)
        {
            this._settings.SourcesChanged -= this.OnSettingsChanged;
        }

        foreach (var registration in this._registrations)
        {
            registration.Source.Changed -= this.OnSourceChanged;
            registration.Source.Dispose();
        }
    }
}