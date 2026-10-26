package com.phonedeck.app

import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
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
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import kotlin.math.roundToInt

/** Mélangeur : une fenêtre avec une tranche verticale (comme une table de mixage) par appli qui joue du son sur le PC. */
@Composable
fun MixerDialog(vm: DeckViewModel) {
    val theme = LocalTheme.current
    Dialog(onDismissRequest = { vm.closeMixer() }, properties = DialogProperties(usePlatformDefaultWidth = false)) {
        Column(
            Modifier
                .padding(horizontal = 12.dp)
                .fillMaxWidth()
                .height(420.dp)
                .clip(RoundedCornerShape(22.dp))
                .background(lerp(theme.body, Color.Black, 0.25f))
                .padding(14.dp),
        ) {
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                Text("Volume", color = Color.White, fontSize = 20.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.weight(1f))
                Text("✕", color = Color(0xFFBBBBC4), fontSize = 22.sp, modifier = Modifier.clickable { vm.closeMixer() }.padding(8.dp))
            }
            if (vm.mixer.isEmpty()) {
                Box(Modifier.weight(1f).fillMaxWidth(), contentAlignment = Alignment.Center) {
                    Text(if (vm.status == Status.Connected) "Chargement…" else "Pas connecté au PC", color = Color(0xFFBBBBC4))
                }
            } else {
                Row(
                    Modifier.weight(1f).fillMaxWidth().horizontalScroll(rememberScrollState()),
                    horizontalArrangement = Arrangement.spacedBy(10.dp),
                ) {
                    for (channel in vm.mixer) ChannelStrip(channel, vm)
                }
            }
        }
    }
}

@Composable
private fun ChannelStrip(channel: MixerChannel, vm: DeckViewModel) {
    val isMaster = channel.id == "master"
    val accent = LocalTheme.current.accent
    val tile = LocalTheme.current.key
    Column(
        Modifier
            .width(76.dp)
            .fillMaxHeight()
            .clip(RoundedCornerShape(16.dp))
            .background(if (isMaster) lerp(tile, accent, 0.25f) else lerp(tile, Color.White, 0.06f))
            .padding(horizontal = 8.dp, vertical = 10.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(6.dp),
    ) {
        Text("${(channel.volume * 100).roundToInt()}", color = Color.White, fontSize = 14.sp, fontWeight = FontWeight.SemiBold)
        VolumeBar(
            fraction = channel.volume,
            vertical = true,
            color = if (channel.muted) Color.Gray else accent,
            modifier = Modifier.weight(1f).width(36.dp),
            onChange = { f, final -> vm.setVolume(channel.id, f, final) },
        )
        Text(if (channel.muted) "🔇" else "🔈", fontSize = 22.sp, modifier = Modifier.clickable { vm.toggleMute(channel) }.padding(2.dp))
        Box(Modifier.size(30.dp), contentAlignment = Alignment.Center) {
            val icon = channel.icon
            if (icon != null) Image(bitmap = icon, contentDescription = null, modifier = Modifier.size(28.dp))
            else Text(if (isMaster) "🔊" else "🎵", fontSize = 20.sp)
        }
        Text(channel.name, color = Color(0xFFDDDDE6), fontSize = 11.sp, maxLines = 1, overflow = TextOverflow.Ellipsis, textAlign = TextAlign.Center)
    }
}
