#!/usr/bin/env python3
"""Add optional visual word effects without modifying the existing timing mode."""
from pathlib import Path
import sys
root = Path(sys.argv[1]) / "app/src/main/java/com/melisma/app"

def edit(path, pairs):
    f = root / path
    s = f.read_text(encoding="utf-8")
    for old, new, count in pairs:
        n = s.count(old)
        if n != count:
            raise RuntimeError(f"{path}: expected {count}, got {n}: {old[:100]}")
        s = s.replace(old, new)
    f.write_text(s, encoding="utf-8")

edit("settings/SettingsStore.kt", [
    ("enum class TextAnimationStyle(", '''enum class WordEffect(val label: String) {
    ENLARGEMENT("Enlargement"),
    BOUNCING_BALL("Bouncing ball"),
    UNDERLINE("Underline"),
    GLOW("Glow"),
    HIGHLIGHT_ONLY("Highlight only"),
    RANDOM("Random"),
}

enum class TextAnimationStyle(''', 1),
    ("    val textAnimationStyle: TextAnimationStyle = TextAnimationStyle.CALCULATE,", """    val textAnimationStyle: TextAnimationStyle = TextAnimationStyle.CALCULATE,
    val wordEffect: WordEffect = WordEffect.ENLARGEMENT,""", 1),
    ("        textAnimationStyle = prefs.enum(KEY_TEXT_ANIMATION, TextAnimationStyle.CALCULATE),", """        textAnimationStyle = prefs.enum(KEY_TEXT_ANIMATION, TextAnimationStyle.CALCULATE),
        wordEffect = prefs.enum(KEY_WORD_EFFECT, WordEffect.ENLARGEMENT),""", 1),
    ("    fun setTextAnimationStyle(style: TextAnimationStyle) =", """    fun setWordEffect(effect: WordEffect) = edit { putString(KEY_WORD_EFFECT, effect.name) }

    fun setTextAnimationStyle(style: TextAnimationStyle) =""", 1),
    ('        const val KEY_TEXT_ANIMATION = "text_animation_style"', '''        const val KEY_TEXT_ANIMATION = "text_animation_style"
        const val KEY_WORD_EFFECT = "word_effect"''', 1),
])
edit("ui/SettingsSheet.kt", [
    ("import com.melisma.app.settings.TextAnimationStyle", "import com.melisma.app.settings.TextAnimationStyle\nimport com.melisma.app.settings.WordEffect", 1),
    ('''                ChipGroup(
                    label = "Text animation",''', '''                ChipGroup(
                    label = "Animation style",
                    options = WordEffect.entries.map { it to it.label },
                    selected = settings.wordEffect,
                    accent = accent,
                    onSelect = { store.setWordEffect(it) },
                )
                Hint("Random chooses one moving effect per song. Word effects use real syllable timings.")
                ChipGroup(
                    label = "Text animation",''', 1),
])
# Shared resolver for main display and popup. Track identity is the recording
# key already used for per-song/provider timing; source or layout changes don't reroll.
f = root / "ui/lyrics/WordEffects.kt"
f.write_text('''package com.melisma.app.ui.lyrics

import com.melisma.app.settings.WordEffect
import kotlin.random.Random
import kotlin.math.sin

internal class WordEffectPicker(private val next: (Int) -> Int = { Random.nextInt(it) }) {
    private var key: String? = null
    private var selected = WordEffect.ENLARGEMENT
    private val pool = listOf(WordEffect.ENLARGEMENT, WordEffect.BOUNCING_BALL,
        WordEffect.UNDERLINE, WordEffect.GLOW)

    fun resolve(setting: WordEffect, track: String): WordEffect {
        if (setting != WordEffect.RANDOM) return setting
        if (key != track) {
            val candidates = if (key == null) pool else pool.filter { it != selected }
            selected = candidates[next(candidates.size)]
            key = track
        }
        return selected
    }
}

internal val sharedWordEffectPicker = WordEffectPicker()

internal fun effectEase(p: Float): Float {
    val t = p.coerceIn(0f, 1f)
    return t * t * (3f - 2f * t)
}

internal fun effectLift(p: Float): Float {
    val s = sin(Math.PI * p.coerceIn(0f, 1f)).toFloat()
    return s * s
}
''', encoding="utf-8")

edit("ui/lyrics/LyricsView.kt", [
    ("    freeRead: Boolean = false,\n) {", "    freeRead: Boolean = false,\n    animationTrackKey: String? = null,\n) {", 1),
    ("            renderer.animationStyle = settings.textAnimationStyle", """            renderer.animationStyle = settings.textAnimationStyle
            renderer.wordEffect = sharedWordEffectPicker.resolve(settings.wordEffect,
                animationTrackKey ?: "unidentified")""", 1),
])
edit("ui/PlayerScreen.kt", [
    ("                        document = document,\n                        settings = settings,\n                        positionMsProvider = positionProvider,", """                        document = document,
                        settings = settings,
                        animationTrackKey = snapshot.track?.correctionKey,
                        positionMsProvider = positionProvider,""", 1),
    ("                    document = document,\n                    settings = settings,\n                    positionMsProvider = positionProvider,", """                    document = document,
                    settings = settings,
                    animationTrackKey = snapshot.track?.correctionKey,
                    positionMsProvider = positionProvider,""", 1),
])
edit("ui/lyrics/LyricsRenderer.kt", [
    ("import com.melisma.app.settings.TextAnimationStyle", "import com.melisma.app.settings.TextAnimationStyle\nimport com.melisma.app.settings.WordEffect", 1),
    ("    var layout: LyricsLayout = layout", "    var wordEffect: WordEffect = WordEffect.ENLARGEMENT\n\n    var layout: LyricsLayout = layout", 1),
    ('''            for (unit in line.units) {
                drawActiveUnit(
                    canvas, unit, paintForLine, fontSize, positionMs, delta, opacity * roleAlpha,
                )
            }''', '''            if (!lineIsWordless && wordEffect != WordEffect.ENLARGEMENT) {
                for (unit in line.units) {
                    drawEffectUnit(canvas, unit, paintForLine, fontSize, positionMs, opacity * roleAlpha)
                }
                drawWordMarker(canvas, line, fontSize, positionMs, opacity * roleAlpha)
            } else {
                for (unit in line.units) {
                    drawActiveUnit(
                        canvas, unit, paintForLine, fontSize, positionMs, delta, opacity * roleAlpha,
                    )
                }
            }''', 1),
    ("    private fun drawActiveUnit(", '''    // Same timed gradient and original glyph geometry, no layout/transport changes.
    private fun drawEffectUnit(
        canvas: Canvas, unit: PlacedUnit, source: Paint, fontSize: Float,
        positionMs: Int, opacity: Float,
    ) {
        val state = LyricsAnim.stateOf(positionMs, unit.startMs, unit.endMs)
        if (state != LyricsAnim.State.ACTIVE) unit.sweepStartedAt = 0L
        val progress = if (state == LyricsAnim.State.ACTIVE) sweepProgress(unit, positionMs)
            else LyricsAnim.progressOf(positionMs, unit.startMs, unit.endMs)
        val gradient = when (state) {
            LyricsAnim.State.ACTIVE -> GRADIENT_START + 120f * progress
            LyricsAnim.State.NOT_SUNG -> GRADIENT_START
            LyricsAnim.State.SUNG -> 100f
        }
        paint.reset()
        paint.isAntiAlias = true
        paint.isSubpixelText = true
        paint.textSize = source.textSize
        paint.typeface = source.typeface
        paint.shader = fillShader(unit, gradient, opacity)
        if (wordEffect == WordEffect.GLOW && state == LyricsAnim.State.ACTIVE) {
            val strength = 0.32f + 0.68f * effectLift(progress)
            paint.setShadowLayer(fontSize * (0.09f + 0.14f * strength), 0f, 0f,
                whiteWithAlpha(opacity * strength * 0.85f))
        }
        canvas.drawText(unit.text, unit.x, unit.baseline, paint)
        paint.shader = null
        paint.clearShadowLayer()
    }

    private fun drawWordMarker(
        canvas: Canvas, line: LineLayout, fontSize: Float, positionMs: Int, opacity: Float,
    ) {
        if (wordEffect != WordEffect.BOUNCING_BALL && wordEffect != WordEffect.UNDERLINE) return
        val i = line.units.indexOfFirst {
            LyricsAnim.stateOf(positionMs, it.startMs, it.endMs) == LyricsAnim.State.ACTIVE
        }
        if (i < 0) return
        val unit = line.units[i]
        val progress = LyricsAnim.progressOf(positionMs, unit.startMs, unit.endMs)
        paint.reset()
        paint.isAntiAlias = true
        paint.color = whiteWithAlpha(opacity)
        if (wordEffect == WordEffect.BOUNCING_BALL) {
            val previous = line.units.getOrNull(i - 1)
            val from = if (previous != null && abs(previous.baseline - unit.baseline) < fontSize * 0.5f)
                previous.centerX else unit.centerX
            val x = from + (unit.centerX - from) * effectEase(progress / 0.34f)
            val radius = (fontSize * 0.085f).coerceAtLeast(2f)
            val y = unit.baseline - fontSize * (0.91f + 0.23f * effectLift(progress))
            canvas.drawCircle(x, y, radius, paint)
        } else {
            val end = effectEase(progress)
            val start = (end - 0.32f).coerceAtLeast(0f)
            if (end > start) {
                paint.strokeWidth = (fontSize * 0.055f).coerceAtLeast(1.5f)
                paint.strokeCap = Paint.Cap.ROUND
                val y = unit.baseline + fontSize * 0.10f
                canvas.drawLine(unit.x + unit.width * start, y, unit.x + unit.width * end, y, paint)
            }
        }
    }

    private fun drawActiveUnit(''', 1),
])
test = Path(sys.argv[1]) / "app/src/test/java/com/melisma/app/ui/lyrics/WordEffectsTest.kt"
test.parent.mkdir(parents=True, exist_ok=True)
test.write_text('''package com.melisma.app.ui.lyrics

import com.melisma.app.settings.WordEffect
import org.junit.Assert.*
import org.junit.Test

class WordEffectsTest {
    @Test fun randomStableAndDoesNotRepeat() {
        val picker = WordEffectPicker { 0 }
        val a = picker.resolve(WordEffect.RANDOM, "one")
        assertEquals(a, picker.resolve(WordEffect.RANDOM, "one"))
        val b = picker.resolve(WordEffect.RANDOM, "two")
        assertNotEquals(a, b)
        assertEquals(WordEffect.GLOW, picker.resolve(WordEffect.GLOW, "two"))
        assertEquals(b, picker.resolve(WordEffect.RANDOM, "two"))
        assertNotEquals(WordEffect.HIGHLIGHT_ONLY, b)
    }
    @Test fun motionClamped() {
        assertEquals(0f, effectEase(-1f), 0.001f)
        assertEquals(1f, effectEase(2f), 0.001f)
        assertEquals(1f, effectLift(0.5f), 0.001f)
        assertEquals(0f, effectLift(1f), 0.001f)
    }
}
''', encoding="utf-8")
print("Native Android visual word animations and saved setting patched.")
