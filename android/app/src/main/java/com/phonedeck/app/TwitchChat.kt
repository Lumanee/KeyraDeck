package com.phonedeck.app

import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.InlineTextContent
import androidx.compose.foundation.text.appendInlineContent
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.derivedStateOf
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.lerp
import androidx.compose.ui.graphics.luminance
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.Placeholder
import androidx.compose.ui.text.PlaceholderVerticalAlign
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.text.withStyle
import androidx.compose.ui.unit.TextUnit
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.launch

/** Couleurs que Twitch donne aux pseudos qui n'en ont pas choisi. */
private val DefaultNickColors = listOf(
    0xFFFF0000, 0xFF0000FF, 0xFF00FF00, 0xFFB22222, 0xFFFF7F50, 0xFF9ACD32, 0xFFFF4500, 0xFF2E8B57,
    0xFFDAA520, 0xFFD2691E, 0xFF5F9EA0, 0xFF1E90FF, 0xFFFF69B4, 0xFF8A2BE2, 0xFF00FF7F,
).map { Color(it) }

/** Couleur du pseudo, éclaircie si elle est trop sombre pour se lire sur le verre noir du widget. */
private fun nickColor(m: TwitchMsg): Color {
    val c = Regex("^#[0-9A-Fa-f]{6}$").takeIf { it.matches(m.color) }?.let { Color(0xFF000000 or m.color.substring(1).toLong(16)) }
        ?: DefaultNickColors[(m.user.hashCode() and 0x7fffffff) % DefaultNickColors.size]
    return if (c.luminance() < 0.18f) lerp(c, Color.White, 0.45f) else c
}

/**
 * Widget « Chat Twitch » : le chat de la chaîne choisie dans le configurateur, en lecture seule.
 * Les nouveaux messages arrivent en bas et la liste les suit, sauf si on est remonté lire plus haut.
 */
@Composable
fun TwitchChatWidget(widget: DeckWidget, vm: DeckViewModel) {
    val accent = LocalTheme.current.accent
    val touch = LocalTouch.current
    val messages = vm.twitchMessages
    val newestFirst = messages.toList().asReversed()
    val list = rememberLazyListState()
    val scope = rememberCoroutineScope()
    val atBottom by remember { derivedStateOf { list.firstVisibleItemIndex <= 1 } }
    val newest = messages.lastOrNull()?.id
    LaunchedEffect(newest) { if (newest != null && atBottom) list.animateScrollToItem(0) }

    BoxWithConstraints(Modifier.fillMaxSize()) {
        val fs = (maxWidth.value / 24f).coerceIn(11f, 15f).sp
        Column(Modifier.fillMaxSize()) {
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(5.dp)) {
                val dot = when (vm.twitchState) { "ok" -> Color(0xFF23A55A); "error" -> Color(0xFFE5484D); else -> Color(0xFF888890) }
                Box(Modifier.size(7.dp).clip(CircleShape).background(dot))
                Text(
                    "💬 " + vm.twitchChannel.ifEmpty { "Chat Twitch" },
                    color = Color(0xFFBBBBC4), fontSize = 11.sp, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f),
                )
            }
            Box(Modifier.fillMaxWidth().weight(1f).padding(top = 3.dp)) {
                if (messages.isEmpty()) {
                    val hint = when {
                        vm.twitchChannel.isEmpty() -> "Choisis une chaîne dans le configurateur du PC"
                        vm.twitchState == "ok" -> "En attente de messages…"
                        else -> vm.twitchStatus.ifEmpty { "Connexion…" }
                    }
                    Text(hint, color = Color(0xFF9999A3), fontSize = 12.sp, modifier = Modifier.align(Alignment.Center))
                } else {
                    LazyColumn(Modifier.fillMaxSize(), state = list, reverseLayout = true, verticalArrangement = Arrangement.spacedBy(3.dp)) {
                        items(newestFirst, key = { it.id }) { m -> TwitchLine(m, vm, fs) }
                    }
                    // Remonté dans l'historique : un bouton pour revenir aux derniers messages.
                    if (!atBottom) {
                        Text(
                            "↓ Derniers messages",
                            color = LocalTheme.current.onAccent, fontSize = 11.sp, fontWeight = FontWeight.Bold,
                            modifier = Modifier
                                .align(Alignment.BottomCenter)
                                .padding(bottom = 4.dp)
                                .clip(RoundedCornerShape(50))
                                .background(accent)
                                .clickable { touch.touch(null, null, false); scope.launch { list.animateScrollToItem(0) } }
                                .padding(horizontal = 10.dp, vertical = 3.dp),
                        )
                    }
                }
            }
        }
    }
}

@Composable
private fun TwitchLine(m: TwitchMsg, vm: DeckViewModel, fs: TextUnit) {
    val nick = nickColor(m)
    val emoteIds = m.parts.mapNotNull { it.emote }.distinct()
    LaunchedEffect(m.id) { emoteIds.forEach { vm.requestEmote(it) } }
    val text = buildAnnotatedString {
        withStyle(SpanStyle(color = nick, fontWeight = FontWeight.Bold)) { append(m.user) }
        append(if (m.action) " " else ": ")
        val style = if (m.action) SpanStyle(color = nick, fontStyle = FontStyle.Italic) else SpanStyle(color = Color(0xFFEDEDF2))
        withStyle(style) {
            for (p in m.parts) {
                if (p.emote != null) appendInlineContent("e:" + p.emote, p.text) else append(p.text)
            }
        }
    }
    val emoteSize = (fs.value * 1.7f).sp
    val inline = emoteIds.associate { id ->
        "e:$id" to InlineTextContent(Placeholder(emoteSize, emoteSize, PlaceholderVerticalAlign.TextCenter)) { alt ->
            val img = vm.twitchEmotes[id]
            if (img != null) Image(img, contentDescription = alt, contentScale = ContentScale.Fit, modifier = Modifier.fillMaxSize())
            else Text(alt, color = Color(0xFF9999A3), fontSize = (fs.value * 0.7f).sp, maxLines = 1)
        }
    }
    Text(text, inlineContent = inline, fontSize = fs, lineHeight = (fs.value * 1.75f).sp, modifier = Modifier.fillMaxWidth())
}
