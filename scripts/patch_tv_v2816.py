#!/usr/bin/env python3
"""Move the viewport to the next vocal while the three dots remain visible."""
from pathlib import Path
import sys
root = Path(sys.argv[1]) / "app/src/main/java/com/melisma/app/ui/lyrics"
p = root / "LyricsRenderer.kt"
s = p.read_text(encoding="utf-8")
old = """        val target = targetScrollFor(activeIndex)
        if (snapNextFrame) {"""
new = """        // Interludes have a deterministic viewport path. Finish the approach
        // before vocal onset; the normal spring then starts at the same target.
        val gap = layout.lines.getOrNull(activeIndex)
        if (gap != null && gap.isInterlude) {
            val next = layout.lines.drop(activeIndex + 1).firstOrNull {
                !it.isInterlude && it.role != LineRole.BACKGROUND
            }
            if (next != null) {
                val previous = (activeIndex - 1 downTo 0).firstOrNull {
                    !layout.lines[it].isInterlude && layout.lines[it].role != LineRole.BACKGROUND
                }
                val dot = targetScrollFor(activeIndex)
                val incoming = targetScrollFor(next.index)
                val from = previous?.let { targetScrollFor(it) } ?: dot
                scrollY = interludeFocus(lastPositionMs, gap.startMs, next.startMs,
                    from, dot, incoming).coerceIn(minScroll(), maxScroll())
                scrollSpring.snapTo(scrollY)
                snapNextFrame = false
                return
            }
        }
        val target = targetScrollFor(activeIndex)
        if (snapNextFrame) {"""
assert s.count(old)==1
s=s.replace(old,new)
# Ensure last dot visibly lands and softly fades before vocal onset.
old = """            val lift = unit.springs.yOffset.step(delta) * fontSize
            val glow = unit.springs.glow.step(delta)
            val alpha = unit.springs.opacity?.step(delta) ?: 1f"""
new = """            var lift = unit.springs.yOffset.step(delta) * fontSize
            val glow = unit.springs.glow.step(delta)
            val remaining = line.endMs - positionMs
            if (unit === line.units.lastOrNull()) {
                lift *= effectEase((remaining - 120f) / 530f)
            }
            val alpha = (unit.springs.opacity?.step(delta) ?: 1f) *
                effectEase(remaining / 110f)"""
assert s.count(old)==1
s=s.replace(old,new)
p.write_text(s, encoding="utf-8")
f=root/"InterludeFocus.kt"
f.write_text('''package com.melisma.app.ui.lyrics

/** Same two-stage playback-time curve as Windows. No change to lyric timestamps. */
internal fun interludeFocus(position: Int, start: Int, end: Int,
                           previous: Float, dots: Float, next: Float): Float {
    val approach = minOf(end - 120, maxOf(start + 480, end - 2000))
    val finish = maxOf(approach + 1, end - 120)
    return if (position < approach)
        previous + (dots - previous) * effectEase((position - start) / 480f)
    else dots + (next - dots) * effectEase((position - approach).toFloat() / (finish - approach))
}
''', encoding="utf-8")
test=Path(sys.argv[1])/"app/src/test/java/com/melisma/app/ui/lyrics/InterludeFocusTest.kt"
test.write_text('''package com.melisma.app.ui.lyrics

import org.junit.Assert.*
import org.junit.Test

class InterludeFocusTest {
    @Test fun incomingLineIsCenteredBeforeVocal() {
        assertEquals(40f, interludeFocus(1000,1000,6000,40f,130f,235f),0.001f)
        assertEquals(130f, interludeFocus(4000,1000,6000,40f,130f,235f),0.001f)
        val middle = interludeFocus(4900,1000,6000,40f,130f,235f)
        assertTrue(middle > 130f && middle < 235f)
        assertEquals(235f, interludeFocus(5880,1000,6000,40f,130f,235f),0.001f)
        assertEquals(235f, interludeFocus(6000,1000,6000,40f,130f,235f),0.001f)
    }
}
''', encoding="utf-8")
print("Playback-timed gradual interlude centering and dot landing applied.")
