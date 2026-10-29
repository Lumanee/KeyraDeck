package com.phonedeck.app

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.IntRect
import androidx.compose.ui.unit.IntSize
import androidx.compose.ui.unit.LayoutDirection
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Popup
import androidx.compose.ui.window.PopupPositionProvider
import androidx.compose.ui.window.PopupProperties

private val DiscordRed = Color(0xFFED4245)

/** Au-dessus de la touche s'il y a la place, sinon en dessous ; centré sur elle sans sortir de l'écran. */
private class AboveOrBelow(private val gap: Int) : PopupPositionProvider {
    override fun calculatePosition(anchorBounds: IntRect, windowSize: IntSize, layoutDirection: LayoutDirection, popupContentSize: IntSize): IntOffset {
        val x = (anchorBounds.center.x - popupContentSize.width / 2).coerceIn(0, maxOf(0, windowSize.width - popupContentSize.width))
        val above = anchorBounds.top - gap - popupContentSize.height
        return IntOffset(x, if (above >= 0) above else anchorBounds.bottom + gap)
    }
}

/**
 * Menu d'un bouton Discord : micro et sourdine (état réel), partage d'écran et raccrocher.
 * Il s'ouvre au-dessus ou en dessous de la touche ; toucher ailleurs le referme.
 */
@Composable
fun DiscordPanel(vm: DeckViewModel, key: Dp) {
    val d = vm.discord
    val gap = with(LocalDensity.current) { 8.dp.roundToPx() }
    Popup(
        popupPositionProvider = AboveOrBelow(gap),
        onDismissRequest = { vm.discordPanel = null },
        properties = PopupProperties(focusable = true),
    ) {
        val shape = RoundedCornerShape(key * 0.22f)
        Box(
            Modifier
                .shadow(12.dp, shape)
                .clip(shape)
                .background(Color(0xF0141418))
                .border(1.dp, Color(0x33FFFFFF), shape)
                .padding(key * 0.1f),
        ) {
            if (d == null || !d.connected) {
                Text(
                    if (d == null) "Discord…" else "Discord : " + d.status,
                    color = Color(0xFFBBBBC4), fontSize = 12.sp, textAlign = TextAlign.Center,
                    modifier = Modifier.widthIn(max = key * 3).padding(6.dp),
                )
                return@Box
            }
            val active = LocalTheme.current.active
            val micOff = d.muted || d.deafened
            Row(horizontalArrangement = Arrangement.spacedBy(key * 0.1f)) {
                PanelButton(if (micOff) "🔇" else "🎙", if (micOff) "Micro coupé" else "Micro", if (micOff) active else null, true, key) {
                    vm.sendDiscord("mute")
                }
                PanelButton("🎧", if (d.deafened) "Sourdine" else "Son", if (d.deafened) active else null, true, key) {
                    vm.sendDiscord("deafen")
                }
                PanelButton("🖥", "Partager", null, d.inCall, key) {
                    vm.sendDiscord("screen"); vm.discordPanel = null
                }
                PanelButton("📞", "Raccrocher", DiscordRed, d.inCall, key) {
                    vm.sendDiscord("leave"); vm.discordPanel = null
                }
            }
        }
    }
}

@Composable
private fun PanelButton(symbol: String, label: String, color: Color?, enabled: Boolean, key: Dp, onTap: () -> Unit) {
    val touch = LocalTouch.current
    val size = key * 0.78f
    Column(
        Modifier.alpha(if (enabled) 1f else 0.35f).widthIn(min = size),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Box(
            Modifier
                .size(size)
                .clip(RoundedCornerShape(50))
                .background(color ?: Color(0x26FFFFFF))
                .clickable(enabled = enabled) { touch.touch(null, null, false); onTap() },
            contentAlignment = Alignment.Center,
        ) { Text(symbol, fontSize = (size.value * 0.4f).sp) }
        Text(label, color = Color(0xFFDDDDE4), fontSize = 10.sp, fontWeight = FontWeight.Medium, maxLines = 1, softWrap = false,
            modifier = Modifier.padding(top = 3.dp))
    }
}
