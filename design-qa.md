# App Expose Dark Scrim and Large Preview Design QA

## Comparison target

- Multi-window source: `C:\Users\matej\AppData\Local\Temp\codex-clipboard-1ec7c2c8-a1b5-433f-9a6f-f3ec9d9f30a9.png`.
- Single-window source: `C:\Users\matej\AppData\Local\Temp\codex-clipboard-b78f9d7d-356e-4b8d-bc7d-e5405ef65d36.png`.
- Multi-window implementation: `C:\Users\matej\.codex\visualizations\2026\08\06\019fd96a-65da-71e2-a926-14eb8ba5ba5d\implementation-dark-large-fixture-three.png`.
- Single-window implementation: `C:\Users\matej\.codex\visualizations\2026\08\06\019fd96a-65da-71e2-a926-14eb8ba5ba5d\implementation-dark-large-single.png`.
- Multi-window comparison: `C:\Users\matej\.codex\visualizations\2026\08\06\019fd96a-65da-71e2-a926-14eb8ba5ba5d\design-qa-dark-large-multi.png`.
- Single-window comparison: `C:\Users\matej\.codex\visualizations\2026\08\06\019fd96a-65da-71e2-a926-14eb8ba5ba5d\design-qa-dark-large-single.png`.
- Source pixels: 1912 x 1027 and 1916 x 1028. Implementation work-area capture: 1920 x 1032 physical pixels at native density.
- Density normalization: each source and implementation work area was resampled to 960 x 514 and placed side by side without browser or device framing.
- States: one selected window and three same-application windows, overview fully open, virtual-desktop strip absent.

## Findings

No actionable P0, P1, or P2 differences remain for the two requested changes.

- Fonts and typography: the accepted Segoe UI title bars, sizes, weights, truncation, and application names remain unchanged and readable at the larger scale.
- Spacing and layout rhythm: the fixed 300-pixel preview-height ceiling is gone. A single window uses up to 70% of monitor width and 78% of usable height; two-to-four-window groups use up to 92% of monitor width and 74% of usable height while preserving aspect ratios and 16-pixel gaps.
- Colors and visual tokens: the pale blue/white veil was replaced by layered `#780A0E14` and `#24000000` dark-neutral scrims. The desktop remains recognizable but subdued, while preview cards carry the visual emphasis.
- Image quality and asset fidelity: previews remain native DWM surfaces and application icons remain native window assets. Scaling does not introduce placeholder imagery or raster substitution.
- Copy and content: no app-specific labels changed; the desktop-strip labels remain absent.
- Interaction and motion: source-bound opening, reverse closing, keyboard selection, mouse selection, and shortcut toggling retain their existing behavior.

Separate detail crops were unnecessary because the requested changes concern full-screen backdrop tone and large-region utilization; both are directly judgeable in the normalized single- and multi-window full-view comparisons. Title text and preview content are also readable at this comparison size.

## Comparison history

1. The supplied captures showed a P1 readability problem: the 300-pixel height cap left most of the work area unused and made preview content unnecessarily small. They also showed the unwanted pale scrim.
2. The cap was replaced with count-aware width and height utilization, and the scrim was changed to dark neutral layers.
3. The revised app was captured in one-window and three-window states. The post-fix comparisons show materially larger readable previews, high screen utilization, preserved aspect ratios, and a clearly darkened backdrop.

## Implementation checklist

- [x] Replace the light scrim with a dark neutral scrim.
- [x] Remove the fixed preview-height ceiling.
- [x] Add count-aware one-to-four-window sizing.
- [x] Preserve aspect ratios, gaps, navigation, and reversible motion.
- [x] Add regression checks for minimum single- and three-window utilization.
- [x] Build, run, capture, compare, and visually inspect both target states.

final result: passed
