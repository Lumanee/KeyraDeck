package com.phonedeck.app

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.delay

/** Bandeau de notification en haut de l'écran (quelques secondes) et liste des notifications reçues. */
@Composable
fun NotificationOverlay(vm: DeckViewModel) {
    val banner = vm.banner
    LaunchedEffect(banner?.id) {
        if (banner != null) {
            delay(5000)
            if (vm.banner?.id == banner.id) vm.banner = null
        }
    }
    if (banner != null) {
        Box(Modifier.fillMaxWidth().statusBarsPadding().padding(12.dp), contentAlignment = Alignment.TopCenter) {
            Column(
                Modifier
                    .fillMaxWidth()
                    .clip(RoundedCornerShape(16.dp))
                    .background(Color(0xF2202026))
                    .clickable { vm.banner = null; vm.showNotifs = true }
                    .padding(12.dp),
            ) {
                if (banner.app.isNotBlank()) Text(banner.app, color = LocalTheme.current.accent, fontSize = 11.sp, fontWeight = FontWeight.Bold, maxLines = 1)
                if (banner.title.isNotBlank()) Text(banner.title, color = Color.White, fontSize = 15.sp, fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis)
                if (banner.text.isNotBlank()) Text(banner.text, color = Color(0xFFDDDDE6), fontSize = 13.sp, maxLines = 3, overflow = TextOverflow.Ellipsis)
            }
        }
    }
    if (vm.showNotifs) {
        AlertDialog(
            onDismissRequest = { vm.showNotifs = false },
            title = { Text("Notifications") },
            text = {
                if (vm.notifications.isEmpty()) Text("Aucune notification", color = Color(0xFFBBBBC4))
                else LazyColumn(Modifier.heightIn(max = 380.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                    items(vm.notifications.toList(), key = { it.id }) { n ->
                        Column {
                            if (n.app.isNotBlank()) Text(n.app, color = LocalTheme.current.accent, fontSize = 11.sp, fontWeight = FontWeight.Bold)
                            if (n.title.isNotBlank()) Text(n.title, fontWeight = FontWeight.SemiBold)
                            if (n.text.isNotBlank()) Text(n.text, color = Color(0xFFBBBBC4), fontSize = 13.sp)
                        }
                    }
                }
            },
            confirmButton = { TextButton(onClick = { vm.showNotifs = false }) { Text("Fermer") } },
            dismissButton = { TextButton(onClick = { vm.clearNotifs() }) { Text("Tout effacer") } },
        )
    }
}

/** Petite pastille « 🔔 3 » dans la barre du boîtier. */
@Composable
fun NotificationBadge(vm: DeckViewModel) {
    val n = vm.notifications.size
    if (n == 0) return
    Row(
        Modifier
            .clip(CircleShape)
            .background(LocalTheme.current.active)
            .clickable { vm.showNotifs = true }
            .padding(horizontal = 7.dp, vertical = 2.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text("🔔 $n", color = Color.White, fontSize = 10.sp, fontWeight = FontWeight.Bold)
    }
}
