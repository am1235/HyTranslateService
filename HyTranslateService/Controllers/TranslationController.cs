using System.Globalization;
using System.Text;
using HyTranslateService.Translation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace HyTranslateService.Controllers;

/// <summary>
/// 翻译控制器：目标语言清单、一步到位的同步翻译、大文本文件上传翻译与结果下载、
/// 「当前任务」查询与停止。系统只维护一个当前任务：忙时翻译请求返回 409（响应体带 currentJobId）。
/// 流式翻译走 SignalR（/signalr/translation）。
/// 大小上限统一见 AppOptions（MaxTextBytes / MaxFileUploadBytes），由命令行或环境变量覆盖。
/// </summary>
[Route("/api/[Controller]")]
[ApiController]
public sealed class TranslationController : ControllerBase
{
    /// <summary>翻译任务管理器：全局唯一的当前任务入口（受理、查询、停止）。</summary>
    private readonly TranslationJobManager _jobs;

    /// <summary>应用配置：提供内联文本与文件上传的大小上限。</summary>
    private readonly AppOptions _options;

    /// <summary>日志记录器。</summary>
    private readonly ILogger<TranslationController> _logger;

    /// <summary>构造函数，注入任务管理器、配置与日志。</summary>
    /// <param name="jobs">翻译任务管理器，受理并跟踪当前任务。</param>
    /// <param name="options">应用配置，提供 MaxTextBytes / MaxFileUploadBytes 等上限。</param>
    /// <param name="logger">日志记录器。</param>
    public TranslationController(TranslationJobManager jobs, AppOptions options, ILogger<TranslationController> logger)
    {
        _jobs = jobs;
        _options = options;
        _logger = logger;
    }

    /// <summary>列出受支持的翻译目标语言（38 种）。</summary>
    /// <returns>语言清单（code/id/englishName/chineseName/nativeName/displayName）。</returns>
    /// <response code="200">返回目标语言清单。</response>
    /// <remarks>
    /// Sample request:
    ///
    ///     GET /api/translation/languages
    ///
    /// Sample response:
    ///     [
    ///        {
    ///           "code": "zh",
    ///           "id": 1,
    ///           "englishName": "Chinese Simplified",
    ///           "chineseName": "简体中文",
    ///           "nativeName": "中文",
    ///           "displayName": "Chinese Simplified"
    ///        }
    ///     ]
    /// </remarks>
    [HttpGet("languages")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Languages() => Ok(HyMT2Languages.All.Select(d => new
    {
        code = d.Code,
        id = d.Id,
        englishName = d.EnglishName,
        chineseName = d.ChineseName,
        nativeName = d.NativeName,
        displayName = d.EnglishName,
    }));

    /// <summary>
    /// 一步到位的翻译：等待整篇完成再返回译文（用于一段文字翻译）。
    /// </summary>
    /// <param name="body">翻译请求（text 必填，其余可选，字段宽松写法由后端归一化）。</param>
    /// <returns>200 返回完整译文与任务统计；400 返回 error；409 返回占用者 currentJobId。</returns>
    /// <response code="200">返回译文与任务统计。</response>
    /// <response code="400">文本为空、超限或请求参数无效。</response>
    /// <response code="409">模型正忙（响应体带 currentJobId）。</response>
    /// <remarks>
    /// Sample request:
    ///
    ///     POST /api/translation
    ///     {
    ///        "text": "Hello, world!",
    ///        "targetLanguage": "zh",
    ///        "model": "auto",
    ///        "engine": "auto",
    ///        "kvCache": "memory"
    ///     }
    ///
    /// Sample response:
    ///     {
    ///        "jobId": "a1b2c3",
    ///        "status": "Completed",
    ///        "model": "hymt2-7b-q4",
    ///        "engine": "auto",
    ///        "kvCache": "memory",
    ///        "sourceLanguage": "en",
    ///        "targetLanguage": "zh",
    ///        "totalSegments": 1,
    ///        "completedSegments": 1,
    ///        "sourceChars": 13,
    ///        "elapsedMs": 820,
    ///        "translation": "你好，世界！",
    ///        "error": null
    ///     }
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Translate([FromBody] TranslationApiRequest body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Text))
        {
            _logger.LogWarning("同步翻译被拒绝：待翻译内容为空");
            return BadRequest(new { error = "待翻译内容不能为空。" });
        }

        // 内联文本入口的大小闸（接口层收口）：核心层不校验大小，文件入口的大小上限由 AppOptions 定义
        int textBytes = Encoding.UTF8.GetByteCount(body.Text);
        if (textBytes > _options.MaxTextBytes)
        {
            _logger.LogWarning("同步翻译被拒绝：文本 {Actual} 字节超过上限 {Limit}", textBytes, _options.MaxTextBytes);
            return BadRequest(new
            {
                error = $"文本过大（{textBytes / 1024.0:F1} KB），单次上限为 {_options.MaxTextBytes / 1024} KB，请拆分后分批翻译。",
            });
        }

        var outcome = _jobs.TryStart(body.ToTranslationRequest() with { Kind = TranslationJobKinds.Inline });
        if (outcome.Busy is not null)
        {
            _logger.LogWarning("同步翻译被拒绝（模型正忙）：{Reason}", outcome.Busy.Message);
            return BusyResult(outcome.Busy);
        }
        if (outcome.Invalid is not null)
        {
            _logger.LogWarning("同步翻译请求无效：{Reason}", outcome.Invalid.Message);
            return BadRequest(new { error = outcome.Invalid.Message });
        }
        var job = outcome.Job!;

        var planned = job.Snapshot();
        _logger.LogInformation("同步翻译已受理 {JobId}：{Chars} 字符 → {Target}，{Segments} 段（模型 {Model}）",
            job.Id, body.Text.Length, planned.TargetLanguage, planned.TotalSegments, planned.Model);

        // 客户端断开时同步终止翻译
        using var registration = cancellationToken.Register(() => job.Cancel());

        try
        {
            await foreach (var evt in job.SubscribeAsync(cancellationToken).ConfigureAwait(false))
            {
                if (evt.Type is JobEventTypes.Done or JobEventTypes.Error or JobEventTypes.Cancelled) break;
            }
        }
        catch (OperationCanceledException)
        {
            job.Cancel();
            _logger.LogInformation("同步翻译请求被客户端取消：{JobId}", job.Id);
            return StatusCode(499, new { error = "客户端已取消请求。" });
        }

        var snapshot = job.Snapshot();
        if (string.Equals(snapshot.Status, nameof(TranslationJobStatus.Failed), StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("同步翻译失败 {JobId}：{Error}", job.Id, snapshot.Error ?? "未知错误");
            return BadRequest(new { error = snapshot.Error ?? "翻译失败。", jobId = snapshot.JobId });
        }

        _logger.LogInformation("同步翻译完成 {JobId}：{Completed}/{Total} 段，耗时 {Elapsed:0.0} 秒",
            job.Id, snapshot.CompletedSegments, snapshot.TotalSegments, snapshot.ElapsedMs / 1000);

        return Ok(new
        {
            jobId = snapshot.JobId,
            status = snapshot.Status,
            model = snapshot.Model,
            engine = snapshot.Engine,
            kvCache = snapshot.KvCache,
            sourceLanguage = snapshot.SourceLanguage,
            targetLanguage = snapshot.TargetLanguage,
            totalSegments = snapshot.TotalSegments,
            completedSegments = snapshot.CompletedSegments,
            sourceChars = snapshot.SourceChars,
            elapsedMs = snapshot.ElapsedMs,
            translation = snapshot.Translation,
            error = snapshot.Error,
        });
    }

    /// <summary>
    /// 大文本文件上传翻译（multipart/form-data，字段名 <c>file</c>；可选字段同 POST /api/translation）。
    /// 手工流式解析 multipart（不依赖通用解析器，行为完全可控）。
    /// 立即返回 jobId；进度用 GET /api/translation/current 轮询，结束后 GET /api/translation/file-result 下载译文。
    /// 大小上限见 AppOptions.MaxFileUploadBytes。
    /// </summary>
    /// <returns>200 返回 jobId 与任务概要；400 返回 error；409 返回占用者 currentJobId。</returns>
    /// <response code="200">返回 jobId 与任务概要（进度用 current 轮询）。</response>
    /// <response code="400">Content-Type/边界无效、缺少文件段或文件过大。</response>
    /// <response code="409">模型正忙（响应体带 currentJobId）。</response>
    /// <remarks>
    /// Sample request:
    ///
    ///     POST /api/translation/file
    ///     Content-Type: multipart/form-data; boundary=----boundary
    ///     （文件字段名 file；可选表单字段同 POST /api/translation）
    ///
    /// Sample response:
    ///     {
    ///        "jobId": "a1b2c3",
    ///        "status": "Pending",
    ///        "model": "hymt2-7b-q4",
    ///        "targetLanguage": "zh",
    ///        "sourceChars": 150000,
    ///        "totalSegments": 130
    ///     }
    /// </remarks>
    [HttpPost("file")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> TranslateFileUpload(CancellationToken cancellationToken)
    {
        if (Request.ContentType is null || !Request.ContentType.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "请使用 multipart/form-data 上传，文件字段名为 file。" });

        string? boundary = GetMultipartBoundary(Request.ContentType);
        if (string.IsNullOrWhiteSpace(boundary))
            return BadRequest(new { error = "multipart 边界无效。" });

        // 请求体整体读入内存后解析；不用 MultipartReader：直接按 boundary 切分内存缓冲，行为完全可控。
        using var bodyBuffer = new MemoryStream();
        await Request.Body.CopyToAsync(bodyBuffer, cancellationToken).ConfigureAwait(false);
        if (bodyBuffer.Length > _options.MaxFileUploadBytes + 1024 * 1024)
            return BadRequest(new { error = $"上传过大（{bodyBuffer.Length / 1024 / 1024} MB），文件上限 {_options.MaxFileUploadBytes / 1024 / 1024} MB。" });

        string? fileText = null;
        string? fileName = null;
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 直接复用 MemoryStream 的内部缓冲（new MemoryStream() 的缓冲可公开访问），
        // 传入有效数据长度，避免 ToArray() 再整体拷贝一遍。
        foreach (var part in SplitMultipart(bodyBuffer.GetBuffer(), (int)bodyBuffer.Length, boundary!))
        {
            if (string.IsNullOrEmpty(part.FileName))
            {
                // 表单字段（targetLanguage / sourceLanguage / model / glossary …），单个字段限 64KB 防滥用
                var value = Encoding.UTF8.GetString(part.Body);
                if (!string.IsNullOrEmpty(part.Name) && value.Length <= 64 * 1024)
                    fields[part.Name.Trim()] = value;
                continue;
            }

            // 只保留第一个文件段
            fileName ??= part.FileName;
            if (fileText is not null) continue;
            if (part.Body.Length > _options.MaxFileUploadBytes)
                return BadRequest(new { error = $"文件过大（{part.Body.Length / 1024 / 1024} MB），上限 {_options.MaxFileUploadBytes / 1024 / 1024} MB。" });
            fileText = Encoding.UTF8.GetString(part.Body).TrimStart('\ufeff');
        }

        if (fileText is null)
            return BadRequest(new { error = "缺少文件段：请以字段名 file 上传文本文件。" });
        if (string.IsNullOrWhiteSpace(fileText))
            return BadRequest(new { error = "文件内容为空，无需翻译。" });

        string? AsString(string key) => fields.TryGetValue(key, out var v) && v.Trim().Length > 0 ? v.Trim() : null;
        int? AsInt(string key) => int.TryParse(AsString(key), out var n) ? n : null;
        float? AsFloat(string key) => float.TryParse(AsString(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : null;

        var outcome = _jobs.TryStart(new TranslationRequest
        {
            Text = fileText,
            Kind = TranslationJobKinds.File,
            TargetLanguage = AsString("targetLanguage"),
            SourceLanguage = AsString("sourceLanguage"),
            Model = AsString("model"),
            SegmentChars = AsInt("segmentChars"),
            MaxTokensPerSegment = AsInt("maxTokens"),
            Temperature = AsFloat("temperature"),
            TopP = AsFloat("topP"),
            TopK = AsInt("topK"),
            RepetitionPenalty = AsFloat("repetitionPenalty"),
            PromptStyle = AsString("promptStyle"),
            PromptLanguage = AsString("promptLanguage"),
            Glossary = AsString("glossary"),
            Background = AsString("background"),
            TargetStyle = AsString("targetStyle"),
            Engine = AsString("engine"),
            KvCache = AsString("kvCache"),
            SourceName = string.IsNullOrWhiteSpace(fileName) ? null : fileName,
        });
        if (outcome.Busy is not null)
        {
            _logger.LogWarning("文件上传翻译被拒绝（模型正忙）：{Reason}", outcome.Busy.Message);
            return BusyResult(outcome.Busy);
        }
        if (outcome.Invalid is not null)
        {
            _logger.LogWarning("文件上传翻译请求无效：{Reason}", outcome.Invalid.Message);
            return BadRequest(new { error = outcome.Invalid.Message });
        }
        var job = outcome.Job!;

        var planned = job.Snapshot();
        _logger.LogInformation("文件上传翻译已受理 {JobId}：{Chars} 字符 → {Target}，{Segments} 段（文件 {Name}，模型 {Model}）",
            job.Id, planned.SourceChars, planned.TargetLanguage, planned.TotalSegments, fileName ?? "(未命名)", planned.Model);

        return Ok(new
        {
            jobId = job.Id,
            status = planned.Status,
            model = planned.Model,
            targetLanguage = planned.TargetLanguage,
            sourceChars = planned.SourceChars,
            totalSegments = planned.TotalSegments,
        });
    }

    /// <summary>
    /// 下载已完成任务的译文（仅保留最近一次结束的任务）。
    /// 任务进行中返回 409；没有该任务的结果返回 404；失败任务返回 409（带 error）。
    /// </summary>
    /// <param name="job">要下载译文的任务 ID，必填。</param>
    /// <returns>200 返回译文全文（text/plain 附件下载）；400/404/409 返回 error 或提示。</returns>
    /// <response code="200">返回译文全文（Content-Disposition: attachment）。</response>
    /// <response code="400">缺少 job 参数。</response>
    /// <response code="404">没有该任务的译文结果。</response>
    /// <response code="409">任务尚未结束，或任务已失败（带 error）。</response>
    /// <remarks>
    /// Sample request:
    ///
    ///     GET /api/translation/file-result?job=a1b2c3
    ///
    /// Sample response:
    ///     text/plain（译文全文，文件名形如 原文件名.目标语言.txt）
    /// </remarks>
    [HttpGet("file-result")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public IActionResult FileResult([FromQuery] string? job)
    {
        if (string.IsNullOrWhiteSpace(job))
            return BadRequest(new { error = "缺少 job 参数。" });

        var snapshot = _jobs.TryGetFinishedSnapshot(job);
        if (snapshot is null)
        {
            var current = _jobs.GetCurrentSnapshot();
            if (current is not null && current.JobId == job)
                return StatusCode(StatusCodes.Status409Conflict, new
                {
                    busy = true,
                    status = current.Status,
                    hint = "任务尚未结束：请继续用 GET /api/translation/current 查询，结束后再下载。",
                });
            return NotFound(new { error = "没有该任务的译文结果（结果只保留最近一次完成的任务）。" });
        }

        if (string.Equals(snapshot.Status, nameof(TranslationJobStatus.Failed), StringComparison.OrdinalIgnoreCase))
            return StatusCode(StatusCodes.Status409Conflict, new { status = snapshot.Status, error = snapshot.Error ?? "翻译失败。" });

        var fileName = BuildResultFileName(snapshot);
        Response.Headers[HeaderNames.ContentDisposition] =
            "attachment; filename*=UTF-8''" + Uri.EscapeDataString(fileName);
        Response.Headers["X-Translation-Status"] = snapshot.Status;
        _logger.LogInformation("文件翻译结果已下载 {JobId}：{File}（{Chars} 字符）", job, fileName, snapshot.Translation.Length);
        return Content(snapshot.Translation ?? string.Empty, "text/plain; charset=utf-8");
    }

    /// <summary>结果文件名：原文件名（去扩展名）.目标语言.txt；无来源名时用 translation。</summary>
    /// <param name="snapshot">已完成任务的快照，取其中的来源文件名与目标语言。</param>
    /// <returns>下载用的文件名，如 sample.zh.txt。</returns>
    private static string BuildResultFileName(TranslationJobSnapshot snapshot)
    {
        var baseName = string.IsNullOrWhiteSpace(snapshot.SourceName)
            ? "translation"
            : Path.GetFileNameWithoutExtension(snapshot.SourceName);
        return $"{baseName}.{snapshot.TargetLanguage}.txt";
    }

    /// <summary>从 Content-Type 里取 multipart boundary（带引号则去引号）。</summary>
    /// <param name="contentType">请求头 Content-Type 原始值，可为 null。</param>
    /// <returns>boundary 值；无法解析或缺失时为 null。</returns>
    private static string? GetMultipartBoundary(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType) ||
            !MediaTypeHeaderValue.TryParse(contentType, out var mediaType))
            return null;
        var boundary = mediaType.Parameters
            .FirstOrDefault(p => StringSegment.Equals(p.Name, "boundary", StringComparison.OrdinalIgnoreCase));
        if (boundary is null || StringSegment.IsNullOrEmpty(boundary.Value)) return null;
        return HeaderUtilities.RemoveQuotes(boundary.Value).Value;
    }

    /// <summary>multipart 解析结果：Content-Disposition 里的 name / filename 与该段的字节内容。</summary>
    /// <param name="Name">表单字段名；文件段为 null。</param>
    /// <param name="FileName">文件名；表单字段段为 null。</param>
    /// <param name="Body">该段内容的字节切片（自独立数组拷贝，生命周期独立于缓冲）。</param>
    private readonly record struct MultipartPart(string? Name, string? FileName, byte[] Body);

    /// <summary>
    /// 手工解析 multipart/form-data 内存缓冲：按「--boundary」定界符切分，
    /// 每段先按空行（CRLFCRLF）分出头与体，头里取 name / filename，体到下一个定界符前的 CRLF 为止。
    /// 比通用解析器简单可靠（输入已在内存，长度可控）。
    /// body 直接复用 MemoryStream 的内部缓冲（GetBuffer），bodyLength 为有效数据长度，
    /// 避免 ToArray() 再整体拷贝一遍缓冲。
    /// </summary>
    /// <param name="body">完整 multipart 请求体字节缓冲（可能大于有效长度）。</param>
    /// <param name="bodyLength">缓冲中的有效数据长度。</param>
    /// <param name="boundary">multipart 定界符（不含前导 --）。</param>
    /// <returns>依次产出各段的字段名、文件名与字节内容。</returns>
    private static IEnumerable<(string? Name, string? FileName, byte[] Body)> SplitMultipart(byte[] body, int bodyLength, string boundary)
    {
        int len = bodyLength;
        byte[] delimiter = Encoding.ASCII.GetBytes("--" + boundary);

        int IndexOf(byte[] haystack, byte[] needle, int from)
        {
            int max = len - needle.Length;
            for (int i = from; i <= max; i++)
            {
                int j = 0;
                while (j < needle.Length && haystack[i + j] == needle[j]) j++;
                if (j == needle.Length) return i;
            }
            return -1;
        }

        int pos = IndexOf(body, delimiter, 0);
        while (pos >= 0)
        {
            int lineEnd = pos + delimiter.Length;
            // 终止定界符「--boundary--」→ 解析结束
            if (lineEnd + 1 < len && body[lineEnd] == (byte)'-' && body[lineEnd + 1] == (byte)'-')
                yield break;
            // 跳过定界符行尾的 CRLF（可能还有 header 传输用的空白）
            while (lineEnd < len && (body[lineEnd] == (byte)' ' || body[lineEnd] == (byte)'\t')) lineEnd++;
            if (lineEnd + 1 < len && body[lineEnd] == (byte)'\r' && body[lineEnd + 1] == (byte)'\n') lineEnd += 2;
            else if (lineEnd < len && body[lineEnd] == (byte)'\n') lineEnd += 1;

            // 空行分隔头与体
            byte[] crlfcrlf = { (byte)'\r', (byte)'\n', (byte)'\r', (byte)'\n' };
            int headerEnd = IndexOf(body, crlfcrlf, lineEnd);
            int nextDelim;
            int bodyStart;
            string headers;
            if (headerEnd < 0)
            {
                // 没有头部（罕见）：整段到下一个定界符都算体
                headerEnd = lineEnd - 2;
                nextDelim = IndexOf(body, delimiter, lineEnd);
                if (nextDelim < 0) yield break;
                bodyStart = lineEnd;
                headers = string.Empty;
            }
            else
            {
                headers = Encoding.ASCII.GetString(body, lineEnd, headerEnd - lineEnd);
                nextDelim = IndexOf(body, delimiter, headerEnd + 4);
                if (nextDelim < 0) yield break;
                bodyStart = headerEnd + 4;
            }

            // 体以 CRLF 结束（位于下一个定界符之前）
            int bodyEnd = nextDelim;
            if (bodyEnd >= 2 && body[bodyEnd - 2] == (byte)'\r' && body[bodyEnd - 1] == (byte)'\n') bodyEnd -= 2;
            else if (bodyEnd >= 1 && body[bodyEnd - 1] == (byte)'\n') bodyEnd -= 1;

            var (name, fileName) = ParseContentDisposition(headers);
            byte[] part = new byte[Math.Max(0, bodyEnd - bodyStart)];
            Array.Copy(body, bodyStart, part, 0, part.Length);
            yield return (name, fileName, part);

            pos = nextDelim;
        }
    }

    /// <summary>从头文本里找 Content-Disposition 行，取 name 与 filename（支持 filename*，去引号）。</summary>
    /// <param name="headers">某一段的原始头文本（ASCII）。</param>
    /// <returns>字段名与文件名；没有该头或解析失败时对应项为 null。</returns>
    private static (string? Name, string? FileName) ParseContentDisposition(string headers)
    {
        string? line = headers
            .Split('\n')
            .Select(l => l.TrimEnd('\r').Trim())
            .FirstOrDefault(l => l.StartsWith("Content-Disposition", StringComparison.OrdinalIgnoreCase));
        if (line is null) return (null, null);

        int colon = line.IndexOf(':');
        if (colon < 0) return (null, null);
        if (!ContentDispositionHeaderValue.TryParse(line[(colon + 1)..].Trim(), out var disposition))
            return (null, null);

        string? name = StringSegment.IsNullOrEmpty(disposition.Name) ? null : HeaderUtilities.RemoveQuotes(disposition.Name).Value;
        string? fileName = null;
        if (!StringSegment.IsNullOrEmpty(disposition.FileNameStar))
            fileName = HeaderUtilities.RemoveQuotes(disposition.FileNameStar).Value;
        else if (!StringSegment.IsNullOrEmpty(disposition.FileName))
            fileName = HeaderUtilities.RemoveQuotes(disposition.FileName).Value;
        return (name, fileName);
    }

    /// <summary>
    /// 查询当前任务状态（含进度与已译内容）；空闲时返回 <c>{ idle: true }</c>。
    /// 传 <c>?brief=1</c> 返回轻量进度快照（无分段明细与全文，O(1)）——大文件任务的高频轮询请务必用它，
    /// 全量快照对数十万段的任务每次都要复制全部状态并拼接全文。
    /// </summary>
    /// <param name="brief">为 "1" 时返回轻量进度快照（O(1)，适合高频轮询）；其他值返回全量快照。</param>
    /// <returns>200 返回 <c>{ idle: true }</c>（空闲）或当前任务快照/进度。</returns>
    /// <response code="200">返回当前任务状态；空闲时返回 <c>{ idle: true }</c>。</response>
    /// <remarks>
    /// Sample request:
    ///
    ///     GET /api/translation/current?brief=1
    ///
    /// Sample response:
    ///     {
    ///        "jobId": "a1b2c3",
    ///        "status": "Translating",
    ///        "completedSegments": 42,
    ///        "totalSegments": 130
    ///     }
    /// </remarks>
    [HttpGet("current")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Current([FromQuery] string? brief)
    {
        if (brief == "1")
        {
            var progress = _jobs.GetCurrentProgress();
            return progress is null ? Ok(new { idle = true }) : Ok(progress);
        }

        var snapshot = _jobs.GetCurrentSnapshot();
        return snapshot is null ? Ok(new { idle = true }) : Ok(snapshot);
    }

    /// <summary>停止当前任务（已译部分保留）。空闲或已结束时 <c>cancelled</c> 为 false。</summary>
    /// <returns>200 总是返回；cancelled 指示是否真的停止了任务，snapshot 为停止后的任务快照。</returns>
    /// <response code="200">返回停止结果与当前任务快照。</response>
    /// <remarks>
    /// Sample request:
    ///
    ///     POST /api/translation/cancel
    ///
    /// Sample response:
    ///     {
    ///        "cancelled": true,
    ///        "snapshot": { "jobId": "a1b2c3", "status": "Cancelled" }
    ///     }
    /// </remarks>
    [HttpPost("cancel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Cancel()
    {
        bool cancelled = _jobs.CancelCurrent();
        if (cancelled) _logger.LogInformation("收到停止请求（REST）：已请求终止当前任务 {JobId}", _jobs.CurrentJobId ?? "(已结束)");
        else _logger.LogInformation("收到停止请求（REST）：当前没有可停止的任务");
        return Ok(new { cancelled, snapshot = _jobs.GetCurrentSnapshot() });
    }

    /// <summary>
    /// 「模型正忙」→ 409 Conflict：响应体给出占用者 currentJobId，调用方据此查询 / 停止后重新提交。
    /// </summary>
    /// <param name="ex">任务管理器抛出的忙异常，含占用任务的 ID 与原因。</param>
    /// <returns>409 Conflict 结果，响应体带 error/busy/currentJobId/hint。</returns>
    private IActionResult BusyResult(ModelBusyException ex) => StatusCode(StatusCodes.Status409Conflict, new
    {
        error = ex.Message,
        busy = true,
        currentJobId = ex.CurrentJobId,
        hint = "可先查询 GET /api/translation/current，必要时停止 POST /api/translation/cancel，再重新提交。",
    });
}
