# Operator Experience and Visual Direction

## Experience principles

- Design first for a non-technical parcel operator working repeatedly and under time pressure.
- Use plain operational language rather than implementation terminology.
- Show what happened, why it happened, and what the operator can do next.
- Prevent invalid submission early without erasing entered data.
- Use progressive disclosure so common work stays simple while details remain available.
- Use familiar controls, visible labels, keyboard access, clear focus, and accessible status messages.

## Core flows

### Route one parcel

1. Enter weight and value.
2. Select destination country from a searchable country control.
3. Add optional attributes only when needed.
4. Review validation inline.
5. Submit and receive the intended department, approval state, rule version, and plain-language reason.

### Upload a batch

1. Select a supported XML file.
2. Select a manifest country when rows do not contain one.
3. Submit for secure server validation.
4. If the same manifest and fallback were imported before, review the prior
   batch or explicitly choose `Import again`.
5. Follow durable progress without keeping the request open.
6. Read `Evaluated` separately from insurance approval.
7. Reopen a recent persisted batch after navigation or refresh.

### Approve insurance

1. Open the oldest unresolved high-value decision.
2. Review EUR value, destination, intended department, rule version, and
   explanations.
3. An InsuranceApprover records approval; an ordinary operator remains
   view-only.
4. The drawer shows append-only evidence and the queue refreshes immediately.

### Change rules

1. Create a draft through controlled fields rather than unrestricted code.
2. Validate the draft.
3. Review simulated changes.
4. Activate with authorization and an audit reason.
5. Monitor and roll back if needed.
6. Resume a persisted Draft after navigation. Editing a stored definition
   creates a new immutable version rather than rewriting audited history.

## Shipped visual direction

The foundation interface now uses a bright, high-density operations-console style:

| Purpose | Shipped token |
| --- | --- |
| App background | `#EEF4EF` |
| Surface | `#FFFFFF` |
| Primary text | `#17231D` |
| Secondary text | `#68736D` |
| Border | `#DFE7E1` |
| Primary action | `#08754D` |
| Primary hover | `#075C3E` |
| Soft green surface | `#E3F4E9` |
| Warning | `#9C5B12` |
| Error | `#B23B37` |

- Manrope is the interface typeface.
- IBM Plex Mono is reserved for weights, values, counts, and rule conditions.
- Phosphor supplies the single icon family.
- Cards use 13 to 14 pixel radii; controls use 9 to 10 pixel radii; status badges use pill geometry.
- The browser body remains fixed while the workspace scrolls with an invisible scrollbar.
- Interaction motion is limited to short state transitions and a `0.97` active press response.
- Responsive navigation becomes a compact labelled icon rail above the workspace at 780 pixels and below.
- Overview metrics share one grouped status surface on desktop and separate into stacked cards on narrow screens.
- General operator text is 14-16 pixels where space permits; table and activity
  content is 13-14 pixels; metadata is at least 12 pixels; table headers and
  status badges are at least 11 pixels.
- Friendly reasons and rule outcomes lead. Technical identifiers remain in the
  final table column or expandable detail.

## Verified interaction evidence

- Overview leads with the all-time decision total and keeps the current UTC-day
  count as supporting context.
- Decision history defaults to the newest 10. Operators can select 24 hours,
  7 days, 30 days, 12 months, or all time; longer results use 15-item pages.
- Activity uses the same constrained time windows and 15-item pages. The
  unresolved Insurance queue is oldest-first and paged at 15.
- Overview decision rows open one persisted decision.
- Import results show `Value (EUR)`, country name/code, department, approval,
  processing status, and a detail action.
- Recent imports are bounded and reopen a batch after navigation and refresh.
- Activity uses human labels and opens exact related decisions, batches, or
  rule versions; technical event identifiers are secondary details.
- Every Overview KPI is an explicit action: decisions move to history,
  approvals open Insurance, and import issues/queue rows open a dedicated
  privacy-safe attention panel in Import XML.
- Decision history filters by department or approval state, while Activity
  filters by Imports, Routing, Insurance, or Rules. Filtering is server-side
  before page counts are calculated.
- Import activity summarizes validation issues in plain language; stable event,
  correlation, subject, and error identifiers remain secondary support detail.
- Import XML uses two internal task views instead of one long mixed workflow:
  `New import` contains source selection and its immediate result, while
  `Operations & history` contains issue recovery, queue state, and durable
  batches.
- Decision and Activity filters use the product's own accessible popover
  treatment rather than browser-native option menus.
- Failed rows lead with `Needs correction` or `Processing failed`, explain the
  next action, and keep durable state/error codes secondary. Historical batches
  are never presented as editable.
- Rules expose only typed boundaries and enforce simulation before activation.
- A saved Draft is restored automatically, and every historical version can be
  used as the typed starting point for a new immutable draft.
- At 1024, 1280, and 1440 CSS-pixel desktop widths, the document has no
  horizontal overflow at browser scale 1.0.
- Final packaged-browser verification produced zero console errors or warnings.

This is a focused implementation review, not a full WCAG conformance audit.
Dedicated screen-reader, contrast measurement, high zoom, and complete keyboard
journey evidence remain optional follow-up work.

## Explicitly avoided

- Dark purple AI-dashboard styling.
- Decorative gradients as the primary visual system.
- Glassmorphism and blurred panels.
- Oversized empty hero sections.
- Unexplained icons.
- Raw configuration as the default administrator interface.
- Color-only status communication.
- Fabricated customer names, avatars, notifications, or production metrics.
- A custom local username and password account system.
