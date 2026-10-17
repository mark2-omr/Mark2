namespace Mark2Worker;

using System.Text.Json.Serialization;

// Only strings and primitives cross the boundary of the worker, so the models are passed as JSON.
// Source generation keeps the serialization working with trimming and AOT.
[JsonSerializable(typeof(Page))]
[JsonSerializable(typeof(RecognitionResult))]
public partial class WorkerJsonContext : JsonSerializerContext
{
}
