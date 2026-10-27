package com.phonedeck.app

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.lerp
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.delay
import java.time.LocalDateTime
import java.time.format.DateTimeFormatter
import java.util.Locale
import kotlin.math.min

private val WidgetCap = Color(0xFF060608)
private val WidgetGloss = Brush.verticalGradient(listOf(Color(0x1FFFFFFF), Color(0x00FFFFFF)))

/** Un widget : même cadre que les touches, mais sur plusieurs cases. [unit] est la taille d'une touche. */
@Composable
fun DeckWidgetView(widget: DeckWidget, width: Dp, height: Dp, unit: Dp, vm: DeckViewModel) {
    val theme = LocalTheme.current
    // Un widget est un petit écran tactile encastré dans le boîtier : verre sombre, sans capuchon de touche ni relief.
    val shape = RoundedCornerShape(unit * 0.12f)
    val glass = Brush.verticalGradient(listOf(lerp(Color(0xFF14151B), theme.accent, 0.10f), Color(0xFF050509)))
    val edge = Brush.verticalGradient(listOf(Color(0x40FFFFFF), Color(0x10FFFFFF)))
    Box(
        Modifier
            .size(width, height)
            .clip(shape)
            .background(glass)
            .border(1.dp, edge, shape)
            .padding(unit * 0.08f),
        contentAlignment = Alignment.Center,
    ) {
        Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
            when (widget.type) {
                "media" -> MediaWidget(widget, vm)
                "volume" -> VolumeWidget(widget, vm)
                "clock" -> ClockWidget(widget)
                "system" -> SystemWidget(widget, vm)
                "weather" -> WeatherWidget(widget, vm)
                else -> Text(widget.type, color = Color.Gray)
            }
        }
    }
}

// ---------- Pavé média ----------

@Composable
private fun MediaWidget(widget: DeckWidget, vm: DeckViewModel) {
    val play = "⏯" to "PlayPause"
    val prev = "⏮" to "Prev"
    val next = "⏭" to "Next"
    val volDown = "🔉" to "VolumeDown"
    val mute = "🔇" to "Mute"
    val volUp = "🔊" to "VolumeUp"
    // Chaque modèle a une taille fixe et sa propre disposition.
    val rows: List<List<Pair<String, String>>> = when (widget.model) {
        "media-1x1" -> listOf(listOf(play))
        "media-2x2" -> listOf(listOf(prev, next), listOf(play, mute))
        "media-3x2" -> listOf(listOf(prev, play, next), listOf(volDown, mute, volUp))
        "media-3x1" -> listOf(listOf(prev, play, next))
        else -> when { // anciens widgets sans modèle
            widget.w >= 3 && widget.h >= 2 -> listOf(listOf(prev, play, next), listOf(volDown, mute, volUp))
            widget.w >= 3 -> listOf(listOf(prev, play, next))
            widget.w == 2 -> listOf(listOf(prev, next), listOf(play, mute)).take(widget.h.coerceAtMost(2))
            else -> listOf(listOf(play))
        }
    }
    Column(Modifier.fillMaxSize(), verticalArrangement = Arrangement.spacedBy(6.dp)) {
        for (row in rows) MediaRow(row, vm, Modifier.weight(1f))
    }
}

@Composable
private fun MediaRow(items: List<Pair<String, String>>, vm: DeckViewModel, modifier: Modifier) {
    val haptic = LocalHapticFeedback.current
    val hapticsOn = LocalSettings.current.haptics
    val accent = LocalTheme.current.accent
    Row(modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
        for ((glyph, key) in items) {
            Box(
                Modifier
                    .weight(1f)
                    .fillMaxHeight()
                    .clip(RoundedCornerShape(12.dp))
                    .background(lerp(Color.Black, accent, 0.18f))
                    .clickable {
                        if (hapticsOn) haptic.performHapticFeedback(HapticFeedbackType.TextHandleMove)
                        vm.sendMedia(key)
                    },
                contentAlignment = Alignment.Center,
            ) {
                BoxWithConstraints(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                    val size = (min(maxWidth.value, maxHeight.value) * 0.5f).coerceIn(14f, 40f)
                    Text(glyph, fontSize = size.sp, color = Color.White)
                }
            }
        }
    }
}

// ---------- Volume général ----------

@Composable
private fun VolumeWidget(widget: DeckWidget, vm: DeckViewModel) {
    val vertical = widget.h > widget.w
    val volume = vm.masterVolume
    val fraction = (volume ?: 0) / 100f
    val accent = LocalTheme.current.accent
    val label = if (volume == null) "—" else if (vm.masterMuted) "Muet" else "$volume %"
    val icon = if (vm.masterMuted || volume == 0) "🔇" else "🔊"
    val bar: @Composable (Modifier) -> Unit = { m ->
        VolumeBar(
            fraction = fraction,
            vertical = vertical,
            color = if (vm.masterMuted) Color.Gray else accent,
            modifier = m,
            onChange = { f, final -> vm.setMasterVolume(f, final) },
        )
    }
    if (vertical) {
        Column(Modifier.fillMaxSize(), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(6.dp)) {
            Text(icon, fontSize = 22.sp, modifier = Modifier.clickable { vm.toggleMasterMute() })
            bar(Modifier.weight(1f).width(34.dp))
            Text(label, color = Color.White, fontSize = 12.sp)
        }
    } else {
        Row(Modifier.fillMaxSize(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            Text(icon, fontSize = 24.sp, modifier = Modifier.clickable { vm.toggleMasterMute() })
            bar(Modifier.weight(1f).height(34.dp))
            Text(label, color = Color.White, fontSize = 13.sp, modifier = Modifier.width(46.dp), textAlign = TextAlign.End)
        }
    }
}

/** Barre de volume : on glisse le doigt (ou on touche) pour régler, sans dépendre du Slider de Material. */
@Composable
fun VolumeBar(fraction: Float, vertical: Boolean, color: Color, modifier: Modifier, onChange: (Float, Boolean) -> Unit) {
    var sizePx by remember { mutableStateOf(1f) }
    fun at(x: Float, y: Float): Float {
        val raw = if (vertical) 1f - y / sizePx else x / sizePx
        return raw.coerceIn(0f, 1f)
    }
    Box(
        modifier
            .clip(RoundedCornerShape(50))
            .background(Color(0xFF0A0A0D))
            .pointerInput(vertical) {
                detectTapGestures { offset -> onChange(at(offset.x, offset.y), true) }
            }
            .pointerInput(vertical) {
                var last = fraction
                detectDragGestures(
                    onDragStart = { offset -> last = at(offset.x, offset.y); onChange(last, false) },
                    onDragEnd = { onChange(last, true) },
                    onDragCancel = { onChange(last, true) },
                    onDrag = { change, _ ->
                        change.consume()
                        last = at(change.position.x, change.position.y)
                        onChange(last, false)
                    },
                )
            }
            .onSizeChanged { px -> sizePx = if (vertical) px.height.toFloat() else px.width.toFloat() },
    ) {
        Box(
            Modifier
                .then(if (vertical) Modifier.fillMaxWidth().fillMaxHeight(fraction).align(Alignment.BottomCenter) else Modifier.fillMaxHeight().fillMaxWidth(fraction))
                .background(color),
        )
    }
}

// ---------- Horloge ----------

@Composable
private fun ClockWidget(widget: DeckWidget) {
    var now by remember { mutableStateOf(LocalDateTime.now()) }
    LaunchedEffect(Unit) {
        while (true) {
            now = LocalDateTime.now()
            delay(1000)
        }
    }
    val time = now.format(DateTimeFormatter.ofPattern("HH:mm"))
    val date = now.format(DateTimeFormatter.ofPattern("EEE d MMM", Locale.getDefault()))
    val seconds = now.format(DateTimeFormatter.ofPattern("ss"))
    BoxWithConstraints(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        val w = maxWidth.value
        val h = maxHeight.value
        when (widget.model) {
            "clock-1x1" -> Text(time, color = Color.White, fontSize = (w * 0.27f).sp, fontWeight = FontWeight.SemiBold, maxLines = 1)
            "clock-2x2" -> Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.Center) {
                Text(time, color = Color.White, fontSize = min(w * 0.30f, h * 0.36f).sp, fontWeight = FontWeight.SemiBold, maxLines = 1)
                Text(seconds, color = LocalTheme.current.accent, fontSize = min(w * 0.12f, h * 0.14f).sp, maxLines = 1)
                Text(date, color = Color(0xFFBBBBC4), fontSize = min(w * 0.085f, h * 0.12f).sp, maxLines = 1, softWrap = false)
            }
            else -> Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.Center) {
                Text(time, color = Color.White, fontSize = min(w * 0.20f, h * 0.40f).sp, fontWeight = FontWeight.SemiBold, maxLines = 1, softWrap = false)
                Text(date, color = Color(0xFFBBBBC4), fontSize = min(w * 0.065f, h * 0.15f).coerceAtLeast(9f).sp, maxLines = 1, softWrap = false)
            }
        }
    }
}

// ---------- CPU / RAM ----------

@Composable
private fun SystemWidget(widget: DeckWidget, vm: DeckViewModel) {
    val accent = LocalTheme.current.accent
    val ramColor = lerp(accent, Color.White, 0.35f)
    BoxWithConstraints(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        val w = maxWidth.value
        val h = maxHeight.value
        when (widget.model) {
            "system-2x2" -> Row(Modifier.fillMaxSize(), horizontalArrangement = Arrangement.SpaceEvenly, verticalAlignment = Alignment.CenterVertically) {
                val ring = min(w / 2.2f, h * 0.75f)
                Ring("CPU", vm.cpu, accent, ring)
                Ring("RAM", vm.ram, ramColor, ring)
            }
            "system-3x1" -> Row(Modifier.fillMaxSize().padding(horizontal = 4.dp), horizontalArrangement = Arrangement.spacedBy(14.dp), verticalAlignment = Alignment.CenterVertically) {
                Box(Modifier.weight(1f)) { StatRow("CPU", vm.cpu, accent, 14f) }
                Box(Modifier.weight(1f)) { StatRow("RAM", vm.ram, ramColor, 14f) }
            }
            "system-1x1" -> Column(Modifier.fillMaxSize(), verticalArrangement = Arrangement.SpaceEvenly) {
                StatRow("CPU", vm.cpu, accent, 10f)
                StatRow("RAM", vm.ram, ramColor, 10f)
            }
            else -> Column(Modifier.fillMaxSize().padding(horizontal = 4.dp), verticalArrangement = Arrangement.SpaceEvenly) {
                StatRow("CPU", vm.cpu, accent, 13f)
                StatRow("RAM", vm.ram, ramColor, 13f)
            }
        }
    }
}

@Composable
private fun StatRow(label: String, value: Int?, color: Color, textSize: Float) {
    Column(Modifier.fillMaxWidth()) {
        Row(Modifier.fillMaxWidth()) {
            Text(label, color = Color(0xFFBBBBC4), fontSize = textSize.sp, maxLines = 1, modifier = Modifier.weight(1f))
            Text(if (value == null) "—" else "$value %", color = Color.White, fontSize = textSize.sp, fontWeight = FontWeight.SemiBold, maxLines = 1)
        }
        Spacer(Modifier.height(2.dp))
        Box(Modifier.fillMaxWidth().height((textSize * 0.45f).dp).clip(RoundedCornerShape(50)).background(Color(0xFF0A0A0D))) {
            Box(Modifier.fillMaxHeight().fillMaxWidth(((value ?: 0) / 100f).coerceIn(0f, 1f)).background(color))
        }
    }
}

/** Jauge circulaire avec le pourcentage au centre. */
@Composable
private fun Ring(label: String, value: Int?, color: Color, sizeDp: Float) {
    Column(horizontalAlignment = Alignment.CenterHorizontally) {
        Box(Modifier.size(sizeDp.dp), contentAlignment = Alignment.Center) {
            Canvas(Modifier.fillMaxSize()) {
                val stroke = size.minDimension * 0.13f
                val inset = stroke / 2
                val arcSize = Size(size.width - stroke, size.height - stroke)
                drawArc(Color(0xFF0A0A0D), 0f, 360f, false, topLeft = Offset(inset, inset), size = arcSize, style = Stroke(stroke))
                drawArc(color, -90f, 360f * ((value ?: 0) / 100f).coerceIn(0f, 1f), false, topLeft = Offset(inset, inset), size = arcSize, style = Stroke(stroke, cap = StrokeCap.Round))
            }
            Text(if (value == null) "—" else "$value", color = Color.White, fontSize = (sizeDp * 0.30f).sp, fontWeight = FontWeight.SemiBold, maxLines = 1)
        }
        Text(label, color = Color(0xFFBBBBC4), fontSize = (sizeDp * 0.16f).coerceAtLeast(10f).sp, maxLines = 1)
    }
}


// ---------- Météo ----------

private fun weatherIcon(code: Int) = when (code) {
    0 -> "☀️"
    1, 2 -> "🌤️"
    3 -> "☁️"
    45, 48 -> "🌫️"
    in 51..57 -> "🌦️"
    in 61..67, in 80..82 -> "🌧️"
    in 71..77, 85, 86 -> "❄️"
    in 95..99 -> "⛈️"
    else -> "🌡️"
}

private fun weatherText(code: Int) = when (code) {
    0 -> "Ciel dégagé"
    1, 2 -> "Éclaircies"
    3 -> "Couvert"
    45, 48 -> "Brouillard"
    in 51..57 -> "Bruine"
    in 61..67, in 80..82 -> "Pluie"
    in 71..77, 85, 86 -> "Neige"
    in 95..99 -> "Orage"
    else -> "—"
}

@Composable
private fun WeatherWidget(widget: DeckWidget, vm: DeckViewModel) {
    val wx = vm.weather
    BoxWithConstraints(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        val w = maxWidth.value
        val h = maxHeight.value
        if (wx == null) {
            Text("Météo…\n(ville à choisir sur le PC)", color = Color(0xFFBBBBC4), fontSize = min(w * 0.10f, 12f).sp, textAlign = TextAlign.Center)
            return@BoxWithConstraints
        }
        when (widget.model) {
            "weather-2x2" -> Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.Center) {
                Text(wx.city, color = Color(0xFFBBBBC4), fontSize = min(w * 0.09f, h * 0.09f).sp, maxLines = 1, softWrap = false)
                Text(weatherIcon(wx.code), fontSize = min(w * 0.28f, h * 0.28f).sp)
                Text("${wx.temp}°", color = Color.White, fontSize = min(w * 0.24f, h * 0.24f).sp, fontWeight = FontWeight.SemiBold, maxLines = 1, softWrap = false)
                Text(weatherText(wx.code), color = Color(0xFFBBBBC4), fontSize = min(w * 0.09f, h * 0.09f).sp, maxLines = 1, softWrap = false)
                Text("💨 ${wx.wind} km/h  💧 ${wx.humidity} %", color = Color(0xFFBBBBC4), fontSize = min(w * 0.075f, h * 0.075f).sp, maxLines = 1, softWrap = false)
            }
            "weather-2x1" -> Row(Modifier.fillMaxSize(), horizontalArrangement = Arrangement.SpaceEvenly, verticalAlignment = Alignment.CenterVertically) {
                Text(weatherIcon(wx.code), fontSize = min(w * 0.24f, h * 0.5f).sp)
                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                    Text("${wx.temp}°", color = Color.White, fontSize = min(w * 0.2f, h * 0.4f).sp, fontWeight = FontWeight.SemiBold, maxLines = 1, softWrap = false)
                    Text(wx.city, color = Color(0xFFBBBBC4), fontSize = min(w * 0.07f, h * 0.14f).coerceAtLeast(9f).sp, maxLines = 1, softWrap = false)
                }
            }
            else -> Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.Center) {
                Text(weatherIcon(wx.code), fontSize = (h * 0.38f).sp)
                Text("${wx.temp}°", color = Color.White, fontSize = (h * 0.28f).sp, fontWeight = FontWeight.SemiBold, maxLines = 1, softWrap = false)
            }
        }
    }
}
