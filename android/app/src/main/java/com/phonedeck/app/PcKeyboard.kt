package com.phonedeck.app

import android.app.Activity
import android.content.pm.ActivityInfo
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.gestures.waitForUpOrCancellation
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxScope
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.systemBarsPadding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.lerp
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

/**
 * Une touche du clavier PC. [vk] = code de touche virtuelle Windows ; [main] / [shift] / [altGr] = ce qui est
 * gravé sur la touche (AZERTY français) ; [w] = largeur (1 = touche normale).
 */
private data class PcKey(
    val vk: Int,
    val main: String,
    val shift: String? = null,
    val altGr: String? = null,
    val w: Float = 1f,
    val letter: Boolean = false,
)

private const val VK_CAPS = 0x14
private const val VK_LSHIFT = 0xA0
private const val VK_RSHIFT = 0xA1
private const val VK_LCTRL = 0xA2
private const val VK_RCTRL = 0xA3
private const val VK_ALT = 0xA4
private const val VK_ALTGR = 0xA5
private const val VK_WIN = 0x5B
private val Modifiers = setOf(VK_LSHIFT, VK_RSHIFT, VK_LCTRL, VK_RCTRL, VK_ALT, VK_ALTGR, VK_WIN)

private fun letter(c: Char, altGr: String? = null) = PcKey(c.uppercaseChar().code, c.toString(), altGr = altGr, letter = true)

private val FunctionRow = listOf(PcKey(0x1B, "Échap")) +
    (1..12).map { PcKey(0x6F + it, "F$it") } +
    listOf(PcKey(0x2C, "Impr"), PcKey(0x2E, "Suppr"))

private val Rows = listOf(
    listOf(
        PcKey(0xDE, "²"), PcKey(0x31, "&", "1"), PcKey(0x32, "é", "2", "~"), PcKey(0x33, "\"", "3", "#"),
        PcKey(0x34, "'", "4", "{"), PcKey(0x35, "(", "5", "["), PcKey(0x36, "-", "6", "|"), PcKey(0x37, "è", "7", "`"),
        PcKey(0x38, "_", "8", "\\"), PcKey(0x39, "ç", "9", "^"), PcKey(0x30, "à", "0", "@"), PcKey(0xDB, ")", "°", "]"),
        PcKey(0xBB, "=", "+", "}"), PcKey(0x08, "⌫", w = 2f),
    ),
    listOf(PcKey(0x09, "Tab ⇥", w = 1.5f)) +
        "azertyuiop".map { if (it == 'e') letter(it, "€") else letter(it) } +
        listOf(PcKey(0xDD, "^", "¨"), PcKey(0xBA, "$", "£", "¤"), PcKey(0x0D, "Entrée", w = 1.5f)),
    listOf(PcKey(VK_CAPS, "Verr. Maj", w = 1.75f)) +
        "qsdfghjklm".map { letter(it) } +
        listOf(PcKey(0xC0, "ù", "%"), PcKey(0xDC, "*", "µ"), PcKey(0x0D, "⏎", w = 1.25f)),
    listOf(PcKey(VK_LSHIFT, "⇧ Maj", w = 1.25f), PcKey(0xE2, "<", ">")) +
        "wxcvbn".map { letter(it) } +
        listOf(PcKey(0xBC, ",", "?"), PcKey(0xBE, ";", "."), PcKey(0xBF, ":", "/"), PcKey(0xDF, "!", "§"), PcKey(VK_RSHIFT, "⇧ Maj", w = 2.75f)),
)

private val BottomRow = listOf(
    PcKey(VK_LCTRL, "Ctrl", w = 1.5f), PcKey(VK_WIN, "Win", w = 1.25f), PcKey(VK_ALT, "Alt", w = 1.25f),
    PcKey(0x20, "", w = 5.75f), PcKey(VK_ALTGR, "Alt Gr", w = 1.25f), PcKey(VK_RCTRL, "Ctrl"),
)

private val NavKeys = listOf(
    PcKey(0x2D, "Inser"), PcKey(0x24, "Début"), PcKey(0x23, "Fin"), PcKey(0x21, "Pg ↑"), PcKey(0x22, "Pg ↓"),
)

/**
 * État des touches spéciales, comme les « touches rémanentes » de Windows :
 * - maintenir Ctrl d'un doigt et taper C de l'autre = Ctrl+C ;
 * - toucher Ctrl une fois = Ctrl reste enfoncé jusqu'à la prochaine touche (pratique d'une seule main) ;
 * - retoucher Ctrl = on le relâche.
 */
private class PcKeyboardState(private val vm: DeckViewModel) {
    var held by mutableStateOf(setOf<Int>())
    var latched by mutableStateOf(setOf<Int>())
    var caps by mutableStateOf(false)
    private val usedWhileHeld = mutableSetOf<Int>()
    private val unlatching = mutableSetOf<Int>()

    fun isOn(vk: Int) = vk in held || vk in latched
    val shift get() = isOn(VK_LSHIFT) || isOn(VK_RSHIFT)
    val altGr get() = isOn(VK_ALTGR) || ((isOn(VK_LCTRL) || isOn(VK_RCTRL)) && isOn(VK_ALT))

    fun down(vk: Int) {
        if (vk in Modifiers) {
            if (vk in latched) { unlatching += vk; return }
            vm.pcKey(vk, true)
            held = held + vk
            usedWhileHeld -= vk
        } else {
            usedWhileHeld += held
            if (vk == VK_CAPS) caps = !caps
            vm.pcKey(vk, true)
        }
    }

    /** Touche gardée enfoncée : on la répète comme un vrai clavier. */
    fun repeat(vk: Int) = vm.pcKey(vk, true)

    fun up(vk: Int) {
        if (vk in Modifiers) {
            if (unlatching.remove(vk)) {
                vm.pcKey(vk, false)
                latched = latched - vk
                return
            }
            held = held - vk
            if (vk in usedWhileHeld) vm.pcKey(vk, false) else latched = latched + vk
        } else {
            vm.pcKey(vk, false)
            if (latched.isNotEmpty()) {
                latched.forEach { vm.pcKey(it, false) }
                latched = emptySet()
            }
        }
    }

    fun releaseAll() {
        (held + latched).forEach { vm.pcKey(it, false) }
        held = emptySet()
        latched = emptySet()
        unlatching.clear()
    }
}

/** Clavier d'ordinateur complet (AZERTY) en plein écran, en paysage. Chaque touche est envoyée telle quelle au PC. */
@Composable
fun PcKeyboardScreen(vm: DeckViewModel, onClose: () -> Unit) {
    BackHandler(onBack = onClose)
    val theme = LocalTheme.current
    val settings = LocalSettings.current
    val context = LocalContext.current
    val state = remember { PcKeyboardState(vm) }
    DisposableEffect(Unit) {
        val activity = context as? Activity
        activity?.requestedOrientation = ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE
        onDispose {
            state.releaseAll()
            activity?.requestedOrientation = orientationFor(settings)
        }
    }
    Column(
        Modifier
            .fillMaxSize()
            .background(theme.desk)
            .systemBarsPadding()
            .padding(horizontal = 6.dp, vertical = 4.dp),
        verticalArrangement = Arrangement.spacedBy(4.dp),
    ) {
        // Barre du haut : retour à la souris + touches de navigation.
        Row(Modifier.fillMaxWidth().height(34.dp), horizontalArrangement = Arrangement.spacedBy(4.dp)) {
            Box(
                Modifier.fillMaxHeight().clip(RoundedCornerShape(8.dp)).background(Color(0x26FFFFFF)).clickable(onClick = onClose).padding(horizontal = 14.dp),
                contentAlignment = Alignment.Center,
            ) { Text("🖱  Souris", color = Color.White, fontSize = 13.sp, fontWeight = FontWeight.SemiBold) }
            Box(Modifier.weight(1f))
            for (k in NavKeys) KeyCap(k, state, Modifier.weight(0.55f).fillMaxHeight())
        }
        KeyRow(FunctionRow, state, 0.75f)
        for (row in Rows) KeyRow(row, state, 1f)
        Row(Modifier.fillMaxWidth().weight(1f), horizontalArrangement = Arrangement.spacedBy(4.dp)) {
            for (k in BottomRow) KeyCap(k, state, Modifier.weight(k.w).fillMaxHeight())
            // Flèches : ← (↑ au-dessus de ↓) →
            KeyCap(PcKey(0x25, "←"), state, Modifier.weight(1f).fillMaxHeight())
            Column(Modifier.weight(1f).fillMaxHeight(), verticalArrangement = Arrangement.spacedBy(3.dp)) {
                KeyCap(PcKey(0x26, "↑"), state, Modifier.weight(1f).fillMaxWidth())
                KeyCap(PcKey(0x28, "↓"), state, Modifier.weight(1f).fillMaxWidth())
            }
            KeyCap(PcKey(0x27, "→"), state, Modifier.weight(1f).fillMaxHeight())
        }
    }
}

@Composable
private fun ColumnScope.KeyRow(keys: List<PcKey>, state: PcKeyboardState, height: Float) {
    Row(Modifier.fillMaxWidth().weight(height), horizontalArrangement = Arrangement.spacedBy(4.dp)) {
        for (k in keys) KeyCap(k, state, Modifier.weight(k.w).fillMaxHeight())
    }
}

/** Une touche : enfoncée tant que le doigt reste dessus (plusieurs doigts possibles), répétée si on la garde. */
@Composable
private fun KeyCap(k: PcKey, state: PcKeyboardState, modifier: Modifier) {
    val theme = LocalTheme.current
    val touch = LocalTouch.current
    val scope = rememberCoroutineScope()
    var pressed by remember { mutableStateOf(false) }
    val isMod = k.vk in Modifiers
    val lit = pressed || (isMod && state.isOn(k.vk)) || (k.vk == VK_CAPS && state.caps)
    // Touches de caractère un peu plus claires que les touches spéciales, comme sur un vrai clavier.
    val special = !k.letter && k.shift == null && (k.main.length > 1 || k.vk == 0x20)
    val base = lerp(theme.key, Color.White, if (isMod || special) 0.04f else 0.10f)
    Box(
        modifier
            .clip(RoundedCornerShape(7.dp))
            .background(if (lit) theme.accent else base)
            .border(1.dp, Color(0x1AFFFFFF), RoundedCornerShape(7.dp))
            .pointerInput(k.vk) {
                awaitEachGesture {
                    awaitFirstDown(requireUnconsumed = false)
                    pressed = true
                    touch.touch(null, null, false)
                    state.down(k.vk)
                    val repeat = if (isMod || k.vk == VK_CAPS) null else scope.launch {
                        delay(450)
                        while (true) { state.repeat(k.vk); delay(40) }
                    }
                    waitForUpOrCancellation()
                    repeat?.cancel()
                    pressed = false
                    state.up(k.vk)
                }
            },
        contentAlignment = Alignment.Center,
    ) {
        when {
            k.letter -> {
                val upper = state.shift != state.caps
                Text(if (upper) k.main.uppercase() else k.main, color = Color.White, fontSize = 16.sp, fontWeight = FontWeight.Medium)
                if (k.altGr != null) Corner(k.altGr, state.altGr, Alignment.BottomEnd)
            }
            k.shift != null -> {
                // Comme sur une vraie touche : symbole avec Maj en haut, symbole normal en bas, Alt Gr à droite.
                val useShift = state.shift
                Text(k.shift, color = Color.White.copy(alpha = if (useShift) 1f else 0.5f), fontSize = if (useShift) 15.sp else 10.sp,
                    modifier = Modifier.align(Alignment.TopStart).padding(start = 5.dp, top = 2.dp))
                Text(k.main, color = Color.White.copy(alpha = if (useShift || state.altGr) 0.5f else 1f), fontSize = if (useShift) 10.sp else 15.sp,
                    modifier = Modifier.align(Alignment.BottomStart).padding(start = 5.dp, bottom = 2.dp))
                if (k.altGr != null) Corner(k.altGr, state.altGr, Alignment.BottomEnd)
            }
            else -> Text(
                k.main, color = Color.White, fontSize = if (k.main.length <= 2) 16.sp else 11.sp,
                fontWeight = FontWeight.SemiBold, textAlign = TextAlign.Center, maxLines = 1,
            )
        }
    }
}

@Composable
private fun BoxScope.Corner(text: String, on: Boolean, align: Alignment) {
    Text(
        text, color = if (on) Color.White else Color.White.copy(alpha = 0.4f), fontSize = if (on) 13.sp else 9.sp,
        modifier = Modifier.align(align).padding(end = 4.dp, bottom = 2.dp),
    )
}
