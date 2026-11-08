namespace Meziantou.DocumentationGenerator.Tests.Infrastructure;

/// <summary>A representative site for browser tests: authored pages and multi-target packages.</summary>
internal static class UiSite
{
    public static async Task<SiteBuilder> BuildAsync(string[] arguments)
    {
        var site = await SiteBuilder.BuildAsync(
            [
                ApiModelTests.CreateKitchenSinkPackage(),

                // A package without API: it has no page, and it draws an arrow in the dependency graph
                new TestPackageBuilder("Contoso.Tool", "1.0.0")
                    .WithNuspecMetadata("""<dependencies><group targetFramework="net8.0"><dependency id="Contoso.Library" version="1.0.0" /><dependency id="Newtonsoft.Json" version="13.0.1" /></group></dependencies>""")
                    .AddFile("tools/readme.txt", "Contoso.Tool"),
            ],
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
                ["docs/dependencies.md"] = """
                    # Dependencies

                    <DependencyGraph Packages="*" External="true" />
                    """,
            },
            arguments: arguments);
        if (site.Result.ExitCode != 0)
            throw new InvalidOperationException("The UI site cannot be built:\n" + site.Result.AllOutput);

        return site;
    }
}
