using System.Text.Json;
namespace NekoProxy.Desktop.Onboarding;
public sealed record LanguageChoice(string Code, string Name, string FontFamily)
{
    public override string ToString() => Name;
}
public sealed class TranslationCatalog
{
    public static IReadOnlyList<LanguageChoice> Languages { get; } =
    [
        new("zh-Hans", "简体中文", "Segoe UI, Microsoft YaHei UI, sans-serif"),
        new("zh-Hant", "繁體中文", "Segoe UI, Microsoft JhengHei UI, Microsoft YaHei UI, sans-serif"),
        new("en", "English", "Segoe UI, sans-serif"),
        new("ja", "日本語", "Segoe UI, Yu Gothic UI, Meiryo, Microsoft YaHei UI, sans-serif"),
        new("yue-Hant", "廣東話", "Segoe UI, Microsoft JhengHei UI, Microsoft YaHei UI, sans-serif"),
        new("ru", "Русский", "Segoe UI, sans-serif")
    ];
    private readonly Dictionary<string, Dictionary<string, string>> _translations;
    public TranslationCatalog()
    {
        using var stream = typeof(TranslationCatalog).Assembly.GetManifestResourceStream("NekoProxy.Desktop.Resources.onboarding.json")
            ?? throw new InvalidDataException("Onboarding language resource is missing.");
        _translations = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(stream)!;
    }
    public static string Normalize(string? code) => Languages.FirstOrDefault(x => x.Code == code || x.Name == code)?.Code ?? "zh-Hans";
    public IReadOnlyDictionary<string, string> For(string code)
    {
        var merged = new Dictionary<string, string>(_translations["zh-Hans"]);
        foreach (var pair in _translations[Normalize(code)]) merged[pair.Key] = pair.Value;
        return merged;
    }
    public string Get(string code, string key) => For(code).TryGetValue(key, out var value) ? value : $"[{key}]";
}
