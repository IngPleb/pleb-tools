# App Expose Dark Title Bar and DPI Design QA

## Comparison target

- Source visual truth: `C:\Users\matej\AppData\Local\Temp\codex-clipboard-7c633237-ec1c-4f00-9721-00adcabe0e92.png`.
- Revised implementation: `C:\Users\matej\.codex\visualizations\2026\08\06\019fd96a-65da-71e2-a926-14eb8ba5ba5d\implementation-titlebar-per-monitor-v2.png`.
- Full selected-card comparison: `C:\Users\matej\.codex\visualizations\2026\08\06\019fd96a-65da-71e2-a926-14eb8ba5ba5d\design-qa-titlebar-full-card.png`.
- Focused title-bar comparison: `C:\Users\matej\.codex\visualizations\2026\08\06\019fd96a-65da-71e2-a926-14eb8ba5ba5d\design-qa-titlebar-white-vs-dark.png`.
- Source pixels: 1086 x 855. Implementation monitor capture: 1920 x 1032 work-area pixels within a 3840 x 1086 dual-monitor capture.
- Density normalization: the full comparison preserves each selected card's aspect ratio on equal 800 x 630 mattes. The focused comparison normalizes both title-bar crops to 960 x 50 physical pixels.
- State: one selected live preview at 96 DPI, overlay fully open, cyan focus outline visible.

## Findings

No actionable P0, P1, or P2 visual differences remain for the requested title-bar revision.

- Fonts and typography: the title uses white 12-DIP Segoe UI text with a separate one-device-pixel black shadow layer. Keeping the shadow separate avoids rasterizing or softening the foreground text.
- Spacing and layout rhythm: the accepted 29-DIP title-bar height, icon spacing, card radius, preview bounds, and focus outline remain unchanged.
- Colors and visual tokens: the former opaque light title bar is replaced with a dark `ARGB(190, 20, 23, 29)` surface. It remains slightly translucent over the overview backdrop while maintaining white-text contrast.
- Image quality and asset fidelity: the application icon remains the native window icon and the window body remains a direct DWM live thumbnail. No screenshot substitute or generated asset was introduced.
- Copy and content: the original window title is preserved with character ellipsis when space is constrained.
- Interaction and motion: selection, keyboard navigation, mouse activation, opening motion, and reverse closing motion remain unchanged.

## DPI and sharpness verification

- The mixed WPF/WinForms process declares Per-Monitor V2 awareness in its process manifest and `ApplicationHighDpiMode` build setting.
- Runtime inspection of the final overlay returned Windows `DPI_AWARENESS_PER_MONITOR_AWARE` (`AWARENESS=2`) and a current-monitor DPI of 96.
- WPF layout rounding, device-pixel snapping, display text formatting, ClearType text rendering, and rounded physical-pixel DWM destinations are enabled.
- This prevents Windows from bitmap-scaling the overlay when it moves between Full HD and high-DPI monitors. A physical 4K or mixed-DPI monitor was not available in this session, so that hardware transition remains an explicit runtime test gap.
- Enlarging a source window beyond the pixel dimensions rendered by that application can still expose normal DWM upscaling. The compositor cannot create detail absent from the source surface; this is distinct from the overlay being DPI-unaware.

The focused comparison was required because the title typography, translucency, and one-pixel shadow were too small to judge reliably in the full-card view. Both comparisons were inspected.

## Comparison history

1. The supplied capture showed a P1 theme mismatch: the custom title bar was opaque white over dark-themed applications. The report also raised a possible DPI sharpness issue.
2. The title bar was changed to translucent dark chrome with crisp white text and a separate black shadow layer. Device-pixel and text-rendering settings were added.
3. An initial property-only DPI configuration produced a system-aware window (`AWARENESS=1`) and was rejected.
4. The WPF process manifest was restored with Per-Monitor V2 plus legacy Per-Monitor fallback, the mixed WPF/WinForms analyzer conflict was documented, and runtime verification then returned per-monitor awareness (`AWARENESS=2`).
5. The post-fix full-card and focused title-bar comparisons show the dark translucent treatment, white text, preserved geometry, and sharp physical-pixel border/text alignment.

## Implementation checklist

- [x] Replace the white title bar with dark translucent chrome.
- [x] Render white title text with a non-rasterizing shadow layer.
- [x] Enable WPF layout rounding and device-pixel snapping.
- [x] Configure and runtime-check Per-Monitor DPI awareness.
- [x] Preserve DWM live previews and reversible motion.
- [x] Build, run, capture, compare, and inspect the focused title bar.
- [ ] Exercise the overlay while physically moving between monitors with different DPI scales, including a 4K display.

final result: passed
