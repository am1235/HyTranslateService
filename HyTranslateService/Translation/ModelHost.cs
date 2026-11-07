using System.Diagnostics;
using System.IO;
using Sdcb.HyMT2Sharp.Model;

namespace HyTranslateService.Translation;

/// <summary>
/// 模型宿主。两条硬规定：
/// 1. 内存中同一时刻只载入一个模型（换模型先卸载再加载）；
/// 2. 同一时刻只允许一个任务使用模型（模型实例非线程安全，持有 KV cache）。
/// 申请是**非阻塞独占**的：拿不到闸门立刻报「忙」，调用方拒绝新任务（不排队）。
/// 空闲超过 <see cref="AppOptions.ModelIdleUnloadMinutes"/> 分钟且无任务时自动卸载释放内存。
/// </summary>
public sealed class ModelHost : IDisposable
{
    #region 字段

    /// <summary>应用配置：空闲卸载窗口、推理线程数等。</summary>
    private readonly AppOptions _options;

    /// <summary>模型注册表：把用户引用解析为模型元信息。</summary>
    private readonly ModelRegistry _registry;

    /// <summary>日志器。</summary>
    private readonly ILogger<ModelHost> _logger;

    /// <summary>独占闸门：持有时才可以加载 / 使用 / 卸载常驻模型。</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>唯一常驻模型；null 表示内存中没有模型。只在持有 <see cref="_gate"/> 时写入。</summary>
    private LoadedModelState? _resident;

    /// <summary>
    /// 最近一次模型活动的单调时钟戳（毫秒）。活动 = 请求受理 / 加载完成 / 归还闸门。
    /// 任务执行期间闸门被占用，空闲计时天然不受影响。
    /// </summary>
    private long _lastActivityTicks = Environment.TickCount64;

    /// <summary>空闲自动卸载定时器（<see cref="AppOptions.ModelIdleUnloadMinutes"/> <= 0 时为 null）。</summary>
    private readonly Timer? _idleUnloadTimer;

    /// <summary>是否已释放；置位后只拒绝新的会话申请，进行中的任务照常完成。</summary>
    private volatile bool _disposed;

    #endregion

    #region 公共属性

    /// <summary>当前常驻内存的模型 ID（无模型时为 null）。</summary>
    public string? ResidentModelId => Volatile.Read(ref _resident)?.Id;

    /// <summary>常驻模型使用的推理引擎标识（无模型时为 null）。</summary>
    public string? ResidentEngine => Volatile.Read(ref _resident)?.Engine;

    /// <summary>常驻模型使用的 KV 缓存策略标识（无模型时为 null）。</summary>
    public string? ResidentKvCache => Volatile.Read(ref _resident)?.KvCache;

    /// <summary>常驻模型实际使用的后端名（cpu / vulkan / metal；无模型时为 null）。</summary>
    public string? ResidentBackendName => Volatile.Read(ref _resident)?.BackendName;

    /// <summary>当前已常驻内存的模型（最多一个）。</summary>
    public IReadOnlyList<string> LoadedModelIds
    {
        get
        {
            string? id = ResidentModelId;
            return id is null ? [] : [id];
        }
    }

    /// <summary>模型是否正被某个任务占用（占用期间新任务一律被拒绝）。</summary>
    public bool IsBusy => _gate.CurrentCount == 0;

    #endregion

    /// <summary>创建模型宿主；配置了空闲卸载窗口时同时启动复查定时器。</summary>
    /// <param name="options">应用配置。</param>
    /// <param name="registry">模型注册表。</param>
    /// <param name="logger">日志器。</param>
    public ModelHost(AppOptions options, ModelRegistry registry, ILogger<ModelHost> logger)
    {
        _options = options;
        _registry = registry;
        _logger = logger;

        // 首次检查安排在窗口期满，之后每 30 秒复查一次
        _idleUnloadTimer = options.ModelIdleUnloadMinutes > 0
            ? new Timer(
                _ => TryIdleUnload(),
                null,
                dueTime: TimeSpan.FromMinutes(options.ModelIdleUnloadMinutes),
                period: TimeSpan.FromSeconds(30))
            : null;
    }

    #region 私有方法

    /// <summary>记录一次模型活动（单调时钟，无需精确时间点）。</summary>
    private void TouchActivity() => Volatile.Write(ref _lastActivityTicks, Environment.TickCount64);

    /// <summary>
    /// 空闲自动卸载（定时器回调）：闸门空闲、有常驻模型、且距上次活动超过窗口时卸载。
    /// </summary>
    private void TryIdleUnload()
    {
        if (_disposed) return;
        int idleMinutes = _options.ModelIdleUnloadMinutes;
        if (idleMinutes <= 0) return;

        TimeSpan idle = TimeSpan.FromMilliseconds(Environment.TickCount64 - Volatile.Read(ref _lastActivityTicks));
        if (idle < TimeSpan.FromMinutes(idleMinutes)) return;

        // 定时器回调异常会击穿进程；与 Dispose 的竞争以 ObjectDisposedException 收场
        bool acquired = false;
        try
        {
            acquired = _gate.Wait(0);    // 正被任务占用 / 加载卸载途中：跳过
            if (!acquired) return;

            // 双检：等待期间可能有新请求已把时钟拨回
            var resident = Volatile.Read(ref _resident);
            if (resident is null) return;
            idle = TimeSpan.FromMilliseconds(Environment.TickCount64 - Volatile.Read(ref _lastActivityTicks));
            if (idle < TimeSpan.FromMinutes(idleMinutes)) return;

            Unload();
            _logger.LogInformation("模型 {Id} 已空闲 {Idle:0.#} 分钟且无新请求，自动卸载释放内存",
                resident.Id, idle.TotalMinutes);
        }
        catch (ObjectDisposedException)
        {
            // 宿主正在释放（进程退出阶段）
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "空闲自动卸载检查出错（跳过本次）");
        }
        finally
        {
            if (acquired) ReleaseGate();
        }
    }

    /// <summary>加载模型。只在持有闸门时调用。</summary>
    /// <param name="info">模型元信息。</param>
    /// <param name="engine">归一化后的推理引擎标识。</param>
    /// <param name="kvCache">归一化后的 KV 缓存策略标识。</param>
    /// <param name="status">加载进度回调，可为 null。</param>
    /// <returns>刚加载好的常驻模型状态。</returns>
    private LoadedModelState Load(TranslationModelInfo info, string engine, string kvCache, ModelStatusReporter? status)
    {
        status?.Invoke(new ModelStatus(ModelStatusKind.Loading,
            $"正在加载模型 {info.Id}（{info.Quantization}，{info.SizeText}，引擎 {EngineText(engine)}）"));

        _logger.LogInformation("正在加载模型 {Id}（{Quant}, {Size}，引擎 {Engine}，KV {Kv}）…",
            info.Id, info.Quantization, info.SizeText, engine, kvCache);

        var sw = Stopwatch.StartNew();

        // 后端实例随模型一起释放：模型在构造失败时也会自行释放它
        var cacheConfig = KvCachePolicies.ToConfig(kvCache);
        var backend = InferenceEngines.CreateBackend(engine);
        string backendName = backend?.Name ?? "cpu";
        bool isGpu = backend is not null && backend.Name != "cpu";

        // 加载前后各采样一次进程内存，作为「本次加载实际占用的内存」的近似度量
        var proc = Process.GetCurrentProcess();
        proc.Refresh();
        long privBefore = proc.PrivateMemorySize64;
        long wsBefore = proc.WorkingSet64;

        HunyuanDenseModel model;
        try
        {
            model = new HunyuanDenseModel(info.FullPath, _options.Threads, cacheConfig, backend);
        }
        catch (Exception ex) when (engine == InferenceEngines.Auto && backend is not null && ex is not OutOfMemoryException)
        {
            // 「自动」的含义就是自动挑一个**能用**的引擎：显存不够 / 量化不被支持时回落 CPU。
            // 显式选了 GPU 的请求不走这里（那是用户明确的意图，必须让失败可见）。
            _logger.LogWarning(ex, "自动引擎在 {Backend} 上加载模型 {Id} 失败，改用 CPU 重试", backendName, info.Id);
            status?.Invoke(new ModelStatus(ModelStatusKind.Loading,
                $"GPU（{backendName}）加载模型失败，正在改用 CPU 重试：{ex.Message}"));
            backendName = "cpu";
            isGpu = false;
            model = new HunyuanDenseModel(info.FullPath, _options.Threads, cacheConfig, backend: null);
        }
        catch (Exception ex) when (engine == InferenceEngines.Gpu && ex is not OutOfMemoryException)
        {
            // 显式选了 GPU：保留用户意图、让失败可见，但要补一句人能看懂该怎么做
            throw new InvalidOperationException(
                $"GPU 引擎加载模型 {info.Id} 失败：{ex.Message}。可改用 CPU 引擎，或选择「自动」（会先试 GPU、失败再回落 CPU）。", ex);
        }

        sw.Stop();

        proc.Refresh();
        long privAfter = proc.PrivateMemorySize64;
        long wsAfter = proc.WorkingSet64;
        long weightsBytes = SafeFileSize(info.FullPath);
        string deviceInfo = isGpu ? $"，设备 {backend?.Description ?? "GPU"}" : string.Empty;

        _logger.LogInformation(
            "模型 {Id} 加载完成：用时 {Ms} ms；后端 {BackendName}（{Kind}）{DeviceInfo}；" +
            "线程 {Threads}（{ThreadHint}）；量化 {Quant}，权重 {Weights}，上下文 {Ctx} token，词表 {Vocab}；" +
            "进程私有内存 {PrivBefore}→{PrivAfter}（增量 {PrivDelta}），工作集 {WsBefore}→{WsAfter}（增量 {WsDelta}）",
            info.Id, sw.ElapsedMilliseconds, backendName, isGpu ? "GPU" : "CPU", deviceInfo,
            model.ThreadCount, model.ThreadAutoHint, info.Quantization, FormatBytes(weightsBytes),
            model.Config.ContextLength, model.Config.VocabSize,
            FormatBytes(privBefore), FormatBytes(privAfter), FormatBytes(Math.Max(0, privAfter - privBefore)),
            FormatBytes(wsBefore), FormatBytes(wsAfter), FormatBytes(Math.Max(0, wsAfter - wsBefore)));

        var state = new LoadedModelState
        {
            Id = info.Id,
            Info = info,
            Model = model,
            Engine = engine,
            KvCache = kvCache,
            BackendName = backendName,
        };
        Volatile.Write(ref _resident, state);
        TouchActivity();
        return state;
    }

    /// <summary>把字节数格式化为带单位的可读字符串（B/KB/MB/GB/TB）。</summary>
    /// <param name="bytes">字节数；负数按 0 处理。</param>
    /// <returns>如 1.5 GB。</returns>
    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        double value = bytes;
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:F1} {units[unit]}";
    }

    /// <summary>安全读取模型文件大小（用于估算权重的显存 / 内存占用）；文件不可读时返回 0。</summary>
    /// <param name="path">模型文件路径。</param>
    /// <returns>文件字节数；读取失败为 0。</returns>
    private static long SafeFileSize(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>把引擎标识转成可直接展示给用户的中文说明。</summary>
    /// <param name="engine">归一化后的引擎标识。</param>
    /// <returns>展示用文本，如「自动（auto）」。</returns>
    private static string EngineText(string engine) => engine switch
    {
        InferenceEngines.Auto => "自动",
        InferenceEngines.Gpu => "GPU",
        _ => "CPU",
    };

    /// <summary>卸载常驻模型。只在持有闸门时调用（因此可以确定没人在用它）。</summary>
    private void Unload()
    {
        var victim = Volatile.Read(ref _resident);
        if (victim is null) return;

        Volatile.Write(ref _resident, null);
        _logger.LogInformation("卸载模型 {Id}，释放内存", victim.Id);
        try
        {
            victim.Model.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "卸载模型 {Id} 时出错", victim.Id);
        }
    }

    /// <summary>拒绝请求时说明「谁在占用、要它做什么」，让调用方能一步找到该终止谁。</summary>
    /// <param name="wanted">本次请求需要的模型。</param>
    /// <param name="engine">本次请求的引擎标识。</param>
    /// <param name="kvCache">本次请求的 KV 缓存策略。</param>
    /// <returns>可直接展示给用户的占用原因。</returns>
    private string DescribeBusy(TranslationModelInfo wanted, string engine, string kvCache)
    {
        var busy = Volatile.Read(ref _resident);
        if (busy is null)
        {
            // 闸门被占用但内存里还没有模型：占用者正在加载/卸载途中
            return $"模型资源正被另一个翻译任务占用（正在准备模型），无法开始需要 {wanted.Id} 的任务。";
        }

        if (!string.Equals(busy.Id, wanted.Id, StringComparison.OrdinalIgnoreCase))
        {
            return $"另一个任务正在使用模型 {busy.Id}（内存中只常驻一个模型，换模型需先卸载），无法开始需要 {wanted.Id} 的任务。";
        }

        // 同一个模型但运行参数不同：换引擎 / 换缓存策略同样要卸载重载
        if (busy.Engine != engine)
            return $"模型 {busy.Id} 正被另一个任务以「{EngineText(busy.Engine)}」引擎使用，切换引擎需要先卸载模型，无法开始本次任务。";

        if (busy.KvCache != kvCache)
            return $"模型 {busy.Id} 正被另一个任务以「{KvCachePolicies.Describe(busy.KvCache)}」缓存策略使用，切换策略需要先卸载模型，无法开始本次任务。";

        return $"模型 {busy.Id} 正被另一个翻译任务使用，无法开始新的任务。";
    }

    /// <summary>归还闸门并刷新活动时钟；释放阶段的竞争异常静默吞掉。</summary>
    private void ReleaseGate()
    {
        TouchActivity();
        try
        {
            _gate.Release();
        }
        catch (ObjectDisposedException)
        {
            // 宿主已释放（进程退出阶段）
        }
        catch (SemaphoreFullException)
        {
        }
    }

    #endregion

    #region 公共方法
    /// <summary>模型是否已常驻内存。</summary>
    /// <param name="modelId">模型 ID，忽略大小写。</param>
    /// <returns>该模型当前已常驻内存时为 true。</returns>
    public bool IsLoaded(string modelId)
        => string.Equals(Volatile.Read(ref _resident)?.Id, modelId, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 非阻塞申请模型会话：只占用闸门，暂不加载模型（加载发生在 <see cref="ModelLease.EnsureLoaded"/>）。
    /// 返回 <c>null</c> 表示模型正忙。<paramref name="busyReason"/> 给出可直接展示的原因。
    /// </summary>
    /// <param name="modelReference">模型引用；null/空时自动选推荐模型。</param>
    /// <param name="engine">归一化后的推理引擎标识。</param>
    /// <param name="kvCache">归一化后的 KV 缓存策略标识。</param>
    /// <param name="busyReason">模型忙时给出可直接展示的占用原因，成功时为 null。</param>
    /// <returns>模型会话；模型正忙时为 null（不排队）。</returns>
    public ModelLease? TryAcquire(string? modelReference, string engine, string kvCache, out string? busyReason)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        TranslationModelInfo info = _registry.Resolve(modelReference);

        if (!_gate.Wait(0))
        {
            busyReason = DescribeBusy(info, engine, kvCache);
            _logger.LogInformation("拒绝翻译请求：{Reason}", busyReason);
            return null;
        }

        busyReason = null;
        TouchActivity();
        return new ModelLease(this, info, engine, kvCache);
    }

    /// <summary>
    /// 确保模型已就绪：复用常驻模型，或（先卸载旧模型再）加载新模型。
    /// 引擎与 KV 缓存策略在加载时绑定，任一项不同都视为「需要换模型」。
    /// 只在持有闸门时调用，因此这里可以安全地替换常驻模型。
    /// </summary>
    /// <param name="info">需要的模型元信息。</param>
    /// <param name="engine">归一化后的推理引擎标识。</param>
    /// <param name="kvCache">归一化后的 KV 缓存策略标识。</param>
    /// <param name="status">加载/卸载进度回调，可为 null。</param>
    /// <returns>已就绪的常驻模型状态。</returns>
    internal LoadedModelState LoadIntoMemory(TranslationModelInfo info, string engine, string kvCache, ModelStatusReporter? status)
    {
        LoadedModelState? resident = Volatile.Read(ref _resident);

        if (resident is null)
            return Load(info, engine, kvCache, status);

        bool sameModel = string.Equals(resident.Id, info.Id, StringComparison.OrdinalIgnoreCase);
        bool sameRuntime = resident.Engine == engine && resident.KvCache == kvCache;

        if (sameModel && sameRuntime)
        {
            _logger.LogDebug("复用常驻模型 {Id}（引擎 {Engine}，KV {Kv}）", resident.Id, resident.Engine, resident.KvCache);
            return resident;
        }

        // 内存中只常驻一个模型：先卸载旧的，再加载新的
        string reason = sameModel
            ? $"引擎/缓存策略变化（{EngineText(resident.Engine)}·{KvCachePolicies.Describe(resident.KvCache)} → {EngineText(engine)}·{KvCachePolicies.Describe(kvCache)}）"
            : $"换模型（{resident.Id} → {info.Id}）";
        status?.Invoke(new ModelStatus(ModelStatusKind.Unloading,
            $"正在卸载模型 {resident.Id}：{reason}（内存中只常驻一个模型）"));
        Unload();
        return Load(info, engine, kvCache, status);
    }

    /// <summary>预加载模型（失败只记录警告，不阻断启动；模型忙时直接跳过）。</summary>
    /// <param name="modelReference">模型引用；null/空时取推荐模型。</param>
    /// <returns>模型已就绪为 true；忙或加载失败为 false。</returns>
    public bool TryPreload(string? modelReference, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var lease = TryAcquire(modelReference, _options.Engine, _options.KvCache, out string? busyReason);
        if (lease is null)
        {
            _logger.LogWarning("跳过预加载：{Reason}", busyReason);
            return false;
        }

        using (lease)
        {
            try
            {
                lease.EnsureLoaded(s => _logger.LogInformation("预加载：{Message}", s.Message));
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "预加载模型失败：{Model}", modelReference);
                return false;
            }
        }
    }

    /// <summary>卸载常驻模型（尽力而为：模型正被占用时跳过，等任务自己结束）。</summary>
    public void UnloadAll()
    {
        bool acquired = false;
        try
        {
            acquired = _gate.Wait(TimeSpan.FromSeconds(5));
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        if (!acquired)
        {
            _logger.LogWarning("仍有任务在使用模型，跳过卸载（进程退出时由操作系统回收内存）");
            return;
        }

        try
        {
            Unload();
        }
        finally
        {
            ReleaseGate();
        }
    }

    /// <summary>归还闸门：模型随即可以被下一个任务使用。</summary>
    internal void Release() => ReleaseGate();

    /// <summary>释放宿主：停止空闲卸载定时器、卸载常驻模型并释放闸门；进程退出阶段可安全重复调用。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _idleUnloadTimer?.Dispose();
        UnloadAll();
        _gate.Dispose();
    }

    #endregion

    #region 嵌套类型

    /// <summary>模型状态阶段（只保留「在做什么」，不再有「在等谁」）。</summary>
    public enum ModelStatusKind
    {
        /// <summary>正在卸载不再需要的常驻模型。</summary>
        Unloading,

        /// <summary>正在从磁盘加载模型。</summary>
        Loading,
    }

    /// <summary>一条模型状态说明（可直接展示给用户）。</summary>
    /// <param name="Kind">状态阶段。</param>
    /// <param name="Message">可直接展示的状态文本。</param>
    public readonly record struct ModelStatus(ModelStatusKind Kind, string Message);

    /// <summary>报告模型状态的回调（可为 null）。</summary>
    /// <param name="status">当前模型状态。</param>
    public delegate void ModelStatusReporter(ModelStatus status);

    /// <summary>已常驻内存的模型状态。</summary>
    public sealed class LoadedModelState
    {
        /// <summary>模型 ID。</summary>
        public required string Id { get; init; }

        /// <summary>模型元信息。</summary>
        public required TranslationModelInfo Info { get; init; }

        /// <summary>已加载的推理模型实例。</summary>
        public required HunyuanDenseModel Model { get; init; }

        /// <summary>加载时采用的推理引擎标识（<see cref="InferenceEngines"/>）。</summary>
        public required string Engine { get; init; }

        /// <summary>加载时采用的 KV 缓存策略标识（<see cref="KvCachePolicies"/>）。</summary>
        public required string KvCache { get; init; }

        /// <summary>实际生效的计算后端名称（<c>cpu</c> / <c>vulkan</c> / <c>metal</c>）；自动模式下可据此确认是否真的用上了 GPU。</summary>
        public required string BackendName { get; init; }
    }

    #endregion
}

/// <summary>
/// 模型使用会话：持有期间独占模型，<see cref="EnsureLoaded"/> 返回的实例始终有效。
/// Dispose 即归还闸门。
/// </summary>
public sealed class ModelLease : IDisposable
{
    #region 字段

    /// <summary>宿主引用，用于归还闸门。</summary>
    private readonly ModelHost _host;

    /// <summary>EnsureLoaded 加载得到的常驻状态；释放后置 null，模型实例归宿主管理。</summary>
    private ModelHost.LoadedModelState? _state;

    /// <summary>0 = 未释放；Dispose 可能被多次调用，用交换置位保证只归还一次闸门。</summary>
    private int _disposed;

    #endregion

    /// <summary>本次会话需要的模型元信息。</summary>
    public TranslationModelInfo Info { get; }

    /// <summary>本次会话要求加载的推理引擎标识。</summary>
    public string Engine { get; }

    /// <summary>本次会话要求加载的 KV 缓存策略标识。</summary>
    public string KvCache { get; }

    /// <summary>创建模型会话。</summary>
    /// <param name="host">模型宿主。</param>
    /// <param name="info">本次会话需要的模型元信息。</param>
    /// <param name="engine">本次会话要求的推理引擎标识。</param>
    /// <param name="kvCache">本次会话要求的 KV 缓存策略标识。</param>
    internal ModelLease(ModelHost host, TranslationModelInfo info, string engine, string kvCache)
    {
        _host = host;
        Info = info;
        Engine = engine;
        KvCache = kvCache;
    }

    #region 公共方法

    /// <summary>
    /// 确保所需模型已常驻内存（必要时先卸载旧模型），返回**独占可用**的模型实例。
    /// 重复调用只加载一次。
    /// </summary>
    /// <param name="status">加载/卸载进度回调，可为 null。</param>
    /// <returns>独占可用的模型实例；会话释放后调用会抛 ObjectDisposedException。</returns>
    public HunyuanDenseModel EnsureLoaded(ModelHost.ModelStatusReporter? status = null)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        _state ??= _host.LoadIntoMemory(Info, Engine, KvCache, status);
        return _state.Model;
    }

    /// <summary>归还闸门并结束会话；模型实例归常驻槽位管理，可安全重复调用。</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _state = null;   // 模型实例归常驻槽位管理，不随会话释放
        _host.Release();
    }

    #endregion
}

/// <summary>
/// 「模型正忙」：同一时刻只跑一个翻译任务，已有任务在跑时新任务被立即拒绝（不排队）。
/// </summary>
public sealed class ModelBusyException : InvalidOperationException
{
    /// <summary>当前正在执行（占用模型）的任务 ID；为 null 表示没有已知的当前任务。</summary>
    public string? CurrentJobId { get; }

    /// <summary>创建「模型正忙」异常。</summary>
    /// <param name="message">可直接展示给用户的拒绝原因。</param>
    /// <param name="currentJobId">当前占用模型的任务 ID；没有已知任务时为 null。</param>
    public ModelBusyException(string message, string? currentJobId = null) : base(message)
        => CurrentJobId = currentJobId;
}
