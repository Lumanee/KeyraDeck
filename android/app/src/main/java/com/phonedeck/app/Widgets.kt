package com.phonedeck.app

import androidx.compose.foundation.background
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
    val capShape = RoundedCornerShape(unit * 0.2f)
    Box(
        Modifier
            .size(width, height)
            .shadow(5.dp, capShape)
            .clip(capShape)
            .background(WidgetCap)
            .padding(unit * 0.05f),
    ) {
        Box(
            Modifier
                .fillMaxSize()
                .clip(RoundedCornerShape(unit * 0.14f))
                .background(theme.key)
                .background(WidgetGloss)
                .padding(unit * 0.07f),
            contentAlignment = Alignment.Center,
        ) {
            when (widget.type) {
                "media" -> MediaWidget(widget, vm)
                "volume" -> VolumeWidget(widget, vm)
                "clock" -> ClockWidget(unit, widget)
                "system" -> SystemWidget(vm)
                else -> Text(widget.type, color = Color.Gray)
            }
        }
    }
}

// ---------- Pavé média ----------

@Composable
private fun MediaWidget(widget: DeckWidget, vm: DeckViewModel) {
    val top = when {
        widget.w >= 3 -> listOf("⏮" to "Prev", "⏯" to "PlayPause", "⏭" to "Next")
        widget.w == 2 -> listOf("⏯" to "PlayPause", "⏭" to "Next")
        else -> listOf("⏯" to "PlayPause")
    }
    val bottom = if (widget.h >= 2) {
        when {
            widget.w >= 3 -> listOf("🔉" to "VolumeDown", "🔇" to "Mute", "🔊" to "VolumeUp")
            widget.w == 2 -> listOf("🔉" to "VolumeDown", "🔊" to "VolumeUp")
            else -> listOf("🔇" to "Mute")
        }
    } else emptyList()
    Column(Modifier.fillMaxSize(), verticalArrangement = Arrangement.spacedBy(6.dp)) {
        MediaRow(top, vm, Modifier.weight(1f))
        if (bottom.isNotEmpty()) MediaRow(bottom, vm, Modifier.weight(1f))
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
private fun VolumeBar(fraction: Float, vertical: Boolean, color: Color, modifier: Modifier, onChange: (Float, Boolean) -> Unit) {
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
private fun ClockWidget(unit: Dp, widget: DeckWidget) {
    var now by remember { mutableStateOf(LocalDateTime.now()) }
    LaunchedEffect(Unit) {
        while (true) {
            now = LocalDateTime.now()
            delay(1000)
        }
    }
    val time = now.format(DateTimeFormatter.ofPattern("HH:mm"))
    val date = now.format(DateTimeFormatter.ofPattern("EEE d MMM", Locale.getDefault()))
    val base = min(unit.value * min(widget.w, widget.h * 2), unit.value * widget.h * 0.55f * 2)
    Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.Center) {
        Text(time, color = Color.White, fontSize = (base * 0.42f).coerceIn(18f, 64f).sp, fontWeight = FontWeight.SemiBold)
        Text(date, color = Color(0xFFBBBBC4), fontSize = (base * 0.16f).coerceIn(10f, 20f).sp)
    }
}

// ---------- CPU / RAM ----------

@Composable
private fun SystemWidget(vm: DeckViewModel) {
    val accent = LocalTheme.current.accent
    Column(Modifier.fillMaxSize().padding(4.dp), verticalArrangement = Arrangement.SpaceEvenly) {
        StatRow("CPU", vm.cpu, accent)
        StatRow("RAM", vm.ram, lerp(accent, Color.White, 0.35f))
    }
}

@Composable
private fun StatRow(label: String, value: Int?, color: Color) {
    Column {
        Row(Modifier.fillMaxWidth()) {
            Text(label, color = Color(0xFFBBBBC4), fontSize = 12.sp, modifier = Modifier.weight(1f))
            Text(if (value == null) "—" else "$value %", color = Color.White, fontSize = 12.sp, fontWeight = FontWeight.SemiBold)
        }
        Spacer(Modifier.height(3.dp))
        Box(Modifier.fillMaxWidth().height(6.dp).clip(RoundedCornerShape(50)).background(Color(0xFF0A0A0D))) {
            Box(Modifier.fillMaxHeight().fillMaxWidth((value ?: 0) / 100f).background(color))
        }
    }
}
