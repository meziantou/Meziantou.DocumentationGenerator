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

Components are rendered sequentially to keep access to shared site services and diagnostic ordering deterministic.
Each page's Razor renderer is disposed after its HTML is captured, so component trees do not accumulate for the entire
site. HTML minification runs in batches of at most four pages (limited by the available processor count). Link extraction
and output writes remain sequential, and the number of HTML documents being minified at once is bounded.

Run `Meziantou.DocumentationGenerator build --verbosity detailed` to see stage durations and the page-rendering breakdown:
component rendering, link extraction, minification, and file writing. Minification time is elapsed time for the batches,
not the sum of worker times.
