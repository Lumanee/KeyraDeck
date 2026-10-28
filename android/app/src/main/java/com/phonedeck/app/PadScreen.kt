package com.phonedeck.app

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.gestures.waitForUpOrCancellation
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.lerp
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.TextFieldValue
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlin.math.abs
import kotlin.math.max

private const val Sentinel = "​"

/**
 * Page « souris / clavier » : un trackpad (clic gauche / droit en bas), un vrai clavier d'ordinateur en plein écran,
 * et le clavier du téléphone pour taper du texte (dictée, emojis…).
 */
@Composable
fun PadScreen(vm: DeckViewModel) {
    var pcKeyboard by remember { mutableStateOf(false) }
    if (pcKeyboard) {
        PcKeyboardScreen(vm) { pcKeyboard = false }
        return
    }
    BackHandler { vm.showPad = false }
    val theme = LocalTheme.current
    var keyboard by remember { mutableStateOf(false) }
    val mods = remember { mutableStateOf(setOf<String>()) }
    val surface = lerp(theme.body, Color.Black, 0.35f)
    Column(
        Modifier
            .fillMaxSize()
            .background(theme.desk)
            .padding(10.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            Text("Souris et clavier", color = Color.White, fontSize = 18.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.weight(1f))
            PadChip("⌨ Clavier", false) { keyboard = false; pcKeyboard = true }
            Box(Modifier.width(8.dp))
            PadChip(if (keyboard) "Texte ✓" else "Texte", keyboard) { keyboard = !keyboard }
            Box(Modifier.width(8.dp))
            Box(
                Modifier.size(40.dp).clip(CircleShape).background(Color(0x26FFFFFF)).clickable { vm.showPad = false },
                contentAlignment = Alignment.Center,
            ) { Text("✕", color = Color.White, fontSize = 18.sp, fontWeight = FontWeight.Bold) }
        }
        if (keyboard) {
            Row(Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                for (m in listOf("Ctrl", "Alt", "Shift", "Win")) {
                    PadChip(m, m in mods.value) { mods.value = if (m in mods.value) mods.value - m else mods.value + m }
                }
                for ((label, key) in listOf("Échap" to "Escape", "Tab" to "Tab", "←" to "Left", "↑" to "Up", "↓" to "Down", "→" to "Right", "Suppr" to "Delete", "Début" to "Home", "Fin" to "End")) {
                    PadChip(label, false) { vm.typeKey((mods.value.toList() + key).joinToString("+")); mods.value = emptySet() }
                }
            }
            KeyboardCatcher(vm, mods.value) { mods.value = emptySet() }
        }
        // Trackpad
        Box(
            Modifier
                .weight(1f)
                .fillMaxWidth()
                .clip(RoundedCornerShape(22.dp))
                .background(surface)
                .border(1.dp, Color(0x22FFFFFF), RoundedCornerShape(22.dp))
                .pointerInput(Unit) { trackpad(vm) },
            contentAlignment = Alignment.Center,
        ) {
            Text("Glisse pour déplacer · touche pour cliquer\n2 doigts : défiler · 2 doigts (appui) : clic droit", color = Color(0x66FFFFFF), fontSize = 12.sp, textAlign = TextAlign.Center)
        }
        Row(Modifier.fillMaxWidth().height(72.dp), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            PadButton("Clic gauche", "left", vm, Modifier.weight(1f))
            PadButton("Clic droit", "right", vm, Modifier.weight(1f))
        }
    }
}

/** Gestes du trackpad : 1 doigt = déplacer, tap = clic gauche, 2 doigts glissés = molette, tap à 2 doigts = clic droit. */
private suspend fun androidx.compose.ui.input.pointer.PointerInputScope.trackpad(vm: DeckViewModel) {
    awaitEachGesture {
        val down = awaitFirstDown()
        val start = down.uptimeMillis
        var travelled = 0f
        var maxPointers = 1
        var scrollAcc = 0f
        do {
            val event = awaitPointerEvent()
            val pressed = event.changes.filter { it.pressed }
            maxPointers = max(maxPointers, pressed.size)
            if (pressed.size >= 2) {
                val dy = pressed.map { it.position.y - it.previousPosition.y }.average().toFloat()
                travelled += abs(dy)
                scrollAcc += dy
                if (abs(scrollAcc) >= 10f) {
                    vm.mouseScroll(((scrollAcc / 10f).toInt()) * 40)
                    scrollAcc %= 10f
                }
            } else if (pressed.size == 1) {
                val c = pressed[0]
                val d: Offset = c.position - c.previousPosition
                travelled += abs(d.x) + abs(d.y)
                // Un peu d'accélération : les gestes rapides vont plus loin.
                val speed = (abs(d.x) + abs(d.y))
                val gain = 1.5f + (speed / 25f).coerceAtMost(1.5f)
                vm.mouseMove(d.x * gain, d.y * gain)
            }
            event.changes.forEach { it.consume() }
        } while (event.changes.any { it.pressed })
        vm.flushMouse()
        val duration = android.os.SystemClock.uptimeMillis() - start
        if (travelled < 16f && duration < 300) vm.mouseClick(if (maxPointers >= 2) "right" else "left")
    }
}

@Composable
private fun PadChip(label: String, on: Boolean, onClick: () -> Unit) {
    Box(
        Modifier
            .clip(RoundedCornerShape(10.dp))
            .background(if (on) LocalTheme.current.accent else Color(0x26FFFFFF))
            .clickable(onClick = onClick)
            .padding(horizontal = 12.dp, vertical = 8.dp),
        contentAlignment = Alignment.Center,
    ) { Text(label, color = Color.White, fontSize = 13.sp, fontWeight = FontWeight.SemiBold) }
}

/** Bouton de souris maintenu : appuyer = enfoncé, relâcher = relâché (permet de glisser-déposer). */
@Composable
private fun PadButton(label: String, button: String, vm: DeckViewModel, modifier: Modifier) {
    var pressed by remember { mutableStateOf(false) }
    Box(
        modifier
            .fillMaxSize()
            .clip(RoundedCornerShape(18.dp))
            .background(if (pressed) LocalTheme.current.accent else lerp(LocalTheme.current.key, Color.White, 0.08f))
            .pointerInput(button) {
                awaitEachGesture {
                    awaitFirstDown()
                    pressed = true
                    vm.mouseButton(button, true)
                    waitForUpOrCancellation()
                    pressed = false
                    vm.mouseButton(button, false)
                }
            },
        contentAlignment = Alignment.Center,
    ) { Text(label, color = Color.White, fontSize = 15.sp, fontWeight = FontWeight.SemiBold) }
}

/** Champ invisible qui ouvre le clavier du téléphone et envoie ce qu'on tape au PC (bouton « Texte »). */
@Composable
private fun KeyboardCatcher(vm: DeckViewModel, mods: Set<String>, onSent: () -> Unit) {
    val focus = remember { FocusRequester() }
    val controller = LocalSoftwareKeyboardController.current
    var value by remember { mutableStateOf(TextFieldValue(Sentinel, selection = androidx.compose.ui.text.TextRange(1))) }
    LaunchedEffect(Unit) { focus.requestFocus(); controller?.show() }
    BasicTextField(
        value = value,
        onValueChange = { nv ->
            val t = nv.text
            when {
                t.isEmpty() -> { vm.typeKey("Back"); onSent() }
                t.length > 1 -> {
                    val typed = t.replace(Sentinel, "")
                    if (mods.isNotEmpty() && typed.length == 1) vm.typeKey((mods.toList() + typed.uppercase()).joinToString("+")) else vm.typeText(typed)
                    onSent()
                }
            }
            value = TextFieldValue(Sentinel, selection = androidx.compose.ui.text.TextRange(1))
        },
        keyboardOptions = KeyboardOptions(autoCorrectEnabled = false, keyboardType = KeyboardType.Text, imeAction = ImeAction.Send),
        keyboardActions = KeyboardActions(onSend = { vm.typeKey("Enter") }),
        modifier = Modifier.size(1.dp).alpha(0f).focusRequester(focus),
    )
}
