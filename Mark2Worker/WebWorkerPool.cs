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

    private WebWorkerPool(IReadOnlyList<WebWorkerClient> workers)
    {
        this.workers = workers;
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

    // Each worker starts its own .NET runtime, which takes time and memory
    public static async Task<WebWorkerPool> CreateAsync(IJSRuntime jsRuntime, int size, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);

        var tasks = Enumerable.Range(0, size)
            .Select(_ => WebWorkerClient.CreateAsync(jsRuntime, cancellationToken: cancellationToken))
            .ToList();

        try
        {
            return new WebWorkerPool(await Task.WhenAll(tasks));
        }
        catch
        {
            // Do not leave the workers that started running in the background
            foreach (var task in tasks.Where(t => t.IsCompletedSuccessfully))
            {
                await task.Result.DisposeAsync();
            }
            throw;
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
