namespace Meziantou.DocumentationGenerator.Tests.Infrastructure;

/// <summary>A representative site for browser tests: authored pages, a table of contents, and multi-target packages.</summary>
internal static class UiSite
{
    public static async Task<SiteBuilder> BuildAsync(string[] arguments)
    {
        var site = await SiteBuilder.BuildAsync(
            [ApiModelTests.CreateKitchenSinkPackage()],
            files: new()
            {
                ["docs/index.md"] = """
                    ---
                    title: Welcome
                    ---

                    # Welcome

                    Read the [guide](guide.md) or browse [the API](cref:T:Contoso.Library.Client).
                    """,
                ["docs/guide.md"] = """
                    # Guide

                    ## Install

                    ```bash
                    dotnet add package Contoso.Library
                    ```

                    ## Usage

                    ```csharp
                    var client = new Contoso.Library.Client("a-very-long-name-that-makes-the-line-much-longer-than-a-phone-screen-can-display-without-scrolling");
                    ```

                    | Column | Description |
                    | ------ | ----------- |
                    | A | AVeryLongValueWithoutAnySpaceThatWouldOtherwiseOverflowTheLayoutOnNarrowScreensAndBreakTheReadingExperience |

                    > [!NOTE]
                    > A note.
                    """,
                ["docs/toc.yml"] = """
                    - href: index.md
                    - href: guide.md
                    - name: API
                      href: api:contoso.library
                    """,
            },
            arguments: arguments);
        if (site.Result.ExitCode != 0)
            throw new InvalidOperationException("The UI site cannot be built:\n" + site.Result.AllOutput);

        return site;
    }
}
