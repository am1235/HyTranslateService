using HyTranslateService.Services;
using HyTranslateService.Translation;
using Microsoft.Extensions.DependencyInjection;

namespace HyTranslateService;

/// <summary>服务注册。</summary>
public static class HyTranslateServices
{
    /// <summary>服务端实现名与版本。</summary>
    public static string ServerName => "HyTranslateService";

    /// <summary>服务端版本号，取程序集版本前 3 段；取不到时回退 1.0.0。</summary>
    public static string ServerVersion =>
        typeof(HyTranslateServices).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    /// <summary>给 AI 客户端的服务说明。</summary>
    public const string ServerInstructions = """
        HyTranslateService：基于腾讯 Hy-MT2（HyMT2Sharp，纯 C#，CPU / GPU 双后端）的本地翻译服务。

        使用建议：
        - 先调用 list_translation_models 查看可用模型，调用 list_target_languages 查看可用的目标语言（38 种）。
        - 本地尚未下载模型时，可在 WebUI「翻译模型」下拉中选择官方模型自动下载
          （先探测 huggingface.co，受限时自动切换镜像站 hf-mirror.com）；list_translation_models 也会列出官方目录与下载状态。
        - 需要翻译时调用 translate_text：它会在执行过程中持续上报进度（含译文增量），
          客户端可以在收到不满意或不需要时直接取消该次工具调用（MCP 取消通知会立即中断推理）。
        - translate_text / translate_file 都支持 engine（auto/gpu/cpu）与 kvCache（memory/none）：
          两者在模型加载时绑定，与常驻模型不同时会先卸载再加载，译文本身不受影响。
          engine=auto 在 GPU 不可用时自动回落 CPU；显式 engine=gpu 不可用则直接失败，不会静默降级。
        - 长文本会自动按段落/句子拆分为多个片段依次翻译，无需自己切分；也可以用 segmentChars 控制单段长度。
        - 术语表（glossary，每行「原文=译文」）、背景信息（background）、目标风格（targetStyle）
          可显著提升专业文本的翻译一致性。
        - 参数名一律为驼峰（targetLanguage、sourceLanguage、segmentChars、maxTokens、topP、topK、
          promptStyle、promptLanguage、repetitionPenalty、kvCache、targetStyle、includeSegments），
          不是蛇形写法，按工具 schema 传参。
        - 服务端同一时刻只常驻 1 个模型（内存限制）：要换模型时会先卸载当前模型再加载，需要一点加载时间。
        - 服务端同一时刻**只执行一个翻译任务**（模型实例非线程安全）。若当前已有任务在跑，新任务会被**立即拒绝**
          （模型正忙，不排队、不等待），错误信息里会说明当前任务。
          此时先用 get_current_translation 查看当前任务的进度与译文，
          确认可以放弃后用 cancel_current_translation 停止它，然后再重新提交——
          这就是「停止当前任务、开启新任务」的标准流程。WebUI 也提供同样的能力。
        """;

    /// <summary>注册核心服务（模型注册表、模型宿主、模型下载器、翻译引擎、任务管理器、界面语言目录）。</summary>
    /// <param name="services">DI 容器服务集合</param>
    /// <param name="options">启动配置，各核心服务以单例共享同一份</param>
    /// <returns>服务集合本身，便于链式调用</returns>
    public static IServiceCollection AddHyTranslateCore(this IServiceCollection services, AppOptions options)
    {
        services.AddSingleton(options);
        services.AddSingleton<ModelRegistry>();
        services.AddSingleton<ModelHost>();
        services.AddSingleton<ModelDownloader>();
        services.AddSingleton<TranslationEngine>();
        services.AddSingleton<TranslationJobManager>();
        services.AddSingleton<LanguageCatalog>();
        return services;
    }
}
