using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Mark2Worker;

// [JSExport] methods that run in the Web Worker.
// Call them from the Blazor app using WebWorkerClient.InvokeAsync.
// Example: await worker.InvokeAsync<string>("Mark2Worker.WorkerMethods.Recognize", [...]);

[SupportedOSPlatform("browser")]
public static partial class WorkerMethods
{
    // Recognizes the marks of one scanned image of the page given as JSON,
    // and returns the answers (and the log image if requested) as JSON of RecognitionResult.
    [JSExport]
    public static async Task<string> Recognize(byte[] imageBytes, string pageJson,
        double colorThreshold, double areaThreshold, bool withLogImage)
    {
        var page = JsonSerializer.Deserialize(pageJson, WorkerJsonContext.Default.Page)!;

        bool imageLoaded = true;
        var image = new Image<Rgba32>(595, 842);
        try
        {
            image = Image.Load<Rgba32>(imageBytes);
        }
        catch
        {
            imageLoaded = false;
        }

        Item item = new(page, colorThreshold, areaThreshold, image);
        RecognitionResult result = new();
        if (imageLoaded)
        {
            try
            {
                await item.Recognize();
                result.Answers = item.Answers;
                if (withLogImage)
                {
                    result.LogImage = item.LogImageBase64();
                }
            }
            catch
            {
                result.Answers = item.ErrorAnswers();
                if (withLogImage)
                {
                    result.LogImage = item.ErrorImageBase64();
                }
            }
        }
        else
        {
            result.Answers = item.ErrorAnswers();
            if (withLogImage)
            {
                result.LogImage = item.ErrorImageBase64();
            }
        }

        return JsonSerializer.Serialize(result, WorkerJsonContext.Default.RecognitionResult);
    }
}
