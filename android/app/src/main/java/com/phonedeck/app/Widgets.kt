package com.phonedeck.app

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.Image
import androidx.compose.foundation.basicMarquee
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
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
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

/** La zone écran : une seule dalle de verre sombre, sans touches, sur laquelle les widgets se posent. */
@Composable
fun ScreenPanel(width: Dp, height: Dp, unit: Dp) {
    val theme = LocalTheme.current
    val shape = RoundedCornerShape(unit * 0.14f)
    val glass = Brush.verticalGradient(listOf(lerp(theme.key, Color.White, 0.08f), theme.key, lerp(theme.key, Color.Black, 0.65f)))
    val edge = Brush.verticalGradient(listOf(Color(0x66FFFFFF), Color(0x12FFFFFF), Color(0x29FFFFFF)))
    Box(
        Modifier
            .size(width, height)
            .clip(shape)
            .background(glass)
            .border(1.dp, edge, shape),
    )
}

/**
 * Un widget : même cadre que les touches, mais sur plusieurs cases. [unit] est la taille d'une touche.
 * [framed] = false quand il est posé sur la zone écran : pas de cadre à lui, il fait partie de l'écran.
 */
@Composable
fun DeckWidgetView(widget: DeckWidget, width: Dp, height: Dp, unit: Dp, vm: DeckViewModel, framed: Boolean = true) {
    val theme = LocalTheme.current
    // Un widget est un petit écran tactile encastré dans le boîtier : verre sombre, sans capuchon de touche ni relief.
    val shape = RoundedCornerShape(unit * 0.12f)
    // Verre fumé neutre (sans teinte d'accent) : reflet clair en haut, très sombre en bas, liseré comme une tranche de verre.
    val glass = Brush.verticalGradient(listOf(lerp(theme.key, Color.White, 0.10f), theme.key, lerp(theme.key, Color.Black, 0.60f)))
    val edge = Brush.verticalGradient(listOf(Color(0x66FFFFFF), Color(0x12FFFFFF), Color(0x29FFFFFF)))
    val frame = if (framed) Modifier.clip(shape).background(glass).border(1.dp, edge, shape) else Modifier.clip(shape)
    Box(
        Modifier
            .size(width, height)
            .then(frame)
            .padding(unit * 0.08f),
        contentAlignment = Alignment.Center,
    ) {
        Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
            when (widget.type) {
                "media" -> MediaWidget(widget, vm)
                "nowplaying" -> NowPlayingWidget(widget, vm)
                "volume" -> VolumeWidget(widget, vm)
                "clock" -> ClockWidget(widget)
                "system" -> SystemWidget(widget, vm)
                "weather" -> WeatherWidget(widget, vm)
                "discord" -> DiscordWidget(widget, vm)
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
    val touch = LocalTouch.current
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
                        touch.touch(null, null, false)
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

// ---------- Morceau en cours ----------

@Composable
private fun NowPlayingWidget(widget: DeckWidget, vm: DeckViewModel) {
    val np = vm.nowPlaying
    val art = vm.nowPlayingArt
    val accent = LocalTheme.current.accent
    // La position avance toute seule entre deux messages du PC.
    var now by remember { mutableStateOf(System.currentTimeMillis()) }
    LaunchedEffect(np?.playing) {
        while (np?.playing == true) { now = System.currentTimeMillis(); delay(500) }
    }
    val pos = if (np == null) 0 else if (np.playing) (np.pos + ((now - np.at) / 1000).toInt()).coerceAtMost(np.dur) else np.pos
    val title = np?.title?.ifBlank { null } ?: "Rien en lecture"
    val artist = np?.artist ?: if (np == null) "Lance Spotify ou une vidéo" else ""
    val playGlyph = if (np?.playing == true) "⏸" else "▶"

    val cover: @Composable (Modifier) -> Unit = { m ->
        Box(m.clip(RoundedCornerShape(10.dp)).background(lerp(Color.Black, accent, 0.30f)), contentAlignment = Alignment.Center) {
            if (art != null) Image(art, contentDescription = null, contentScale = ContentScale.Crop, modifier = Modifier.fillMaxSize())
            else Text("♪", color = Color.White.copy(alpha = 0.8f), fontSize = 26.sp)
        }
    }
    val texts: @Composable (Modifier, Float) -> Unit = { m, size ->
        Column(m, verticalArrangement = Arrangement.Center) {
            MarqueeText(title, Color.White, size, FontWeight.SemiBold)
            if (artist.isNotEmpty()) MarqueeText(artist, Color(0xFFBBBBC4), size * 0.82f, FontWeight.Normal)
        }
    }
    val controls: @Composable (Modifier, Boolean) -> Unit = { m, withPrev ->
        val items = buildList {
            if (withPrev) add("⏮" to "prev")
            add(playGlyph to "toggle")
            add("⏭" to "next")
        }
        NowPlayingButtons(items, vm, m)
    }

    BoxWithConstraints(Modifier.fillMaxSize()) {
        val h = maxHeight
        when (widget.model) {
            "nowplaying-2x1" -> Row(Modifier.fillMaxSize().clickable { vm.nowPlayingCommand("toggle") }, verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                cover(Modifier.size(h))
                texts(Modifier.weight(1f), 13f)
            }
            "nowplaying-4x1" -> Row(Modifier.fillMaxSize(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                cover(Modifier.size(h))
                texts(Modifier.weight(1f), 14f)
                controls(Modifier.fillMaxHeight().width(h * 1.9f), true)
            }
            "nowplaying-2x2" -> Box(Modifier.fillMaxSize().clip(RoundedCornerShape(10.dp))) {
                // Pochette en fond, assombrie pour garder le texte lisible.
                cover(Modifier.fillMaxSize())
                Box(Modifier.fillMaxSize().background(Brush.verticalGradient(listOf(Color(0x33000000), Color(0xDD000000)))))
                Column(Modifier.fillMaxSize().padding(6.dp), verticalArrangement = Arrangement.Bottom) {
                    texts(Modifier.fillMaxWidth(), 13f)
                    Spacer(Modifier.height(6.dp))
                    controls(Modifier.fillMaxWidth().height(h * 0.28f), true)
                }
            }
            else -> Row(Modifier.fillMaxSize(), horizontalArrangement = Arrangement.spacedBy(10.dp)) { // nowplaying-4x2
                cover(Modifier.size(h))
                Column(Modifier.weight(1f).fillMaxHeight(), verticalArrangement = Arrangement.SpaceBetween) {
                    if (np?.app?.isNotEmpty() == true) Text(np.app, color = accent, fontSize = 11.sp, maxLines = 1)
                    texts(Modifier.fillMaxWidth(), 16f)
                    if (np != null && np.dur > 0) Progress(pos, np.dur, accent)
                    controls(Modifier.fillMaxWidth().height(h * 0.30f), true)
                }
            }
        }
    }
}

@OptIn(ExperimentalFoundationApi::class)
@Composable
private fun MarqueeText(text: String, color: Color, size: Float, weight: FontWeight) {
    // Les titres trop longs défilent doucement.
    Text(text, color = color, fontSize = size.sp, fontWeight = weight, maxLines = 1, overflow = TextOverflow.Clip, modifier = Modifier.basicMarquee(iterations = Int.MAX_VALUE))
}

private fun mmss(s: Int) = "%d:%02d".format(s / 60, s % 60)

@Composable
private fun Progress(pos: Int, dur: Int, color: Color) {
    Column(Modifier.fillMaxWidth()) {
        Box(Modifier.fillMaxWidth().height(4.dp).clip(RoundedCornerShape(50)).background(Color(0xFF0A0A0D))) {
            Box(Modifier.fillMaxHeight().fillMaxWidth((pos.toFloat() / dur).coerceIn(0f, 1f)).background(color))
        }
        Row(Modifier.fillMaxWidth()) {
            Text(mmss(pos), color = Color(0xFFBBBBC4), fontSize = 10.sp, modifier = Modifier.weight(1f))
            Text(mmss(dur), color = Color(0xFFBBBBC4), fontSize = 10.sp)
        }
    }
}

@Composable
private fun NowPlayingButtons(items: List<Pair<String, String>>, vm: DeckViewModel, modifier: Modifier) {
    val touch = LocalTouch.current
    val accent = LocalTheme.current.accent
    Row(modifier, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
        for ((glyph, action) in items) {
            Box(
                Modifier
                    .weight(1f)
                    .fillMaxHeight()
                    .clip(RoundedCornerShape(10.dp))
                    .background(if (action == "toggle") accent.copy(alpha = 0.85f) else lerp(Color.Black, accent, 0.22f).copy(alpha = 0.9f))
                    .clickable {
                        touch.touch(null, null, false)
                        vm.nowPlayingCommand(action)
                    },
                contentAlignment = Alignment.Center,
            ) {
                BoxWithConstraints(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                    val size = (min(maxWidth.value, maxHeight.value) * 0.5f).coerceIn(12f, 30f)
                    Text(glyph, fontSize = size.sp, color = if (action == "toggle") LocalTheme.current.onAccent else Color.White)
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

// ---------- Salon vocal Discord ----------

internal val DiscordBlurple = Color(0xFF5865F2)
internal val DiscordGreen = Color(0xFF23A55A)

/** Qui est dans le salon vocal et qui parle (contour vert), avec le micro et la sourdine en haut. */
@Composable
private fun DiscordWidget(widget: DeckWidget, vm: DeckViewModel) {
    val d = vm.discord
    val active = LocalTheme.current.active
    BoxWithConstraints(Modifier.fillMaxSize()) {
        val w = maxWidth.value
        val h = maxHeight.value
        val small = min(w * 0.075f, 12f)
        if (d == null || !d.connected) {
            Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                Text(if (d == null) "Discord…" else "Discord\n" + d.status, color = Color(0xFFBBBBC4), fontSize = small.sp, textAlign = TextAlign.Center, maxLines = 4)
            }
            return@BoxWithConstraints
        }
        if (widget.model == "discord-1x1") {
            DiscordSpeaker(d, w, h)
            return@BoxWithConstraints
        }
        // Nombre de visages selon le modèle : 2×1 → une ligne, 2×2 → 4, 3×2 → 6.
        val maxFaces = when (widget.model) { "discord-2x1" -> 3; "discord-3x2" -> 6; else -> 4 }
        val perRow = when (widget.model) { "discord-2x1" -> 3; "discord-3x2" -> 3; else -> 2 }
        Column(Modifier.fillMaxSize()) {
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                Text(
                    if (d.channel.isEmpty()) "Pas en vocal" else "🔊 " + d.channel,
                    color = Color.White, fontSize = small.sp, maxLines = 1, softWrap = false, modifier = Modifier.weight(1f),
                )
                MiniToggle(if (d.muted || d.deafened) "🔇" else "🎙", d.muted || d.deafened, active, small) { vm.sendDiscord("mute") }
                Spacer(Modifier.width(4.dp))
                MiniToggle("🎧", d.deafened, active, small) { vm.sendDiscord("deafen") }
            }
            Spacer(Modifier.height(4.dp))
            if (d.members.isEmpty()) {
                Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                    Text(if (d.channel.isEmpty()) "Rejoins un salon vocal" else "Personne", color = Color(0xFFBBBBC4), fontSize = small.sp)
                }
            } else {
                val shown = d.members.take(maxFaces)
                val rows = shown.chunked(perRow)
                val face = min(w / perRow * 0.55f, (h - small * 3f) / rows.size * 0.55f).coerceAtLeast(14f)
                Column(Modifier.fillMaxSize(), verticalArrangement = Arrangement.SpaceEvenly) {
                    rows.forEach { row ->
                        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceEvenly) {
                            row.forEach { m -> DiscordFace(m, face, small) }
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun MiniToggle(symbol: String, on: Boolean, active: Color, size: Float, onTap: () -> Unit) {
    Box(
        Modifier
            .size((size * 2.1f).dp)
            .clip(RoundedCornerShape(50))
            .background(if (on) active else Color(0x33FFFFFF))
            .clickable { onTap() },
        contentAlignment = Alignment.Center,
    ) {
        Text(symbol, fontSize = (size * 1.05f).sp)
    }
}

@Composable
private fun DiscordFace(m: DiscordMember, size: Float, textSize: Float) {
    Column(horizontalAlignment = Alignment.CenterHorizontally) {
        Box(
            Modifier
                .size(size.dp)
                .clip(RoundedCornerShape(50))
                .background(DiscordBlurple)
                .border(if (m.speaking) 3.dp else 0.dp, if (m.speaking) DiscordGreen else Color.Transparent, RoundedCornerShape(50)),
            contentAlignment = Alignment.Center,
        ) {
            Text(m.name.trim().take(1).uppercase(), color = Color.White, fontSize = (size * 0.45f).sp, fontWeight = FontWeight.Bold)
            if (m.muted || m.deafened) {
                Text(if (m.deafened) "🎧" else "🔇", fontSize = (size * 0.28f).sp, modifier = Modifier.align(Alignment.BottomEnd))
            }
        }
        Text(m.name, color = if (m.speaking) DiscordGreen else Color(0xFFDDDDE4), fontSize = (textSize * 0.85f).sp, maxLines = 1, softWrap = false)
    }
}

/** Widget 1×1 : seulement la personne qui parle. Quand plus personne ne parle, la dernière reste affichée en gris. */
@Composable
private fun DiscordSpeaker(d: DiscordVoice, w: Float, h: Float) {
    val speakers = d.members.filter { it.speaking }
    var last by remember { mutableStateOf<DiscordMember?>(null) }
    LaunchedEffect(speakers.firstOrNull()?.name) { speakers.firstOrNull()?.let { last = it } }
    LaunchedEffect(d.inCall) { if (!d.inCall) last = null }
    val m = speakers.firstOrNull() ?: last?.copy(speaking = false)
    val small = min(w * 0.13f, 12f)
    if (m == null) {
        Text(if (d.inCall) "🔊\nPersonne\nne parle" else "Pas en\nvocal", color = Color(0xFFBBBBC4), fontSize = small.sp, textAlign = TextAlign.Center, maxLines = 3)
        return
    }
    val face = min(w, h) * 0.6f
    Column(Modifier.fillMaxSize(), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.Center) {
        Box(
            Modifier
                .size(face.dp)
                .clip(RoundedCornerShape(50))
                .background(if (m.speaking) DiscordBlurple else Color(0xFF3A3B44))
                .border(if (m.speaking) 3.dp else 0.dp, if (m.speaking) DiscordGreen else Color.Transparent, RoundedCornerShape(50)),
            contentAlignment = Alignment.Center,
        ) {
            Text(m.name.trim().take(1).uppercase(), color = Color.White, fontSize = (face * 0.45f).sp, fontWeight = FontWeight.Bold)
            if (speakers.size > 1) {
                Text("+${speakers.size - 1}", color = Color.White, fontSize = (face * 0.22f).sp, fontWeight = FontWeight.Bold,
                    modifier = Modifier.align(Alignment.TopEnd).clip(RoundedCornerShape(50)).background(DiscordGreen).padding(horizontal = 3.dp))
            }
        }
        Spacer(Modifier.height(3.dp))
        Text(m.name, color = if (m.speaking) DiscordGreen else Color(0xFF8E8E98), fontSize = small.sp, maxLines = 1, softWrap = false, overflow = TextOverflow.Ellipsis)
    }
}
