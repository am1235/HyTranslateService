<Query Kind="Program">
  <NuGetReference>Sdcb.HyMT2Sharp</NuGetReference>
  <NuGetReference>Sdcb.GgufWeights.Hy-MT2-1.8B-2Bit</NuGetReference>
  <Namespace>Sdcb.GgufWeights.Hy_MT2_1_8B_2Bit</Namespace>
  <Namespace>Sdcb.HyMT2Sharp.Backends</Namespace>
  <Namespace>Sdcb.HyMT2Sharp.Model</Namespace>
  <Namespace>System.Diagnostics</Namespace>
</Query>

// Interactive translator: each line you type is translated into the target language.
// Commands: /to <language>  switch target language (e.g. /to Japanese)
//           /exit           quit (an empty line also quits)
// First run restores ~573 MB of 2Bit weights (3 NuGet packages), which can take a while.
void Main()
{
	// BackendFactory.Create() picks Metal/Vulkan when available, otherwise CPU (null).
	IComputeBackend? backend = BackendFactory.Create();
	using HunyuanDenseModel model = new(Weights.Model(), backend: backend);
	string target = "Chinese";
	Console.WriteLine($"backend={backend?.Name ?? "cpu"}  threads={model.ThreadCount}  target={target}");

	while (true)
	{
		string? line = Console.ReadLine()?.Trim();
		if (string.IsNullOrEmpty(line) || line == "/exit")
			break;

		if (line.StartsWith("/to ", StringComparison.OrdinalIgnoreCase))
		{
			target = line[4..].Trim();
			Console.WriteLine($"target={target}");
			continue;
		}

		string prompt = $"""
			Translate the following text into {target}. Note that you should only output the translated result without any additional explanation:

			{line}
			""";

		Console.WriteLine($"> {line}");
		Stopwatch sw = Stopwatch.StartNew();
		foreach (string piece in Generate(model, prompt))
			Console.Write(piece);
		Console.WriteLine($"  ({sw.Elapsed.TotalSeconds:F2}s)");
	}
}

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
