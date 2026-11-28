#!/usr/bin/env python3
"""Center all wrapped lyric rows in full Lyrics mode, not Cinema or popup."""
from pathlib import Path
import sys
root = Path(sys.argv[1]) / "app/src/main/java/com/melisma/app/ui/lyrics"

def edit(name, pairs):
    file = root / name
    s = file.read_text(encoding="utf-8")
    for old, new, count in pairs:
        n = s.count(old)
        if n != count:
            raise RuntimeError(f"{name}: expected {count}, got {n}: {old[:100]}")
        s = s.replace(old, new)
    file.write_text(s, encoding="utf-8")

edit("LyricsView.kt", [
    ("import com.melisma.app.settings.Settings", "import com.melisma.app.settings.Settings\nimport com.melisma.app.settings.ViewMode", 1),
    ("        val layout = remember(\n", """        val centerLyrics = settings.viewMode == ViewMode.LYRICS && !tight
        val layout = remember(
            centerLyrics,
""", 1),
    ("                furiganaHiragana = furiganaHiragana,", """                furiganaHiragana = furiganaHiragana,
                centered = centerLyrics,""", 1),
])
edit("LyricsLayout.kt", [
    ("    val widthPx: Float,\n) {", "    val widthPx: Float,\n    val centered: Boolean = false,\n) {", 1),
    ("        furiganaHiragana: Boolean? = null,\n    ): LyricsLayout {", """        furiganaHiragana: Boolean? = null,
        centered: Boolean = false,
    ): LyricsLayout {""", 1),
    ("        val insetPx = if (hasDuet && duetPadding) widthPx * 0.15f else widthPx * 0.05f", """        val insetPx = if (!centered && hasDuet && duetPadding) widthPx * 0.15f else widthPx * 0.05f
        val centerWidth = if (centered) widthPx else null""", 1),
    ("interludeLayout(line, index, metrics, maxWidth, alignRight, y)", "interludeLayout(line, index, metrics, maxWidth, alignRight, y, centerWidth)", 1),
    ("                        if (rubyRoom) furiganaHiragana else null,", """                        if (rubyRoom) furiganaHiragana else null,
                        centerWidth,""", 1),
    ("                    furiganaHiragana = if (rubyRoom) furiganaHiragana else null,", """                    furiganaHiragana = if (rubyRoom) furiganaHiragana else null,
                    centerWidth = centerWidth,""", 1),
    ("buildFooter(document, metrics, widthPx - insetPx, y)", "buildFooter(document, metrics, widthPx - insetPx, y, centerWidth)", 1),
    ("LyricsLayout(document, out, footer, y + footerHeight, metrics, widthPx)", "LyricsLayout(document, out, footer, y + footerHeight, metrics, widthPx, centered)", 1),
    ("        top: Float,\n    ): List<FooterRow> {", "        top: Float,\n        centerWidth: Float?,\n    ): List<FooterRow> {", 1),
    ("                    x = 0f,\n                    baseline = y - metrics.creditsFontSizePx", """                    x = rowOrigin(maxWidth, metrics.creditsPaint.measureText(row), false, centerWidth),
                    baseline = y - metrics.creditsFontSizePx""", 1),
    ("                    x = 0f,\n                    baseline = y - metrics.providerFontSizePx", """                    x = rowOrigin(maxWidth, metrics.providerPaint.measureText(row), false, centerWidth),
                    baseline = y - metrics.providerFontSizePx""", 1),
    ("        furiganaHiragana: Boolean?,\n    ): LineLayout {", "        furiganaHiragana: Boolean?,\n        centerWidth: Float?,\n    ): LineLayout {", 1),
    ("        furiganaHiragana: Boolean? = null,\n    ): LineLayout {", "        furiganaHiragana: Boolean? = null,\n        centerWidth: Float? = null,\n    ): LineLayout {", 1),
    ("        top: Float,\n    ): LineLayout {", "        top: Float,\n        centerWidth: Float?,\n    ): LineLayout {", 1),
    ("        original: String? = null,\n    ): List<TextRow> {", "        original: String? = null,\n        centerWidth: Float? = null,\n    ): List<TextRow> {", 1),
    ("if (alignRight) maxWidth - widthOfRow else 0f", "rowOrigin(maxWidth, widthOfRow, alignRight, centerWidth)", 1),
    ("if (alignRight) maxWidth - rowWidth else 0f", "rowOrigin(maxWidth, rowWidth, alignRight, centerWidth)", 1),
    ("if (alignRight) maxWidth - width else 0f", "rowOrigin(maxWidth, width, alignRight, centerWidth)", 2),
    ("if (alignRight) maxWidth - totalWidth else 0f", "rowOrigin(maxWidth, totalWidth, alignRight, centerWidth)", 1),
    ("            line, metrics, maxWidth, alignRight, contentHeight, showTranslation,", """            line, metrics, maxWidth, alignRight, contentHeight, showTranslation,
            centerWidth = centerWidth,""", 2),
])
# Scale around the actual center of the viewport, not the left margin.
# Text and all marker positions come from the same pre-centered unit geometry.
edit("LyricsRenderer.kt", [
    ("        val pivotX = if (line.alignRight) line.rightEdge() else 0f", """        val pivotX = if (layout.centered) layout.widthPx / 2f
            else if (line.alignRight) line.rightEdge() else 0f""", 1),
    ("        boxRect.set(-padX, -padY, line.rightEdge() + padX, line.contentHeight + padY)", """        val left = if (layout.centered) line.units.minOfOrNull { it.x } ?: 0f else 0f
        boxRect.set(left - padX, -padY, line.rightEdge() + padX, line.contentHeight + padY)""", 1),
    ("    private fun LineLayout.contentWidthCentre(): Float = rightEdge() / 2f", """    private fun LineLayout.contentWidthCentre(): Float =
        if (layout.centered) layout.widthPx / 2f else rightEdge() / 2f""", 1),
])
(root / "CenteredLyricRows.kt").write_text('''package com.melisma.app.ui.lyrics

/** Position each row independently inside the complete content width. */
internal fun rowOrigin(maxWidth: Float, rowWidth: Float, alignRight: Boolean, centerWidth: Float?): Float =
    if (centerWidth != null) (centerWidth - rowWidth) / 2f
    else if (alignRight) maxWidth - rowWidth else 0f
''', encoding="utf-8")
test = Path(sys.argv[1]) / "app/src/test/java/com/melisma/app/ui/lyrics/CenteredLyricRowsTest.kt"
test.parent.mkdir(parents=True, exist_ok=True)
test.write_text('''package com.melisma.app.ui.lyrics

import org.junit.Assert.assertEquals
import org.junit.Test

class CenteredLyricRowsTest {
    @Test fun wrappedRowsUseFullViewportCenter() {
        for (width in listOf(120f, 300f, 900f)) {
            val x = rowOrigin(950f, width, false, 1000f)
            assertEquals(500f, x + width / 2f, 0.001f)
            assertEquals(x, rowOrigin(950f, width, true, 1000f), 0.001f)
        }
    }
    @Test fun legacyCinemaAndCompactAlignmentUnchanged() {
        assertEquals(0f, rowOrigin(950f, 300f, false, null), 0.001f)
        assertEquals(650f, rowOrigin(950f, 300f, true, null), 0.001f)
    }
}
''', encoding="utf-8")
print("Centered Lyrics mode: wrapped rows, timed units, translations, interlude dots and credits.")
