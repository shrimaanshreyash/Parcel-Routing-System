# Privacy-safe XML verification fixtures

These files contain routing facts and synthetic parser-boundary markers only.
They contain no real recipient, address, contact, payment, or private
source-manifest data.

| Fixture | Expected boundary |
| --- | --- |
| `01-valid-boundaries.xml` | Document accepted; all five rows evaluated at routing and insurance boundaries. |
| `02-valid-variations.xml` | Document accepted; element order and either country name are supported, missing country uses the operator fallback, and recipient content is discarded. Dedicated parser and API tests also accept both `Receipient` and `Recipient`. |
| `03-mixed-row-errors.xml` | Document accepted; two valid rows continue and four invalid rows are retained as row-level failures (three parser-shape failures and one domain-value failure). |
| `04-invalid-country.xml` | Document accepted; the unsupported ISO code is one row-level failure while valid siblings continue. |
| `05-malformed.xml` | Whole document rejected before a batch is created. |
| `06-unsupported-structure.xml` | Whole document rejected because the root contract is unsupported. |
| `07-xxe.xml` | Whole document rejected because DTD processing and external entities are prohibited. |
| `08-duplicate-retry.xml` | Both identical rows are valid work; an identical request key replays one batch, a new key warns, and explicit confirmation creates a new batch. |
| `09-reference-corpus.xml` | Seventeen privacy-safe routing fact rows, including valid duplicates, exercise the complete import worker without source recipient data. |
| `generated/09-over-2mb.xml` | HTTP upload rejected at the 2 MiB request boundary; the parser also enforces its independent 2,000,000-character ceiling. |
| `generated/10-over-2000000-characters.xml` | Whole document rejected by the parser character ceiling while remaining below 2 MiB. |
| `generated/11-over-10000-rows.xml` | Whole document rejected after the 10,000-row ceiling. |

Run `Generate-LimitFixtures.ps1` to create the three intentionally large local
fixtures. Generated files are verification artifacts and are not source data.

Document-level failures mean no durable batch is created because the uploaded
document cannot be processed safely or unambiguously. Row-level failures mean
the document contract is valid, a durable batch is created, and only the
affected parcel rows are marked failed so valid siblings can continue.
