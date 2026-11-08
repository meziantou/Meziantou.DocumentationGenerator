using System.Text;
using Meziantou.DocumentationGenerator.Content;
using Meziantou.Framework.Markdown.Helpers;

namespace Meziantou.DocumentationGenerator.Rendering;

/// <summary>
/// HTML of the generated lists: the sections of the generated home page and the <c>&lt;SectionList /&gt;</c> and
/// <c>&lt;PackageList /&gt;</c> Markdown components. Both use this class so they always look the same.
/// </summary>
internal static class EntryListHtml
{
    /// <summary>Returns the HTML of <paramref name="items"/>; every text and URL is HTML-encoded.</summary>
    /// <param name="headingLevel">Heading level of the card titles.</param>
    /// <param name="nameHeader">Header of the first column of the table layout.</param>
    public static string Render(IReadOnlyList<EntryListItem> items, EntryListLayout layout, int headingLevel = 2, string nameHeader = "Name")
    {
        if (items.Count == 0)
            return "";

        var html = new StringBuilder();
        switch (layout)
        {
            case EntryListLayout.List:
                html.Append("<ul class=\"entry-list\">");
                foreach (var item in items)
                {
                    html.Append("<li>");
                    AppendLink(html, item);
                    AppendVersion(html, item);
                    if (item.Description is not null)
                    {
                        html.Append(" <span class=\"entry-list-description\">").Append(Encode(item.Description)).Append("</span>");
                    }

                    html.Append("</li>");
                }

                html.Append("</ul>");
                break;

            case EntryListLayout.Table:
                var hasVersion = items.Any(i => i.Version is not null);
                html.Append("<table class=\"api-table\"><thead><tr><th scope=\"col\">").Append(Encode(nameHeader)).Append("</th>");
                if (hasVersion)
                {
                    html.Append("<th scope=\"col\">Version</th>");
                }

                html.Append("<th scope=\"col\">Description</th></tr></thead><tbody>");
                foreach (var item in items)
                {
                    html.Append("<tr><td>");
                    AppendLink(html, item);
                    html.Append("</td>");
                    if (hasVersion)
                    {
                        html.Append("<td>").Append(Encode(item.Version)).Append("</td>");
                    }

                    html.Append("<td>").Append(Encode(item.Description)).Append("</td></tr>");
                }

                html.Append("</tbody></table>");
                break;

            default:
                var heading = "h" + Math.Clamp(headingLevel, 2, 6).ToString(System.Globalization.CultureInfo.InvariantCulture);
                html.Append("<ul class=\"section-catalog\">");
                foreach (var item in items)
                {
                    html.Append("<li class=\"section-card\"><").Append(heading).Append('>');
                    AppendLink(html, item);
                    html.Append("</").Append(heading).Append('>');
                    if (item.Version is not null)
                    {
                        html.Append("<p class=\"section-card-version\">").Append(Encode(item.Version)).Append("</p>");
                    }

                    if (item.Description is not null)
                    {
                        html.Append("<p>").Append(Encode(item.Description)).Append("</p>");
                    }

                    html.Append("</li>");
                }

                html.Append("</ul>");
                break;
        }

        return html.ToString();
    }

    private static void AppendLink(StringBuilder html, EntryListItem item)
    {
        html.Append("<a href=\"").Append(Encode(item.Url)).Append("\">").Append(Encode(item.Title)).Append("</a>");
    }

    private static void AppendVersion(StringBuilder html, EntryListItem item)
    {
        if (item.Version is not null)
        {
            html.Append(" <span class=\"entry-list-version\">").Append(Encode(item.Version)).Append("</span>");
        }
    }

    private static string Encode(string? value) => value is null ? "" : HtmlEncoding.Encode(value);
}
