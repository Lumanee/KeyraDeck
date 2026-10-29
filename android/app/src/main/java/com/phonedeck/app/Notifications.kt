package com.phonedeck.app

import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.tween
import androidx.compose.foundation.gestures.detectHorizontalDragGestures
import androidx.compose.material3.OutlinedTextField
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.onSizeChanged
import kotlinx.coroutines.launch
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.size
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
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.delay
import kotlin.math.min

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
    // Une zone écran affichée montre déjà le bandeau elle-même.
    if (banner != null && vm.notifScreens == 0) {
        Box(Modifier.fillMaxWidth().statusBarsPadding().padding(12.dp), contentAlignment = Alignment.TopCenter) {
            SwipeAway(banner.id, onDismiss = { vm.dismissNotif(banner.id) }) {
                NotifCard(banner, LocalTheme.current.accent) { vm.banner = null; vm.showNotifs = true }
            }
        }
    }
    if (vm.showNotifs) {
        AlertDialog(
            onDismissRequest = { vm.showNotifs = false },
            title = { Text("Notifications") },
            text = {
                if (vm.notifications.isEmpty()) Text("Aucune notification", color = Color(0xFFBBBBC4))
                else Column {
                    Text("Glisse vers la droite pour effacer, touche pour répondre.", color = Color(0xFF8E8E98), fontSize = 11.sp)
                    LazyColumn(Modifier.heightIn(max = 380.dp).padding(top = 8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        items(vm.notifications.toList(), key = { it.id }) { n ->
                            SwipeAway(n.id, onDismiss = { vm.dismissNotif(n.id) }) { NotifCard(n, LocalTheme.current.accent) { vm.replyTo = n } }
                        }
                    }
                }
            },
            confirmButton = { TextButton(onClick = { vm.showNotifs = false }) { Text("Fermer") } },
            dismissButton = { TextButton(onClick = { vm.clearNotifs() }) { Text("Tout effacer") } },
        )
    }
    vm.replyTo?.let { ReplyDialog(vm, it) }
}

/**
 * Répondre à une notification. Windows ne laisse pas une autre appli utiliser le champ « Répondre » d'une notification :
 * le PC ouvre la fenêtre de l'appli et y tape le texte, dans la conversation ouverte à ce moment-là.
 */
@Composable
private fun ReplyDialog(vm: DeckViewModel, n: Notif) {
    var text by remember(n.id) { mutableStateOf("") }
    val app = n.app.ifBlank { "l'appli" }
    AlertDialog(
        onDismissRequest = { vm.replyTo = null },
        title = { Text("Répondre") },
        text = {
            Column {
                if (n.title.isNotBlank()) Text(n.title, fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis)
                if (n.text.isNotBlank()) Text(n.text, color = Color(0xFFBBBBC4), fontSize = 13.sp, maxLines = 3, overflow = TextOverflow.Ellipsis)
                OutlinedTextField(
                    value = text, onValueChange = { text = it.take(1000) },
                    placeholder = { Text("Ta réponse") },
                    modifier = Modifier.fillMaxWidth().padding(top = 10.dp),
                )
                Text("KeyraDeck ouvre $app sur le PC et tape ta réponse dans la conversation ouverte. Vérifie que c'est la bonne.",
                    color = Color(0xFF8E8E98), fontSize = 11.sp, modifier = Modifier.padding(top = 6.dp))
            }
        },
        confirmButton = { TextButton(enabled = text.isNotBlank(), onClick = { vm.replyNotif(n, text, pressEnter = true) }) { Text("Envoyer") } },
        dismissButton = {
            Row {
                TextButton(enabled = text.isNotBlank(), onClick = { vm.replyNotif(n, text, pressEnter = false) }) { Text("Écrire seulement") }
                TextButton(onClick = { vm.replyTo = null }) { Text("Annuler") }
            }
        },
    )
}

/** Glisser vers la droite pour effacer : la carte suit le doigt, part si on va assez loin, sinon revient. */
@Composable
fun SwipeAway(key: String, onDismiss: () -> Unit, content: @Composable () -> Unit) {
    val offset = remember(key) { Animatable(0f) }
    val scope = rememberCoroutineScope()
    var width by remember { mutableStateOf(1f) }
    Box(
        Modifier
            .fillMaxWidth()
            .onSizeChanged { width = it.width.toFloat().coerceAtLeast(1f) }
            .graphicsLayer {
                translationX = offset.value
                alpha = 1f - (offset.value / width).coerceIn(0f, 1f) * 0.8f
            }
            .pointerInput(key) {
                detectHorizontalDragGestures(
                    onDragEnd = {
                        scope.launch {
                            if (offset.value > width * 0.35f) { offset.animateTo(width, tween(150)); onDismiss() }
                            else offset.animateTo(0f, tween(150))
                        }
                    },
                    onDragCancel = { scope.launch { offset.animateTo(0f) } },
                ) { change, dx ->
                    change.consume()
                    scope.launch { offset.snapTo((offset.value + dx).coerceAtLeast(0f)) }
                }
            },
    ) { content() }
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

/** Bandeau puis liste des notifications, dessinés par-dessus la zone écran de la page. */
@Composable
fun ScreenNotifications(vm: DeckViewModel, width: Dp, height: Dp, unit: Dp) {
    DisposableEffect(Unit) {
        vm.notifScreens++
        onDispose { vm.notifScreens--; vm.notifsOnScreen = false }
    }
    val banner = vm.banner
    val shape = RoundedCornerShape(unit * 0.14f)
    val accent = LocalTheme.current.accent
    if (vm.notifsOnScreen) {
        Column(
            Modifier
                .size(width, height)
                .clip(shape)
                .background(Color(0xF0101014))
                .padding(10.dp),
        ) {
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                Text("🔔 Notifications", color = Color.White, fontSize = 13.sp, fontWeight = FontWeight.Bold, modifier = Modifier.weight(1f))
                if (vm.notifications.isNotEmpty()) Text("Tout effacer", color = accent, fontSize = 12.sp, modifier = Modifier.clickable { vm.clearNotifs() }.padding(horizontal = 6.dp, vertical = 2.dp))
                Text("✕", color = Color.White, fontSize = 15.sp, modifier = Modifier.clickable { vm.notifsOnScreen = false }.padding(horizontal = 6.dp, vertical = 2.dp))
            }
            if (vm.notifications.isEmpty()) {
                Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) { Text("Aucune notification", color = Color(0xFFBBBBC4), fontSize = 13.sp) }
            } else {
                LazyColumn(Modifier.fillMaxSize().padding(top = 6.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                    items(vm.notifications.toList(), key = { it.id }) { n ->
                        SwipeAway(n.id, onDismiss = { vm.dismissNotif(n.id) }) { NotifCard(n, accent) { vm.replyTo = n } }
                    }
                }
            }
        }
    } else if (banner != null) {
        Box(Modifier.size(width, height).padding(8.dp), contentAlignment = Alignment.TopCenter) {
            SwipeAway(banner.id, onDismiss = { vm.dismissNotif(banner.id) }) { NotifCard(banner, accent) { vm.openNotifs(onScreen = true) } }
        }
    }
}

/** Une notification : appli en couleur, titre, puis le message. Toucher = [onTap]. */
@Composable
private fun NotifCard(n: Notif, accent: Color, onTap: () -> Unit) {
    Column(
        Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(12.dp))
            .background(Color(0xF2202026))
            .clickable { onTap() }
            .padding(10.dp),
    ) {
        if (n.app.isNotBlank()) Text(n.app, color = accent, fontSize = 11.sp, fontWeight = FontWeight.Bold, maxLines = 1)
        if (n.title.isNotBlank()) Text(n.title, color = Color.White, fontSize = 14.sp, fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis)
        if (n.text.isNotBlank()) Text(n.text, color = Color(0xFFDDDDE6), fontSize = 12.sp, maxLines = 3, overflow = TextOverflow.Ellipsis)
    }
}

/**
 * Widget Notifications. 1×1 : une cloche avec le nombre ; les autres modèles montrent les derniers messages.
 * Toucher ouvre la liste : dans la zone écran si la page en a une ([screenHere]), sinon en fenêtre.
 */
@Composable
fun NotificationsWidget(widget: DeckWidget, vm: DeckViewModel, screenHere: Boolean) {
    val accent = LocalTheme.current.accent
    val active = LocalTheme.current.active
    val touch = LocalTouch.current
    val list = vm.notifications
    val open = { touch.touch(null, null, false); vm.openNotifs(screenHere) }
    if (widget.model == "notifications-1x1") {
        BoxWithConstraints(Modifier.fillMaxSize().clickable { open() }, contentAlignment = Alignment.Center) {
            val s = min(maxWidth.value, maxHeight.value)
            Text("🔔", fontSize = (s * 0.42f).sp)
            if (list.isNotEmpty()) {
                Text(
                    if (list.size > 99) "99+" else list.size.toString(),
                    color = Color.White, fontSize = (s * 0.17f).sp, fontWeight = FontWeight.Bold,
                    modifier = Modifier.align(Alignment.TopEnd).clip(CircleShape).background(active).padding(horizontal = 6.dp, vertical = 1.dp),
                )
            }
        }
        return
    }
    val shown = when (widget.model) { "notifications-2x1" -> 1; "notifications-3x2" -> 2; else -> 3 }
    Column(Modifier.fillMaxSize().clickable { open() }) {
        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            Text("🔔", fontSize = 12.sp)
            Text(if (list.isEmpty()) " Aucune notification" else " ${list.size} notification${if (list.size > 1) "s" else ""}",
                color = Color(0xFFBBBBC4), fontSize = 11.sp, maxLines = 1, modifier = Modifier.weight(1f))
        }
        Column(Modifier.fillMaxSize().padding(top = 4.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            for (n in list.take(shown)) {
                Column(Modifier.fillMaxWidth().weight(1f, fill = false)) {
                    Text(listOf(n.app, n.title).filter { it.isNotBlank() }.joinToString(" · "), color = accent, fontSize = 11.sp, fontWeight = FontWeight.Bold, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    if (n.text.isNotBlank()) Text(n.text, color = Color(0xFFDDDDE6), fontSize = 12.sp, maxLines = if (shown == 1) 2 else 1, overflow = TextOverflow.Ellipsis)
                }
            }
        }
    }
}
