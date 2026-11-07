<Query Kind="Program">
  <NuGetReference>Sdcb.HyMT2Sharp</NuGetReference>
  <NuGetReference>Sdcb.GgufWeights.Hy-MT2-1.8B-2Bit</NuGetReference>
  <Namespace>Sdcb.GgufWeights.Hy_MT2_1_8B_2Bit</Namespace>
  <Namespace>Sdcb.HyMT2Sharp.Backends</Namespace>
  <Namespace>Sdcb.HyMT2Sharp.Model</Namespace>
</Query>

// First run restores ~573 MB of 2Bit weights (3 NuGet packages), which can take a while.
void Main()
{
	// BackendFactory.Create() picks Metal/Vulkan when available, otherwise CPU (null).
	using HunyuanDenseModel model = new(Weights.Model(), backend: BackendFactory.Create());

	// Prompts follow the Hy-MT2 model card. Uncomment one to switch.
	// Use full language names: English names in English prompts, Chinese names in Chinese prompts.

	// 1. Default translation (English prompt)
	string prompt = """
		Translate the following text into Chinese. Note that you should only output the translated result without any additional explanation:

		SimdPaddleOCR is officially released today (NuGet: Sdcb.SimdPaddleOCR). It is a complete OCR inference engine written entirely in C#. It does not depend on Paddle Inference or ONNX Runtime, and it does not require shipping OpenCV native libraries.
		""";

	// 2. Default translation (Chinese prompt)
	// string prompt = """
	// 	将以下文本翻译为英语，注意只需要输出翻译后的结果，不要额外解释：
	//
	// 	今天天气真好，我们去公园散步吧。
	// 	""";

	// 3. Terminology
	// string prompt = """
	// 	参考下面的翻译：
	// 	混元 翻译成 Hunyuan
	// 	腾讯 翻译成 Tencent
	//
	// 	将以下文本翻译为英语，注意只需要输出翻译后的结果，不要额外解释：
	//
	// 	腾讯混元发布了新的翻译模型。
	// 	""";

	// 4. Style
	// string prompt = """
	// 	Please translate the following text into Chinese. Note that the translation style must strictly conform to [classical Chinese poetry]:
	//
	// 	The moon rises over the quiet lake, and the wind carries the scent of lotus.
	// 	""";

	// 5. Delimiters (keep the same number of separators in the output)
	// string prompt = """
	// 	Please accurately translate the following text into Japanese.
	// 	You must retain the exact same number of delimiters in the translation. Strictly do not omit, escape, or translate these symbols, and pay close attention to their placement.
	//
	// 	Open the file|Save changes|Close the window
	// 	""";

	// 6. Background information
	// string prompt = """
	// 	[Background Information]
	// 	This is a UI string from a photo editing app. "Crop" is a toolbar button.
	//
	// 	Please translate the following text into Chinese, taking the provided background information into consideration.
	//
	// 	[Source Text]
	// 	Crop
	// 	""";

	foreach (string piece in Generate(model, prompt))
		Console.Write(piece);
	Console.WriteLine();
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
