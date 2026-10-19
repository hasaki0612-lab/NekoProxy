using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using NekoProxy.Desktop.Onboarding;

namespace NekoProxy.Desktop.Localization;

// Stable catalog keys and shared observable language state. User-entered text is never translated.
public sealed class LocalizationHub : INotifyPropertyChanged
{
    public static LocalizationHub Current { get; } = new();
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Language { get; private set; } = "zh-Hans";
    public string FontFamily => TranslationCatalog.Languages.First(x => x.Code == Language).FontFamily;
    public IReadOnlyDictionary<string, Dictionary<string, string>> Catalog { get; }
    private readonly Dictionary<string, string> _sourceKeys;
    private readonly List<(Regex Pattern, string Key)> _patterns = [];
    public LocalizationHub()
    {
        using var stream = typeof(LocalizationHub).Assembly.GetManifestResourceStream("NekoProxy.Desktop.Resources.app.json") ?? throw new InvalidDataException("Application translations are missing.");
        Catalog = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(stream)!;
        _sourceKeys = Catalog["zh-Hans"].GroupBy(p => p.Value).ToDictionary(p => p.Key, p => p.First().Key);
        using var aliases=typeof(LocalizationHub).Assembly.GetManifestResourceStream("NekoProxy.Desktop.Resources.source-aliases.json");
        if(aliases is not null)foreach(var pair in JsonSerializer.Deserialize<Dictionary<string,string>>(aliases)!)_sourceKeys.TryAdd(pair.Key,pair.Value);
        // Prefer a specific status template before a generic one that can also
        // consume its words as placeholders (e.g. historical service status).
        foreach (var pair in Catalog["zh-Hans"].Where(p => Regex.IsMatch(p.Value, @"\{[0-9]+\}"))
                     .OrderByDescending(p => Regex.Replace(p.Value, @"\{[0-9]+\}", "").Length))
        {
            var pattern = string.Concat(Regex.Split(pair.Value, @"(\{[0-9]+\})").Select(p => Regex.IsMatch(p, @"^\{[0-9]+\}$") ? "(.*?)" : Regex.Escape(p)));
            _patterns.Add((new Regex("^" + pattern + "$", RegexOptions.Singleline | RegexOptions.CultureInvariant), pair.Key));
        }
    }
    public string this[string key] => Catalog[Language].TryGetValue(key, out var value) ? value : "[" + key + "]";
    public void Change(string code)
    {
        var next = TranslationCatalog.Normalize(code); if (Language == next) return; Language = next;
        PropertyChanged?.Invoke(this, new(nameof(Language))); PropertyChanged?.Invoke(this, new(nameof(FontFamily))); PropertyChanged?.Invoke(this, new("Item")); PropertyChanged?.Invoke(this, new("Item[]"));
    }
    public string Translate(string source)
    {
        if (_sourceKeys.TryGetValue(source, out var key)) return this[key];
        foreach (var (pattern, templateKey) in _patterns)
        {
            var match = pattern.Match(source); if (match.Success) return string.Format(CultureInfo.CurrentCulture, this[templateKey], match.Groups.Cast<Group>().Skip(1).Select(g => (object)g.Value).ToArray());
        }
        if(source.Contains('\n'))return string.Join("\n",source.Split('\n').Select(Translate));
        if(source.Contains(" · ",StringComparison.Ordinal))return string.Join(" · ",source.Split(" · ").Select(Translate));
        foreach(var suffix in new[]{" 已恢复这次操作前的节点配置。"," 未能确认回滚结果，请检查服务器上的备份与服务状态。"})
            if(source.EndsWith(suffix,StringComparison.Ordinal))return Translate(source[..^suffix.Length])+Translate(suffix);
        return source;
    }
    public string TranslatePlaceholder(string source)
    {
        foreach(var placeholder in new[]{"位置未设置","未设置"})
        {
            if(source==placeholder)return Translate(placeholder);
            if(source.StartsWith(placeholder+" · ",StringComparison.Ordinal))return Translate(placeholder)+source[placeholder.Length..];
        }
        return source;
    }
}
public sealed class TrExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => new Binding("[" + key + "]") { Source = LocalizationHub.Current, Mode = BindingMode.OneWay };
}
public sealed class LocalExtension(string path) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => new MultiBinding
    {
        Bindings = { new Binding(path), new Binding(nameof(LocalizationHub.Language)) { Source = LocalizationHub.Current } },
        Converter = LocalTextConverter.Instance, Mode = BindingMode.OneWay
    };
}
public sealed class LocalTextConverter : IMultiValueConverter
{
    public static LocalTextConverter Instance { get; } = new();
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) => values.Count > 0 && values[0] is string text ? LocalizationHub.Current.Translate(text) : "";
}
public sealed class PlaceholderExtension(string path) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => new MultiBinding
    {
        Bindings = { new Binding(path), new Binding(nameof(LocalizationHub.Language)) { Source = LocalizationHub.Current } },
        Converter = PlaceholderConverter.Instance, Mode = BindingMode.OneWay
    };
}
public sealed class PlaceholderConverter : IMultiValueConverter
{
    public static PlaceholderConverter Instance { get; }=new();
    public object Convert(IList<object?> values,Type targetType,object? parameter,CultureInfo culture)=>values.Count>0&&values[0] is string text?LocalizationHub.Current.TranslatePlaceholder(text):"";
}

