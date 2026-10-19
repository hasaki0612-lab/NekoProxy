using System.Globalization;
using NekoProxy.Desktop.Localization;
using Xunit;

namespace NekoProxy.Tests;

public sealed class GuiCopyRegressionTests
{
    [Fact]
    public void HistoricalServiceStateUsesItsSpecificTranslationInEveryLanguage()
    {
        var hub = new LocalizationHub();
        const string timestamp = "10-17 18:20:31";
        foreach (var language in hub.Catalog.Keys)
        {
            hub.Change(language);
            var expected = string.Format(CultureInfo.CurrentCulture, hub["part2.runtimeHistorical"], timestamp);
            Assert.Equal(expected, hub.Translate($"服务运行：部署时已启动 · {timestamp}（未刷新）"));
        }
    }

    [Fact]
    public void FirstConnectionHelpIsTranslatedWithoutChangingUserText()
    {
        var hub = new LocalizationHub();
        const string source = "填写服务器登录资料，先确认连接和环境。首次连接需要核对主机指纹。";
        foreach (var language in hub.Catalog.Keys)
        {
            hub.Change(language);
            Assert.Equal(hub["quality.connectionHelp"], hub.Translate(source));
            Assert.Equal("羽咲的服务器", hub.Translate("羽咲的服务器"));
            Assert.Contains("test.example.com", hub.Translate("test.example.com"));
        }
    }
}
