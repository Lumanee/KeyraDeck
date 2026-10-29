package com.phonedeck.app

import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.min
import kotlinx.coroutines.delay
import java.text.Normalizer

/**
 * Police 5×7 de l'afficheur : 5 colonnes par caractère (bit 0 = ligne du haut).
 * Même police que dans le configurateur du PC (config.html : LED_FONT).
 */
private const val LedChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-.!?/':+&#()_"
private const val LedData =
    "7e0909097e7f494949363e414141227f4141413e7f494949417f090909013e4149497a7f0808087f00417f41002040413f017f081422417f404040407f020c027f" +
        "7f0408107f3e4141413e7f090909063e4151215e7f09192946464949493101017f01013f4040403f1f2040201f3f4038403f6314081463030478040361514945433e" +
        "5149453e00427f400042615149462141454b311814127f1027454545393c4a49493001710905033649494936064949291e0808080808006060000000005f0000020151" +
        "090620100804020004030000003636000008083e08083649552250147f147f14001c2241000041221c004040404040"

/** Les colonnes de points d'un texte (majuscules, sans accents ; un caractère inconnu devient un espace). */
private fun ledColumns(text: String): IntArray {
    val clean = Normalizer.normalize(text, Normalizer.Form.NFD).replace(Regex("\\p{Mn}+"), "").uppercase().trim()
    val cols = ArrayList<Int>()
    for (c in clean) {
        val i = LedChars.indexOf(c)
        if (i < 0) {
            repeat(3) { cols.add(0) }
            continue
        }
        for (x in 0 until 5) cols.add(LedData.substring((i * 5 + x) * 2, (i * 5 + x) * 2 + 2).toInt(16))
        cols.add(0)
    }
    if (cols.isNotEmpty()) cols.removeAt(cols.size - 1)
    return cols.toIntArray()
}

/**
 * Mini écran LED à points : le nom de la page en lettres lumineuses (couleur d'accent du thème).
 * Le texte s'allume de gauche à droite quand il change, et défile s'il est trop long.
 */
@Composable
fun LedDisplay(text: String, height: Dp, modifier: Modifier = Modifier) {
    val accent = LocalTheme.current.accent
    val glyphs = remember(text) { ledColumns(text) }
    val pitch = height / 9f
    val shape = RoundedCornerShape(height * 0.22f)
    val reveal = remember { Animatable(1f) }
    val scroll = remember { Animatable(0f) }
    LaunchedEffect(text) {
        reveal.snapTo(0f)
        reveal.animateTo(1f, tween(420, easing = LinearEasing))
    }
    BoxWithConstraints(modifier) {
        val wanted = pitch * (glyphs.size + 4)
        val w = min(maxWidth, wanted)
        val visible = ((w / pitch).toInt() - 2).coerceAtLeast(1)
        val scrolls = glyphs.size > visible
        val loop = glyphs.size + 8
        LaunchedEffect(text, scrolls) {
            scroll.snapTo(0f)
            if (!scrolls) return@LaunchedEffect
            while (true) {
                delay(1400)
                scroll.animateTo(loop.toFloat(), tween(loop * 70, easing = LinearEasing))
                scroll.snapTo(0f)
            }
        }
        Canvas(
            Modifier
                .width(w)
                .height(height)
                .clip(shape)
                .background(Color(0xFF050506))
                .border(1.dp, Color(0x1FFFFFFF), shape),
        ) {
            val p = size.height / 9f
            val n = (size.width / p).toInt()
            val left = (size.width - n * p) / 2f
            val r = p * 0.36f
            val off = accent.copy(alpha = 0.09f)
            val glow = accent.copy(alpha = 0.22f)
            val start = if (scrolls) 1 - scroll.value.toInt() else (n - glyphs.size) / 2
            val lit = (reveal.value * n).toInt()
            for (cx in 0 until n) {
                var bits = 0
                if (cx < lit) {
                    var gi = cx - start
                    if (scrolls) gi = ((gi % loop) + loop) % loop
                    if (gi in glyphs.indices) bits = glyphs[gi]
                }
                for (y in 0 until 7) {
                    val c = Offset(left + (cx + 0.5f) * p, (y + 1.5f) * p)
                    if (bits shr y and 1 == 1) {
                        drawCircle(glow, r * 1.9f, c)
                        drawCircle(accent, r, c)
                    } else {
                        drawCircle(off, r, c)
                    }
                }
            }
        }
    }
}
