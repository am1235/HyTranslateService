<Query Kind="Program">
  <NuGetReference>Sdcb.HyMT2Sharp</NuGetReference>
  <NuGetReference>Sdcb.GgufWeights.Hy-MT2-1.8B-2Bit</NuGetReference>
  <Namespace>Microsoft.AspNetCore.Builder</Namespace>
  <Namespace>Microsoft.AspNetCore.Http</Namespace>
  <Namespace>Microsoft.Extensions.Hosting</Namespace>
  <Namespace>Microsoft.Extensions.Logging</Namespace>
  <Namespace>Sdcb.GgufWeights.Hy_MT2_1_8B_2Bit</Namespace>
  <Namespace>Sdcb.HyMT2Sharp.Backends</Namespace>
  <Namespace>Sdcb.HyMT2Sharp.Model</Namespace>
  <Namespace>System.Net.Http</Namespace>
  <Namespace>System.Net.Http.Json</Namespace>
  <Namespace>System.Threading.Tasks</Namespace>
  <IncludeAspNet>true</IncludeAspNet>
</Query>

// ASP.NET Core minimal API translator. Stop the query to shut the server down.
//   POST /translate          {"text":"...","to":"Chinese"}  -> {"text":"..."}
//   GET  /translate/stream?text=...&to=Japanese              -> text/plain, streamed as it is generated
// First run restores ~573 MB of 2Bit weights (3 NuGet packages), which can take a while.
const string Url = "http://127.0.0.1:5089";

async Task Main()
{
	using HunyuanDenseModel model = new(Weights.Model(), backend: BackendFactory.Create());
	// HunyuanDenseModel is not thread-safe; serialize requests.
	SemaphoreSlim gate = new(1, 1);

	// LINQPad has no entry assembly; point ApplicationName at the query assembly so hosting startup does not fail.
	WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName = GetType().Assembly.GetName().Name });
	builder.Logging.SetMinimumLevel(LogLevel.Warning);
	WebApplication app = builder.Build();
	app.Urls.Add(Url);

	app.MapPost("/translate", async (TranslateRequest request, CancellationToken ct) =>
	{
		await gate.WaitAsync(ct);
		try
		{
			string text = string.Concat(Generate(model, Prompt(request.Text, request.To ?? "Chinese")));
			return Results.Ok(new TranslateResponse(text));
		}
		finally
		{
			gate.Release();
		}
	});

	app.MapGet("/translate/stream", async (string text, string? to, HttpResponse response, CancellationToken ct) =>
	{
		response.ContentType = "text/plain; charset=utf-8";
		await gate.WaitAsync(ct);
		try
		{
			foreach (string piece in Generate(model, Prompt(text, to ?? "Chinese")))
			{
				ct.ThrowIfCancellationRequested();
				await response.WriteAsync(piece, ct);
				await response.Body.FlushAsync(ct);
			}
		}
		finally
		{
			gate.Release();
		}
	});

	await app.StartAsync();
	Console.WriteLine($"listening {Url}");

	// Call our own API once so the query shows a result.
	using HttpClient http = new() { BaseAddress = new Uri(Url) };
	HttpResponseMessage reply = await http.PostAsJsonAsync("/translate", new TranslateRequest("The weather is lovely today, let's take a walk in the park.", "Chinese"));
	Console.WriteLine(await reply.Content.ReadFromJsonAsync<TranslateResponse>());
	Console.WriteLine($"try: curl \"{Url}/translate/stream?text=Hello%20world&to=Japanese\"");

	await app.WaitForShutdownAsync(QueryCancelToken);
}

static string Prompt(string text, string to) => $"""
	Translate the following text into {to}. Note that you should only output the translated result without any additional explanation:

	{text}
	""";

// Greedy decoding; yields text as it becomes visible.
static IEnumerable<string> Generate(HunyuanDenseModel model, string user, int maxTokens = 512)
{
	// Raw string literals keep the file's line endings (CRLF on Windows); the model expects LF.
	int[] prompt = model.Tokenizer.Encode(ChatTemplate.RenderHunyuanDense([new ChatMessage("user", user.ReplaceLineEndings("\n"))]));
	float[] logits = model.Forward(model.AlignPrompt(prompt).Suffix);

	List<int> generated = [];
	string visible = "";
	for (int i = 0; i < maxTokens; i++)
	{
		int token = Sampler.ArgMax(logits);
		if (model.Tokenizer.IsStop(token))
			break;

		generated.Add(token);
		// A trailing U+FFFD is an incomplete UTF-8 sequence; wait for the next token.
		string next = model.Tokenizer.DecodeVisible(generated).TrimEnd('\uFFFD');
		if (next.Length > visible.Length && next.StartsWith(visible, StringComparison.Ordinal))
		{
			yield return next[visible.Length..];
			visible = next;
		}

		logits = model.Forward([token]);
	}
}

public record TranslateRequest(string Text, string? To);
public record TranslateResponse(string Text);
