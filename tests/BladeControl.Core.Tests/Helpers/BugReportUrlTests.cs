using BladeControl.Core.Platform;

namespace BladeControl.Core.Tests.Helpers;

public class BugReportUrlTests
{
    [Fact]
    public void ItOpensTheBugFormOnThisProject_WithTheFieldsFilledIn()
    {
        var url = ExternalLinks.BugReportUrl(new Dictionary<string, string?>
        {
            ["version"] = "0.1.6",
            ["laptop"] = "Razer Blade 15 Base (2020)"
        });

        Assert.Equal(
            "https://github.com/jdsgnrinfo/blade-control/issues/new?template=bug_report.yml&version=0.1.6&laptop=Razer%20Blade%2015%20Base%20%282020%29",
            url);
    }

    [Fact]
    public void EmptyFields_AreLeftOut() =>
        Assert.Equal(
            "https://github.com/jdsgnrinfo/blade-control/issues/new?template=bug_report.yml&bios=BIOS%2001.04",
            ExternalLinks.BugReportUrl(new Dictionary<string, string?> { ["laptop"] = null, ["windows"] = " ", ["bios"] = "BIOS 01.04" }));

    [Fact]
    public void CharactersThatWouldBreakTheLink_AreEscaped()
    {
        var url = ExternalLinks.BugReportUrl(new Dictionary<string, string?> { ["laptop"] = "A&B=C#D" });

        Assert.EndsWith("laptop=A%26B%3DC%23D", url);
    }
}
