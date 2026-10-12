using KapeIR.Core.Services.Help;

namespace KapeIR.Builder.Tests;

public class OperatorHelpContentTests
{
    [Fact]
    public void GetSections_LoadsAllMarkdownTopics()
    {
        var sections = OperatorHelpContent.GetSections();
        Assert.Equal(9, sections.Count);
        Assert.Contains(sections, s => s.Id == "03-phase1");
        Assert.Contains(sections, s => s.Id == "06-chainsaw-hayabusa");
        Assert.All(sections, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Title));
            Assert.False(string.IsNullOrWhiteSpace(s.BodyMarkdown));
            Assert.DoesNotContain("Раздел не найден", s.BodyMarkdown);
        });
    }

    [Fact]
    public void Filter_MatchesBodyText()
    {
        var hit = OperatorHelpContent.Filter("evidence_manifest");
        Assert.Contains(hit, s => s.Id == "07-wrapup-files");
    }

    [Fact]
    public void ChainsawSection_MentionsMinimumRequirements()
    {
        var section = OperatorHelpContent.GetSections()
            .Single(s => s.Id == "06-chainsaw-hayabusa");
        Assert.Contains("Modules\\bin\\chainsaw", section.BodyMarkdown);
        Assert.Contains("Modules\\bin\\hayabusa", section.BodyMarkdown);
        Assert.Contains(".evtx", section.BodyMarkdown);
        Assert.Contains("dfir-timeline", section.BodyMarkdown);
        Assert.Contains("оба", section.BodyMarkdown, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Phase1Section_ListsCompounds()
    {
        var section = OperatorHelpContent.GetSections()
            .Single(s => s.Id == "03-phase1");
        Assert.Contains("Phase1_VolatileFirst", section.BodyMarkdown);
        Assert.Contains("Phase1_MemoryTriage", section.BodyMarkdown);
        Assert.Contains("skip-memory-analysis", section.BodyMarkdown);
    }
}
