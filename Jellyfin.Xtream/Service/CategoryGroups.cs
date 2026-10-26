// Copyright (C) 2022  Kevin Jilissen

// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.

// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Xtream.Client.Models;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Model.Channels;

// ChannelItemResult.Items, which receives these lists, is a List<ChannelItemInfo> in the Jellyfin API.
#pragma warning disable CA1002
namespace Jellyfin.Xtream.Service;

/// <summary>
/// Groups provider categories into folders defined by simple rules, one per line: <c>Label: KEY1, KEY2</c>.
/// A category belongs to the first rule with a key equal to one of its tags (<c>|FR|</c>, <c>[EN]</c>...)
/// or present as a word in its name. Without rules, categories are listed as the provider sends them.
/// </summary>
public static class CategoryGroups
{
    /// <summary>
    /// Parses the group rules.
    /// </summary>
    /// <param name="text">The rules, one per line.</param>
    /// <returns>The rules in order; lines without a label or a key are ignored.</returns>
    public static IReadOnlyList<CategoryGroupRule> ParseRules(string? text)
    {
        List<CategoryGroupRule> rules = [];
        if (string.IsNullOrWhiteSpace(text))
        {
            return rules;
        }

        foreach (string line in text.Split('\n'))
        {
            int colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                continue;
            }

            string label = line[..colon].Trim();
            string[] keys = line[(colon + 1)..]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (label.Length > 0 && keys.Length > 0)
            {
                rules.Add(new CategoryGroupRule(label, keys));
            }
        }

        return rules;
    }

    /// <summary>
    /// Finds the group of a category.
    /// </summary>
    /// <param name="rules">The group rules.</param>
    /// <param name="categoryName">The name of the category.</param>
    /// <returns>The index of the first matching rule, or -1.</returns>
    public static int FindGroup(IReadOnlyList<CategoryGroupRule> rules, string? categoryName)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ParsedName parsed = StreamService.ParseName(categoryName);
        for (int i = 0; i < rules.Count; i++)
        {
            foreach (string key in rules[i].Keys)
            {
                if (parsed.Tags.Any(tag => string.Equals(tag, key, StringComparison.OrdinalIgnoreCase))
                    || Regex.IsMatch(parsed.Title, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(key)}(?![\p{{L}}\p{{N}}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                {
                    return i;
                }
            }
        }

        return -1;
    }

    /// <summary>
    /// Gets the top level of a channel: one folder per non-empty group, then the categories in no group.
    /// </summary>
    /// <param name="categories">The selected categories.</param>
    /// <param name="rules">The group rules.</param>
    /// <param name="categoryPrefix">The id prefix of the categories of the channel.</param>
    /// <param name="groupPrefix">The id prefix of the groups of the channel.</param>
    /// <returns>The channel items.</returns>
    public static List<ChannelItemInfo> TopLevel(IEnumerable<Category> categories, IReadOnlyList<CategoryGroupRule> rules, int categoryPrefix, int groupPrefix)
    {
        ArgumentNullException.ThrowIfNull(rules);
        List<Category> list = [.. categories];
        if (rules.Count == 0)
        {
            return list.Select(category => StreamService.CreateChannelItemInfo(categoryPrefix, category)).ToList();
        }

        ILookup<int, Category> byGroup = list.ToLookup(category => FindGroup(rules, category.CategoryName));
        List<ChannelItemInfo> items = [];
        for (int i = 0; i < rules.Count; i++)
        {
            if (byGroup[i].Any())
            {
                items.Add(new ChannelItemInfo()
                {
                    Id = StreamService.ToGuid(groupPrefix, i, 0, 0).ToString(),
                    Name = rules[i].Label,
                    Type = ChannelItemType.Folder,
                });
            }
        }

        items.AddRange(TidyCategories(byGroup[-1], categoryPrefix));
        return items;
    }

    /// <summary>
    /// Gets the categories of a group, with their tags removed from the name.
    /// The language tag is kept in brackets when the group mixes several languages.
    /// </summary>
    /// <param name="categories">The selected categories.</param>
    /// <param name="rules">The group rules.</param>
    /// <param name="groupIndex">The index of the group.</param>
    /// <param name="categoryPrefix">The id prefix of the categories of the channel.</param>
    /// <returns>The channel items, sorted by name.</returns>
    public static List<ChannelItemInfo> InGroup(IEnumerable<Category> categories, IReadOnlyList<CategoryGroupRule> rules, int groupIndex, int categoryPrefix)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return TidyCategories(categories.Where(category => FindGroup(rules, category.CategoryName) == groupIndex), categoryPrefix);
    }

    private static List<ChannelItemInfo> TidyCategories(IEnumerable<Category> categories, int categoryPrefix)
    {
        List<(Category Category, ParsedName Parsed)> parsed = categories
            .Select(category => (category, StreamService.ParseName(category.CategoryName)))
            .ToList();
        bool mixed = parsed.Select(p => p.Parsed.Tags.FirstOrDefault()?.ToUpperInvariant()).Distinct().Count() > 1;

        return parsed
            .Select(p =>
            {
                string name = p.Parsed.Title.Length > 0 ? p.Parsed.Title : p.Category.CategoryName;
                if (mixed && p.Parsed.Tags.Length > 0)
                {
                    name += $" ({p.Parsed.Tags[0]})";
                }

                return new ChannelItemInfo()
                {
                    Id = StreamService.ToGuid(categoryPrefix, p.Category.CategoryId, 0, 0).ToString(),
                    Name = name,
                    Tags = [.. p.Parsed.Tags],
                    Type = ChannelItemType.Folder,
                };
            })
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
#pragma warning restore CA1002
