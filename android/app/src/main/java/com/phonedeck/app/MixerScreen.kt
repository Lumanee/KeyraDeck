package com.phonedeck.app

import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.runtime.remember
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.lerp
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.gestures.detectVerticalDragGestures
import androidx.compose.foundation.layout.BoxScope
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.unit.Dp
import kotlin.math.roundToInt

/** Mélangeur : une fenêtre centrée avec une tranche verticale (comme une table de mixage) par appli qui joue du son sur le PC.
 *  La fenêtre prend juste la place des applis actives (largeur et hauteur), sans grand vide. */
@Composable
fun MixerDialog(vm: DeckViewModel) {
    val theme = LocalTheme.current
    BackHandler { vm.closeMixer() }
    // Fond assombri : toucher à côté de la fenêtre la ferme.
    BoxWithConstraints(
        Modifier
            .fillMaxSize()
            .background(Color(0xB0000000))
            .clickable(interactionSource = remember { MutableInteractionSource() }, indication = null) { vm.closeMixer() },
        contentAlignment = Alignment.Center,
    ) {
        val count = vm.mixer.size
        val content = STRIP_WIDTH * count + STRIP_GAP * (count - 1).coerceAtLeast(0) + 32.dp
        val width = content.coerceIn(260.dp, maxWidth * 0.94f)
        // Barre la plus haute possible (précision) sans dépasser l'écran.
        val barHeight = (maxHeight * 0.48f).coerceIn(160.dp, 340.dp)
        Column(
            Modifier
                .width(width)
                .heightIn(max = maxHeight * 0.9f)
                .clip(RoundedCornerShape(24.dp))
                .background(lerp(theme.body, Color.Black, 0.25f))
                .clickable(interactionSource = remember { MutableInteractionSource() }, indication = null) { }
                .padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                Text("Volume", color = Color.White, fontSize = 20.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.weight(1f))
                Box(
                    Modifier.size(44.dp).clip(CircleShape).background(theme.accent).clickable { vm.closeMixer() },
                    contentAlignment = Alignment.Center,
                ) { Text("✕", color = theme.onAccent, fontSize = 20.sp, fontWeight = FontWeight.Bold) }
            }
            // Sortie son actuelle : la toucher ouvre la liste (casque, enceintes…).
            val current = vm.outputs?.firstOrNull { it.isDefault }
            Row(
                Modifier
                    .fillMaxWidth()
                    .clip(RoundedCornerShape(14.dp))
                    .background(Color(0x1FFFFFFF))
                    .clickable { vm.openOutputs() }
                    .padding(horizontal = 12.dp, vertical = 10.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(10.dp),
            ) {
                Text("🎧", fontSize = 18.sp)
                Column(Modifier.weight(1f)) {
                    Text("Sortie son", color = Color(0xFF9A9AA4), fontSize = 11.sp)
                    Text(current?.name ?: "Choisir…", color = Color.White, fontSize = 14.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
                }
                Text("›", color = Color.White, fontSize = 22.sp)
            }
            if (vm.mixer.isEmpty()) {
                Box(Modifier.fillMaxWidth().height(120.dp), contentAlignment = Alignment.Center) {
                    Text(if (vm.status == Status.Connected) "Chargement…" else "Pas connecté au PC", color = Color(0xFFBBBBC4))
                }
            } else {
                Row(
                    Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()),
                    horizontalArrangement = Arrangement.spacedBy(STRIP_GAP, Alignment.CenterHorizontally),
                ) {
                    for (channel in vm.mixer) ChannelStrip(channel, vm, barHeight)
                }
            }
        }
    }
}

/** Liste des sorties son du PC : la sortie actuelle est cochée, en toucher une la choisit. */
@Composable
fun OutputsDialog(vm: DeckViewModel) {
    val theme = LocalTheme.current
    BackHandler { vm.closeOutputs() }
    BoxWithConstraints(
        Modifier
            .fillMaxSize()
            .background(Color(0xB0000000))
            .clickable(interactionSource = remember { MutableInteractionSource() }, indication = null) { vm.closeOutputs() },
        contentAlignment = Alignment.Center,
    ) {
        Column(
            Modifier
                .width(maxWidth * 0.9f)
                .widthIn(max = 420.dp)
                .heightIn(max = maxHeight * 0.85f)
                .clip(RoundedCornerShape(24.dp))
                .background(lerp(theme.body, Color.Black, 0.25f))
                .clickable(interactionSource = remember { MutableInteractionSource() }, indication = null) { }
                .padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                Text("Sortie son", color = Color.White, fontSize = 20.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.weight(1f))
                Box(
                    Modifier.size(44.dp).clip(CircleShape).background(theme.accent).clickable { vm.closeOutputs() },
                    contentAlignment = Alignment.Center,
                ) { Text("✕", color = theme.onAccent, fontSize = 20.sp, fontWeight = FontWeight.Bold) }
            }
            val list = vm.outputs
            when {
                list == null -> Text(if (vm.status == Status.Connected) "Chargement…" else "Pas connecté au PC", color = Color(0xFFBBBBC4))
                list.isEmpty() -> Text("Aucune sortie son trouvée sur le PC.", color = Color(0xFFBBBBC4))
                else -> Column(Modifier.verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    for (o in list) {
                        Row(
                            Modifier
                                .fillMaxWidth()
                                .clip(RoundedCornerShape(14.dp))
                                .background(if (o.isDefault) lerp(theme.key, theme.accent, 0.35f) else lerp(theme.key, Color.White, 0.06f))
                                .clickable { if (!o.isDefault) vm.chooseOutput(o) else vm.closeOutputs() }
                                .padding(horizontal = 14.dp, vertical = 14.dp),
                            verticalAlignment = Alignment.CenterVertically,
                            horizontalArrangement = Arrangement.spacedBy(12.dp),
                        ) {
                            Text(outputGlyph(o.name), fontSize = 20.sp)
                            Text(o.name, color = Color.White, fontSize = 15.sp, maxLines = 2, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f))
                            if (o.isDefault) Text("✓", color = Color.White, fontSize = 20.sp, fontWeight = FontWeight.Bold)
                        }
                    }
                }
            }
        }
    }
}

/** Petit dessin selon le nom que Windows donne à la sortie. */
private fun outputGlyph(name: String): String {
    val n = name.lowercase()
    return when {
        "casque" in n || "headphone" in n || "headset" in n || "écouteur" in n || "earphone" in n || "buds" in n || "airpods" in n -> "🎧"
        "hdmi" in n || "display" in n || "moniteur" in n || "écran" in n || "tv" in n -> "🖥"
        else -> "🔊"
    }
}

private val STRIP_WIDTH = 84.dp
private val STRIP_GAP = 10.dp

/** Une tranche : valeur en %, barre avec l'icône de l'appli incrustée en bas, bouton muet juste dessous, puis le nom. */
@Composable
private fun ChannelStrip(channel: MixerChannel, vm: DeckViewModel, barHeight: Dp) {
    val isMaster = channel.id == "master"
    val theme = LocalTheme.current
    val accent = theme.accent
    val tile = theme.key
    Column(
        Modifier
            .width(STRIP_WIDTH)
            .clip(RoundedCornerShape(16.dp))
            .background(if (isMaster) lerp(tile, accent, 0.25f) else lerp(tile, Color.White, 0.06f))
            .padding(horizontal = 6.dp, vertical = 10.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        Text(
            "${(channel.volume * 100).roundToInt()} %",
            color = if (channel.muted) Color(0xFF9A9AA4) else Color.White,
            fontSize = 15.sp,
            fontWeight = FontWeight.SemiBold,
            maxLines = 1,
        )
        Fader(
            fraction = channel.volume,
            color = if (channel.muted) Color(0xFF55555E) else accent,
            modifier = Modifier.fillMaxWidth().height(barHeight),
            onChange = { f, final -> vm.setVolume(channel.id, f, final) },
        ) {
            // Icône posée dans la barre, en bas, sur un disque sombre pour rester lisible sur la couleur.
            Box(
                Modifier
                    .align(Alignment.BottomCenter)
                    .padding(bottom = 6.dp)
                    .size(40.dp)
                    .clip(CircleShape)
                    .background(Color(0x99000000)),
                contentAlignment = Alignment.Center,
            ) {
                val icon = channel.icon
                val alpha = if (channel.muted) 0.45f else 1f
                if (icon != null) Image(bitmap = icon, contentDescription = null, modifier = Modifier.size(28.dp).alpha(alpha))
                else Text(if (isMaster) "🔊" else "🎵", fontSize = 18.sp, modifier = Modifier.alpha(alpha))
            }
        }
        Box(
            Modifier
                .size(44.dp)
                .clip(CircleShape)
                .background(if (channel.muted) Color(0xFFD9534F) else Color(0x26FFFFFF))
                .clickable { vm.toggleMute(channel) },
            contentAlignment = Alignment.Center,
        ) { Text(if (channel.muted) "🔇" else "🔈", fontSize = 20.sp) }
        Text(channel.name, color = Color(0xFFDDDDE6), fontSize = 11.sp, maxLines = 1, overflow = TextOverflow.Ellipsis, textAlign = TextAlign.Center)
    }
}

/**
 * Barre verticale réglée en glissant le doigt de façon relative : la valeur ne saute pas à l'endroit touché,
 * elle suit le mouvement du doigt (toute la hauteur = 100 %), arrondie au pour cent. Un simple toucher ne change rien,
 * ce qui évite les sauts de volume involontaires.
 */
@Composable
private fun Fader(
    fraction: Float,
    color: Color,
    modifier: Modifier,
    onChange: (Float, Boolean) -> Unit,
    content: @Composable BoxScope.() -> Unit,
) {
    var heightPx by remember { mutableStateOf(1f) }
    val current by rememberUpdatedState(fraction)
    val report by rememberUpdatedState(onChange)
    Box(
        modifier
            .clip(RoundedCornerShape(18.dp))
            .background(Color(0xFF0A0A0D))
            .onSizeChanged { heightPx = it.height.toFloat().coerceAtLeast(1f) }
            .pointerInput(Unit) {
                var exact = 0f
                var last = 0f
                detectVerticalDragGestures(
                    onDragStart = { exact = current; last = current },
                    onDragEnd = { report(last, true) },
                    onDragCancel = { report(last, true) },
                    onVerticalDrag = { change, dy ->
                        change.consume()
                        exact = (exact - dy / heightPx).coerceIn(0f, 1f)
                        val snapped = (exact * 100f).roundToInt() / 100f
                        if (snapped != last) {
                            last = snapped
                            report(snapped, false)
                        }
                    },
                )
            },
    ) {
        Box(Modifier.fillMaxWidth().fillMaxHeight(fraction).align(Alignment.BottomCenter).background(color))
        content()
    }
}
