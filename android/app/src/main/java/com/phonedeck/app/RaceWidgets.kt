package com.phonedeck.app

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.delay
import kotlin.math.min

// Couleurs des LED de régime, comme sur un volant de course : vert, rouge, puis bleu au moment de passer le rapport.
private val LedGreen = Color(0xFF22C55E)
private val LedRed = Color(0xFFEF4444)
private val LedBlue = Color(0xFF3B82F6)
private val LedOff = Color(0xFF15151A)
private val Throttle = Color(0xFF22C55E)
private val BrakeRed = Color(0xFFEF4444)
private val Dim = Color(0xFFBBBBC4)
private val Tabular = TextStyle(fontFeatureSettings = "tnum")

private fun gearText(g: Int) = when { g < 0 -> "R"; g == 0 -> "N"; else -> g.toString() }

/**
 * Widget « Télémétrie course » : rapport, vitesse, LED de régime, compte-tours, pédales et courbes,
 * avec les données du jeu de course envoyées par le PC (Assetto Corsa, ACC, F1…).
 */
@Composable
fun RaceWidget(widget: DeckWidget, vm: DeckViewModel) {
    val t = vm.telemetry
    // Clignotement des LED au rupteur (5 fois par seconde).
    var blink by remember { mutableIntStateOf(0) }
    val limiter = t != null && t.rev >= 100
    LaunchedEffect(limiter) { while (limiter) { delay(100); blink++ } }
    val flash = limiter && blink % 2 == 0
    Box(Modifier.fillMaxSize()) {
        if (t == null) {
            Waiting(widget)
        } else when (widget.model) {
            "telemetry-1x1" -> GearView(t, flash, Modifier.fillMaxSize())
            "telemetry-2x1" -> Row(Modifier.fillMaxSize(), verticalAlignment = Alignment.CenterVertically) {
                GearView(t, flash, Modifier.weight(1f).fillMaxHeight())
                SpeedView(t, Modifier.weight(1.3f).fillMaxHeight())
            }
            "telemetry-4x1" -> RevLeds(t.rev, flash, Modifier.fillMaxSize())
            "telemetry-1x2" -> Pedals(t, Modifier.fillMaxSize())
            "telemetry-2x2" -> RpmDial(t, flash, Modifier.fillMaxSize())
            "telemetry-4x2" -> Row(Modifier.fillMaxSize(), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                PedalTrace(vm, Modifier.weight(1f).fillMaxHeight())
                Pedals(t, Modifier.width(36.dp).fillMaxHeight())
            }
            else -> Dashboard(t, flash, vm) // telemetry-4x3
        }
        if (t?.demo == true) Text("DÉMO", color = Color(0x99FFFFFF), fontSize = 8.sp, fontWeight = FontWeight.Bold,
            modifier = Modifier.align(Alignment.TopEnd))
    }
}

@Composable
private fun Waiting(widget: DeckWidget) {
    BoxWithConstraints(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        val fs = (min(maxWidth.value, maxHeight.value) * 0.13f).coerceIn(8f, 13f)
        val text = if (widget.w <= 1) "🏁" else "🏁 En attente\ndu jeu de course"
        Text(text, color = Dim, fontSize = fs.sp, lineHeight = (fs * 1.25f).sp, textAlign = androidx.compose.ui.text.style.TextAlign.Center)
    }
}

/** Le rapport engagé, en très grand. Fond bleu qui clignote quand il faut passer le rapport. */
@Composable
private fun GearView(t: RaceTelemetry, flash: Boolean, modifier: Modifier) {
    val accent = LocalTheme.current.accent
    BoxWithConstraints(modifier.clip(RoundedCornerShape(8.dp)).background(if (flash) LedBlue else Color.Transparent), contentAlignment = Alignment.Center) {
        val fs = min(maxWidth.value * 0.8f, maxHeight.value * 0.72f)
        Text(gearText(t.gear), color = if (flash) Color.White else if (t.gear <= 0) Dim else accent,
            fontSize = fs.sp, lineHeight = fs.sp, fontWeight = FontWeight.Bold, maxLines = 1, style = Tabular)
    }
}

@Composable
private fun SpeedView(t: RaceTelemetry, modifier: Modifier) {
    BoxWithConstraints(modifier, contentAlignment = Alignment.Center) {
        val fs = min(maxWidth.value * 0.36f, maxHeight.value * 0.48f)
        Column(horizontalAlignment = Alignment.CenterHorizontally) {
            Text(t.speed.toString(), color = Color.White, fontSize = fs.sp, lineHeight = fs.sp, fontWeight = FontWeight.Bold, maxLines = 1, style = Tabular)
            Text("km/h", color = Dim, fontSize = (fs * 0.3f).coerceAtLeast(8f).sp, maxLines = 1)
        }
    }
}

/** Barre de LED de régime : 5 vertes, 5 rouges, 5 bleues ; tout clignote au rupteur. */
@Composable
private fun RevLeds(rev: Int, flash: Boolean, modifier: Modifier, count: Int = 15) {
    val lit = (rev / 100f * count + 0.001f).toInt().coerceIn(0, count)
    Canvas(modifier) {
        val gap = size.width / count * 0.18f
        val d = min((size.width - gap * (count - 1)) / count, size.height * 0.9f)
        val total = d * count + gap * (count - 1)
        val x0 = (size.width - total) / 2
        val y = size.height / 2
        for (i in 0 until count) {
            val color = when {
                rev >= 100 -> if (flash) LedBlue else LedOff
                i >= lit -> LedOff
                i < count / 3 -> LedGreen
                i < count * 2 / 3 -> LedRed
                else -> LedBlue
            }
            val cx = x0 + i * (d + gap) + d / 2
            if (color != LedOff) drawCircle(color.copy(alpha = 0.35f), d * 0.62f, Offset(cx, y)) // halo
            drawCircle(color, d / 2, Offset(cx, y))
            drawCircle(Color(0x33FFFFFF), d / 2, Offset(cx, y), style = Stroke(1f))
        }
    }
}

/** Accélérateur (vert) et frein (rouge) en deux barres verticales. */
@Composable
private fun Pedals(t: RaceTelemetry, modifier: Modifier) {
    Row(modifier, horizontalArrangement = Arrangement.spacedBy(4.dp)) {
        PedalBar(t.brake, BrakeRed, Modifier.weight(1f).fillMaxHeight())
        PedalBar(t.throttle, Throttle, Modifier.weight(1f).fillMaxHeight())
    }
}

@Composable
private fun PedalBar(value: Int, color: Color, modifier: Modifier) {
    Canvas(modifier) {
        val r = CornerRadius(size.width * 0.25f)
        drawRoundRect(Color(0xFF0A0A0D), cornerRadius = r)
        val h = size.height * value.coerceIn(0, 100) / 100f
        if (h > 0f) drawRoundRect(color, topLeft = Offset(0f, size.height - h), size = Size(size.width, h), cornerRadius = r)
    }
}

/** Les 8 dernières secondes d'accélérateur et de frein, en courbes qui défilent comme dans SimHub. */
@Composable
private fun PedalTrace(vm: DeckViewModel, modifier: Modifier) {
    val samples = 240 // 8 s à 30 images par seconde
    val thr = remember { FloatArray(samples) }
    val brk = remember { FloatArray(samples) }
    var head by remember { mutableIntStateOf(0) }
    val current by rememberUpdatedState(vm.telemetry)
    LaunchedEffect(Unit) {
        while (true) {
            val t = current
            val i = head % samples
            thr[i] = (t?.throttle ?: 0) / 100f
            brk[i] = (t?.brake ?: 0) / 100f
            head++
            delay(33)
        }
    }
    Canvas(modifier.clip(RoundedCornerShape(6.dp)).background(Color(0xFF0A0A0D))) {
        // Lignes à 25 %, 50 % et 75 %.
        for (k in 1..3) drawLine(Color(0x22FFFFFF), Offset(0f, size.height * k / 4f), Offset(size.width, size.height * k / 4f), strokeWidth = 1f)
        val n = min(head, samples)
        if (n < 2) return@Canvas
        val step = size.width / (samples - 1)
        val pad = 2.dp.toPx()
        val h = size.height - pad * 2
        fun trace(values: FloatArray, color: Color) {
            val path = Path()
            for (j in 0 until n) {
                // La plus récente à droite.
                val v = values[(head - n + j).mod(samples)]
                val x = size.width - (n - 1 - j) * step
                val y = pad + h * (1f - v)
                if (j == 0) path.moveTo(x, y) else path.lineTo(x, y)
            }
            drawPath(path, color, style = Stroke(2.dp.toPx(), cap = StrokeCap.Round))
        }
        trace(thr, Throttle)
        trace(brk, BrakeRed)
    }
}

/** Compte-tours rond : régime sur l'arc (zone rouge à la fin), rapport au centre, vitesse dessous. */
@Composable
private fun RpmDial(t: RaceTelemetry, flash: Boolean, modifier: Modifier) {
    val accent = LocalTheme.current.accent
    BoxWithConstraints(modifier, contentAlignment = Alignment.Center) {
        val side = min(maxWidth.value, maxHeight.value)
        val f = if (t.maxRpm > 0) (t.rpm.toFloat() / t.maxRpm).coerceIn(0f, 1f) else 0f
        Canvas(Modifier.fillMaxSize()) {
            val s = size.minDimension
            val sw = s * 0.07f
            val r = s * 0.40f
            val tl = Offset(size.width / 2 - r, size.height / 2 - r)
            val arc = Size(r * 2, r * 2)
            drawArc(Color(0xFF0A0A0D), 135f, 270f, false, topLeft = tl, size = arc, style = Stroke(sw, cap = StrokeCap.Round))
            drawArc(LedRed.copy(alpha = 0.35f), 135f + 270f * 0.88f, 270f * 0.12f, false, topLeft = tl, size = arc, style = Stroke(sw))
            val color = if (flash) LedBlue else if (f >= 0.88f) LedRed else accent
            if (f > 0f) drawArc(color, 135f, 270f * f, false, topLeft = tl, size = arc, style = Stroke(sw, cap = StrokeCap.Round))
        }
        Column(horizontalAlignment = Alignment.CenterHorizontally) {
            Text(gearText(t.gear), color = if (flash) LedBlue else Color.White, fontSize = (side * 0.34f).sp, lineHeight = (side * 0.34f).sp, fontWeight = FontWeight.Bold, maxLines = 1)
            Text("${t.speed} km/h", color = Dim, fontSize = (side * 0.09f).coerceAtLeast(8f).sp, maxLines = 1, style = Tabular)
            Text("${t.rpm} tr/min", color = Color(0x80FFFFFF), fontSize = (side * 0.07f).coerceAtLeast(7f).sp, maxLines = 1, style = Tabular)
        }
    }
}

/** Tableau de bord complet : LED en haut, vitesse / rapport / régime au milieu, pédales en courbes en bas. */
@Composable
private fun Dashboard(t: RaceTelemetry, flash: Boolean, vm: DeckViewModel) {
    BoxWithConstraints(Modifier.fillMaxSize()) {
        val h = maxHeight
        Column(Modifier.fillMaxSize(), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            RevLeds(t.rev, flash, Modifier.fillMaxWidth().height(h * 0.14f))
            Row(Modifier.fillMaxWidth().height(h * 0.46f), verticalAlignment = Alignment.CenterVertically) {
                SpeedView(t, Modifier.weight(1f).fillMaxHeight())
                GearView(t, flash, Modifier.weight(1f).fillMaxHeight())
                BoxWithConstraints(Modifier.weight(1f).fillMaxHeight(), contentAlignment = Alignment.Center) {
                    val fs = min(maxWidth.value * 0.24f, maxHeight.value * 0.36f)
                    Column(horizontalAlignment = Alignment.CenterHorizontally) {
                        Text(t.rpm.toString(), color = Color.White, fontSize = fs.sp, lineHeight = fs.sp, fontWeight = FontWeight.Bold, maxLines = 1, style = Tabular)
                        Text("tr/min", color = Dim, fontSize = (fs * 0.4f).coerceAtLeast(8f).sp, maxLines = 1)
                    }
                }
            }
            Row(Modifier.fillMaxWidth().weight(1f), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                PedalTrace(vm, Modifier.weight(1f).fillMaxHeight())
                Pedals(t, Modifier.width(30.dp).fillMaxHeight())
            }
        }
    }
}
