using Jellyfin.Xtream.Client.Models;
using Jellyfin.Xtream.Service;
using MediaBrowser.Controller.Channels;

namespace Jellyfin.Xtream.Tests;

public class CategoryGroupsTests
{
    private const string Rules = """
        Animés: ANIME, MANGA, JAPAN
        Sport: SPT, SPORT, WORLDCUP
        Français: FR, QC
        English: EN
        """;

    private static Category Cat(int id, string name) => new() { CategoryId = id, CategoryName = name };

    [Fact]
    public void RulesAreParsedInOrderAndInvalidLinesIgnored()
    {
        IReadOnlyList<CategoryGroupRule> rules = CategoryGroups.ParseRules(Rules + "\nno colon here\n: KEY\nEmpty:\n");

        Assert.Equal(["Animés", "Sport", "Français", "English"], rules.Select(r => r.Label));
        Assert.Equal(["ANIME", "MANGA", "JAPAN"], rules[0].Keys);
    }

    [Theory]
    [InlineData("|FR| MANGA", "Animés")]
    [InlineData("|EN| ANIME", "Animés")]
    [InlineData("|JP| JAPANESE DUBBED & SUB", "Français")]
    [InlineData("|SPT| SPORT UFC", "Sport")]
    [InlineData("WORLDCUP 2022", "Sport")]
    [InlineData("|FR| ACTION/AVENTURE", "Français")]
    [InlineData("|QC| FILMS", "Français")]
    [InlineData("|EN| DRAMA", "English")]
    public void ACategoryGoesToTheFirstMatchingRule(string name, string expected)
    {
        IReadOnlyList<CategoryGroupRule> rules = CategoryGroups.ParseRules(Rules.Replace("Français: FR, QC", "Français: FR, QC, JP", StringComparison.Ordinal));

        int index = CategoryGroups.FindGroup(rules, name);

        Assert.Equal(expected, rules[index].Label);
    }

    [Fact]
    public void KeysMatchWholeWordsOnly()
    {
        IReadOnlyList<CategoryGroupRule> rules = CategoryGroups.ParseRules("English: EN");

        Assert.Equal(-1, CategoryGroups.FindGroup(rules, "|DE| FILME ENGLISCH"));
        Assert.Equal(-1, CategoryGroups.FindGroup(rules, "|ES| PELICULAS"));
    }

    [Fact]
    public void WithoutRulesTheCategoriesAreListedAsSent()
    {
        List<ChannelItemInfo> items = CategoryGroups.TopLevel([Cat(1, "|FR| DRAME")], CategoryGroups.ParseRules(null), 100, 200);

        Assert.Equal(["|FR| DRAME"], items.Select(i => i.Name));
    }

    [Fact]
    public void TheTopLevelHasOneFolderPerNonEmptyGroupThenTheOthers()
    {
        List<Category> categories = [Cat(1, "|FR| DRAME"), Cat(2, "|EN| DRAMA"), Cat(3, "|FR| MANGA"), Cat(4, "|DE| FILME")];

        List<ChannelItemInfo> items = CategoryGroups.TopLevel(categories, CategoryGroups.ParseRules(Rules), 100, 200);

        Assert.Equal(["Animés", "Français", "English", "FILME"], items.Select(i => i.Name));
        Assert.All(items, i => Assert.Equal(ChannelItemType.Folder, i.Type));
    }

    [Fact]
    public void InsideAGroupNamesAreCleanedAndSorted()
    {
        List<Category> categories = [Cat(1, "|FR| THRILLER/CRIME"), Cat(2, "|FR| ACTION/AVENTURE"), Cat(3, "|QC| FILMS")];

        List<ChannelItemInfo> items = CategoryGroups.InGroup(categories, CategoryGroups.ParseRules(Rules), 2, 100);

        Assert.Equal(["ACTION/AVENTURE (FR)", "FILMS (QC)", "THRILLER/CRIME (FR)"], items.Select(i => i.Name));
    }

    [Fact]
    public void ASingleLanguageGroupDropsTheTag()
    {
        List<Category> categories = [Cat(1, "|EN| DRAMA"), Cat(2, "|EN| COMEDY")];

        List<ChannelItemInfo> items = CategoryGroups.InGroup(categories, CategoryGroups.ParseRules(Rules), 3, 100);

        Assert.Equal(["COMEDY", "DRAMA"], items.Select(i => i.Name));
    }

    [Fact]
    public void GroupAndCategoryIdsDoNotCollide()
    {
        List<ChannelItemInfo> top = CategoryGroups.TopLevel([Cat(0, "|FR| DRAME"), Cat(9, "|DE| X")], CategoryGroups.ParseRules(Rules), StreamService.VodCategoryPrefix, StreamService.VodGroupPrefix);

        Assert.Equal(top.Count, top.Select(i => i.Id).Distinct().Count());
    }
}
