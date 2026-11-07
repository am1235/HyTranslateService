using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HyTranslateService.Services;

/// <summary>
/// 统一的 JSON 序列化约定：camelCase 字段名 + 不转义中文。
/// 控制器（MVC）与 SignalR 的 JSON 协议共用同一套配置，保证前后端字段命名一致。
/// </summary>
public static class ApiJson
{
    /// <summary>把统一约定应用到指定的序列化配置对象。</summary>
    /// <param name="options">要修改的序列化配置；MVC、Minimal API 与 SignalR 各持有一份，需分别调用</param>
    public static void Configure(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        options.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
    }
}
