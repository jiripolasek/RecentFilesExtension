// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Runtime.InteropServices.WindowsRuntime;
using JPSoftworks.RecentFilesExtension.Helpers;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;

namespace JPSoftworks.RecentFilesExtension.Pages;

internal sealed partial class RecentFileThumbnail(string path, string? fallbackPath, uint size)
    : IRandomAccessStreamReference
{
    private static readonly SemaphoreSlim LoadSlots = new(4);

    public IAsyncOperation<IRandomAccessStreamWithContentType> OpenReadAsync() =>
        AsyncInfo.Run<IRandomAccessStreamWithContentType>(this.OpenThumbnailAsync);

    private async Task<IRandomAccessStreamWithContentType> OpenThumbnailAsync(CancellationToken cancellationToken)
    {
        await LoadSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var thumbnail = await this.TryOpenAsync(path, cancellationToken).ConfigureAwait(false);
            if (thumbnail is null && fallbackPath is not null)
            {
                thumbnail = await this.TryOpenAsync(fallbackPath, cancellationToken).ConfigureAwait(false);
            }

            return thumbnail ?? throw new FileNotFoundException("The recent file has no thumbnail.");
        }
        finally
        {
            LoadSlots.Release();
        }
    }

    private async Task<IRandomAccessStreamWithContentType?> TryOpenAsync(string itemPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            IStorageItemProperties item = Directory.Exists(itemPath)
                ? await StorageFolder.GetFolderFromPathAsync(itemPath).AsTask(cancellationToken).ConfigureAwait(false)
                : await StorageFile.GetFileFromPathAsync(itemPath).AsTask(cancellationToken).ConfigureAwait(false);
            var thumbnail = await item.GetThumbnailAsync(ThumbnailMode.SingleItem, size, ThumbnailOptions.ResizeThumbnail)
                .AsTask(cancellationToken).ConfigureAwait(false);
            if (thumbnail is not null)
            {
                return thumbnail;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            Logger.LogWarning($"Failed to get the thumbnail {ExceptionDiagnostics.Summarize(ex)}");
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var icon = await ThumbnailHelper.GetThumbnail(itemPath, jumbo: true).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return icon is null ? null : await RandomAccessStreamReference.CreateFromStream(icon).OpenReadAsync()
                .AsTask(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            Logger.LogWarning($"Failed to get the icon {ExceptionDiagnostics.Summarize(ex)}");
            return null;
        }
    }
}
