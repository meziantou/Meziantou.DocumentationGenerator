# Performance budgets

The test suite measures a synthetic corpus of 1,000 types with 12 members each, built for two target frameworks, and
fails when a budget is exceeded:

| Measure | Budget | Typical value |
| --- | --- | --- |
| Build time (in-process, warm) | 3 minutes | ~3 seconds |
| Average type page size | 120 KB | ~22 KB |
| Search index size | 3 MB | ~1.7 MB (about 13,000 entries) |

The search index is loaded on the first interaction with the search box. Queries scan the index in memory; ranking rules
and a 20,000-entry index size budget (160 bytes per entry) are tested with the JavaScript engine of the tests, and
search is checked interactively in a browser.

API navigation only lists the types of the current namespace, so the size of a page does not grow with the number of
namespaces of a package.

## Page rendering

Components are rendered sequentially to keep access to shared site services deterministic. Each page's Razor renderer
is disposed after its HTML is captured, so component trees do not accumulate for the entire site. The other steps of a
page (link extraction, minification and file writing) are independent: they run on one thread per processor while the
next pages are rendered, and the number of HTML documents waiting for them is bounded. The tool uses the server garbage
collector, as these steps allocate a lot on several threads.

Links are validated once per directory: the pages of a directory share most of their links (e.g. the navigation), so a
link is resolved once and each page only keeps a reference to it.

The build prints the duration of each stage and a breakdown of the page rendering: the time spent in each step for each
kind of page (summed across threads), the size of the rendered and minified markup, and the slowest pages. Use
`--verbosity minimal` to hide them.
