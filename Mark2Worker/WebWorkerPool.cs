using Microsoft.JSInterop;

namespace Mark2Worker;

// Runs [JSExport] methods on several web workers in parallel.
// Each call takes an idle worker, and waits for one when all of them are busy.
public sealed class WebWorkerPool : IAsyncDisposable
{
    // Blazor WebAssembly runs .NET on a single thread, so the queue needs no lock
    private readonly IReadOnlyList<WebWorkerClient> workers;
    private readonly Queue<WebWorkerClient> idleWorkers;
    private readonly SemaphoreSlim available;

    public int Size => workers.Count;

    // The number of workers asked for when the pool was created, which Size can be less than
    public int RequestedSize { get; }

    private WebWorkerPool(IReadOnlyList<WebWorkerClient> workers, int requestedSize)
    {
        this.workers = workers;
        RequestedSize = requestedSize;
        idleWorkers = new(workers);
        available = new(workers.Count, workers.Count);
    }

    // The number of logical processors of the device (navigator.hardwareConcurrency)
    public static async Task<int> GetHardwareConcurrencyAsync(IJSRuntime jsRuntime, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var module = await jsRuntime.InvokeAsync<IJSObjectReference>(
                "import", cancellationToken, "./_content/Mark2Worker/dotnet-web-worker-client.js");
            return await module.InvokeAsync<int>("hardwareConcurrency", cancellationToken);
        }
        catch (JSException ex)
        {
            throw new InvalidOperationException($"Unable to get the number of logical processors: {ex.Message}", ex);
        }
    }

    // Each worker starts its own .NET runtime, which takes time and memory.
    // The pool may have fewer workers than the size when some of them fail to start.
    public static async Task<WebWorkerPool> CreateAsync(IJSRuntime jsRuntime, int size, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);

        // The first worker starts alone, so that the runtime files are in the cache before the
        // others download them all at once. When it cannot start, the others would fail the same way.
        var first = await CreateWorkerAsync(jsRuntime, cancellationToken);

        // Each of the others downloads dotnet.native.wasm (tens of MB with AOT) at the same time,
        // which can fail with "Failed to fetch" on devices with many processors. Leave out the
        // workers that still fail after retrying instead of failing the whole pool.
        var tasks = Enumerable.Range(1, size - 1)
            .Select(_ => TryCreateWorkerAsync(jsRuntime, cancellationToken))
            .ToList();

        try
        {
            await Task.WhenAll(tasks);
        }
        catch
        {
            // Do not leave the workers that started running in the background
            foreach (var worker in tasks.Where(t => t.IsCompletedSuccessfully).Select(t => t.Result).Prepend(first))
            {
                if (worker != null)
                {
                    await worker.DisposeAsync();
                }
            }
            throw;
        }

        return new WebWorkerPool([first, .. tasks.Select(t => t.Result).OfType<WebWorkerClient>()], size);
    }

    private static async Task<WebWorkerClient?> TryCreateWorkerAsync(IJSRuntime jsRuntime, CancellationToken cancellationToken)
    {
        try
        {
            return await CreateWorkerAsync(jsRuntime, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"A worker failed to start and is left out of the pool: {ex.Message}");
            return null;
        }
    }

    // The runtime retries a failed download only within about 100 ms, so retry the whole startup
    // after waiting long enough for a temporary network error or lack of resources to go away
    private static async Task<WebWorkerClient> CreateWorkerAsync(IJSRuntime jsRuntime, CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await WebWorkerClient.CreateAsync(jsRuntime, cancellationToken: cancellationToken);
            }
            catch (InvalidOperationException) when (attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
            }
        }
    }

    public async Task<TResult> InvokeAsync<TResult>(string method, object[] args, CancellationToken cancellationToken = default)
    {
        await available.WaitAsync(cancellationToken);
        var worker = idleWorkers.Dequeue();
        try
        {
            return await worker.InvokeAsync<TResult>(method, args, cancellationToken: cancellationToken);
        }
        finally
        {
            idleWorkers.Enqueue(worker);
            available.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var worker in workers)
        {
            await worker.DisposeAsync();
        }
        available.Dispose();
    }
}
