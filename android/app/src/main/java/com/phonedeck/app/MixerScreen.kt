package com.phonedeck.app

import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Slider
import androidx.compose.material3.SliderDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlin.math.roundToInt

/** Mélangeur : un curseur de volume par appli qui joue du son sur le PC. */
@Composable
fun MixerScreen(vm: DeckViewModel) {
    Column(Modifier.fillMaxSize()) {
        Row(Modifier.padding(8.dp), verticalAlignment = Alignment.CenterVertically) {
            IconButton(onClick = { vm.closeMixer() }) {
                Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Retour", tint = Color.White)
            }
            Text("Volume", color = Color.White, fontSize = 22.sp, fontWeight = FontWeight.SemiBold)
        }
        if (vm.mixer.isEmpty()) {
            Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                Text(
                    if (vm.status == Status.Connected) "Chargement…" else "Pas connecté au PC",
                    color = Color(0xFFBBBBC4),
                )
            }
            return
        }
        LazyColumn(
            contentPadding = PaddingValues(horizontal = 16.dp, vertical = 8.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            items(vm.mixer, key = { it.id }) { channel ->
                ChannelRow(channel, vm)
            }
        }
    }
}

@Composable
private fun ChannelRow(channel: MixerChannel, vm: DeckViewModel) {
    val isMaster = channel.id == "master"
    val accent = LocalTheme.current.accent
    val tile = LocalTheme.current.key
    Row(
        Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(18.dp))
            .background(if (isMaster) androidx.compose.ui.graphics.lerp(tile, accent, 0.25f) else androidx.compose.ui.graphics.lerp(tile, Color.White, 0.06f))
            .padding(horizontal = 12.dp, vertical = 6.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Box(Modifier.size(40.dp), contentAlignment = Alignment.Center) {
            val icon = channel.icon
            if (icon != null) {
                Image(bitmap = icon, contentDescription = null, modifier = Modifier.size(36.dp))
            } else {
                Text(if (isMaster) "🔊" else "🎵", fontSize = 24.sp)
            }
        }
        Spacer(Modifier.width(10.dp))
        Column(Modifier.weight(1f)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(
                    channel.name,
                    color = Color.White,
                    fontSize = 15.sp,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                    modifier = Modifier.weight(1f),
                )
                Text("${(channel.volume * 100).roundToInt()} %", color = Color(0xFFBBBBC4), fontSize = 13.sp)
            }
            Slider(
                value = channel.volume,
                onValueChange = { vm.setVolume(channel.id, it) },
                onValueChangeFinished = { vm.setVolume(channel.id, channel.volume, final = true) },
                colors = SliderDefaults.colors(
                    thumbColor = if (channel.muted) Color.Gray else accent,
                    activeTrackColor = if (channel.muted) Color.Gray else accent,
                ),
            )
        }
        IconButton(onClick = { vm.toggleMute(channel) }) {
            Text(if (channel.muted) "🔇" else "🔈", fontSize = 22.sp)
        }
    }
}
