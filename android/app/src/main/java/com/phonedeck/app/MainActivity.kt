package com.phonedeck.app

import android.content.Intent
import android.os.Bundle
import android.view.WindowManager
import android.widget.Toast
import android.app.Activity
import android.content.pm.ActivityInfo
import androidx.activity.ComponentActivity
import androidx.activity.compose.BackHandler
import androidx.activity.compose.setContent
import androidx.activity.viewModels
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Slider
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Switch
import androidx.compose.runtime.remember
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.compositionLocalOf
import androidx.compose.ui.unit.sp
import androidx.compose.foundation.layout.BoxScope
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.background
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.ui.Alignment
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp

class MainActivity : ComponentActivity() {
    private val vm: DeckViewModel by viewModels()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        // Un deck doit rester allumé pendant qu'on joue.
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        // Plein écran : on cache les barres d'Android pour l'effet « boîtier ». Un glissement les fait revenir.
        WindowCompat.getInsetsController(window, window.decorView).apply {
            hide(WindowInsetsCompat.Type.systemBars())
            systemBarsBehavior = WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
        }
        setContent { App(vm) }
        handleUsbIntent(intent)
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        handleUsbIntent(intent)
    }

    /** Le PC ouvre l'appli par le câble USB (adb) et donne son adresse, comme le QR code. */
    private fun handleUsbIntent(intent: Intent?) {
        if (intent == null) return
        val url = intent.getStringExtra(EXTRA_USB_PAIR) ?: return
        intent.removeExtra(EXTRA_USB_PAIR)
        vm.pairFromUsb(url)
    }

    companion object {
        const val EXTRA_USB_PAIR = "keyradeck_pair"
    }
}

val Background = Color(0xFF111114)
val Tile = Color(0xFF26262C)

val LocalTheme = compositionLocalOf { DeckTheme.Default }
val LocalSettings = compositionLocalOf { AppSettings() }

/** Vibration + son au toucher : touch(vibrationDuBouton, sonDuBouton, appuiLong). */
fun interface Touch { fun touch(vibrate: Int?, sound: String?, long: Boolean) }
val LocalTouch = compositionLocalOf { Touch { _, _, _ -> } }

/** Orientation choisie dans les réglages du téléphone. */
fun orientationFor(settings: AppSettings) = when (settings.orientation) {
    1 -> ActivityInfo.SCREEN_ORIENTATION_PORTRAIT
    2 -> ActivityInfo.SCREEN_ORIENTATION_LANDSCAPE
    else -> ActivityInfo.SCREEN_ORIENTATION_UNSPECIFIED
}

@Composable
fun App(vm: DeckViewModel) {
    val context = LocalContext.current
    LaunchedEffect(Unit) {
        vm.toasts.collect { Toast.makeText(context, it, Toast.LENGTH_SHORT).show() }
    }

    // Réglages du téléphone : écran allumé et orientation.
    val settings = vm.settings
    LaunchedEffect(settings.keepScreenOn, settings.orientation) {
        val activity = context as? Activity ?: return@LaunchedEffect
        if (settings.keepScreenOn) activity.window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        else activity.window.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        activity.requestedOrientation = orientationFor(settings)
    }

    val theme = vm.layout?.theme ?: DeckTheme.Default
    val touch = remember(settings) { Touch { v, s, long -> vm.feedback.touch(settings, v, s, long) } }
    CompositionLocalProvider(LocalTheme provides theme, LocalSettings provides settings, LocalTouch provides touch) {
        // Thème OLED : fond noir pur partout (les pixels restent éteints).
        val bg = if (theme.pureBlack) Color.Black else Background
        MaterialTheme(colorScheme = darkColorScheme(primary = theme.accent, onPrimary = theme.onAccent, background = bg, surface = bg)) {
            Box(Modifier.fillMaxSize()) {
                Surface(Modifier.fillMaxSize(), color = bg) {
                    when {
                        vm.serverUrl == null -> ConnectScreen(vm)
                        vm.showPad -> PadScreen(vm)
                        else -> DeckScreen(vm)
                    }
                }
                if (vm.serverUrl != null && vm.mixerOpen) MixerDialog(vm)
                UpdateBanner(vm)
                NotificationOverlay(vm)
                if (vm.showSettings) SettingsDialog(vm)
                if (vm.showUsbGuide) UsbGuideDialog(vm)
                vm.pendingUsbPair?.let { url ->
                    AlertDialog(
                        onDismissRequest = { vm.pendingUsbPair = null },
                        title = { Text("Changer de PC ?") },
                        text = { Text("Un autre PC branché par câble USB propose de se connecter. Utiliser ce PC à la place de l'actuel ?") },
                        confirmButton = { TextButton(onClick = { vm.pendingUsbPair = null; vm.pair(url) }) { Text("Utiliser ce PC") } },
                        dismissButton = { TextButton(onClick = { vm.pendingUsbPair = null }) { Text("Garder l'actuel") } },
                    )
                }
            }
        }
    }
}

@Composable
private fun SettingsDialog(vm: DeckViewModel) {
    val s = vm.settings
    AlertDialog(
        onDismissRequest = { vm.showSettings = false },
        title = { Text("Réglages") },
        text = {
            Column(Modifier.verticalScroll(rememberScrollState())) {
                SettingSwitch("Vibration au toucher", s.haptics) {
                    vm.updateSettings(s.copy(haptics = it))
                    if (it) vm.feedback.vibrate(s.hapticStrength)
                }
                if (s.haptics) {
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        VibrationLevels.forEach { (level, label) ->
                            FilterChip(
                                selected = s.hapticStrength == level,
                                onClick = { vm.updateSettings(s.copy(hapticStrength = level)); vm.feedback.vibrate(level) },
                                label = { Text(label) },
                            )
                        }
                    }
                }
                SettingSwitch("Son au toucher", s.sound != "off") { on ->
                    val name = if (on) TouchSounds.first().first else "off"
                    vm.updateSettings(s.copy(sound = name))
                    if (on) vm.feedback.play(name, s.soundVolume)
                }
                if (s.sound != "off") {
                    Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                        TouchSounds.forEach { (name, label) ->
                            FilterChip(
                                selected = s.sound == name,
                                onClick = { vm.updateSettings(s.copy(sound = name)); vm.feedback.play(name, s.soundVolume) },
                                label = { Text(label) },
                            )
                        }
                    }
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text("Volume", color = Color(0xFFBBBBC4), modifier = Modifier.width(64.dp))
                        Slider(
                            value = s.soundVolume.toFloat(),
                            onValueChange = { vm.updateSettings(s.copy(soundVolume = it.toInt())) },
                            onValueChangeFinished = { vm.feedback.play(s.sound, vm.settings.soundVolume) },
                            valueRange = 0f..100f,
                            modifier = Modifier.weight(1f),
                        )
                    }
                }
                SettingSwitch("Garder l'écran allumé", s.keepScreenOn) { vm.updateSettings(s.copy(keepScreenOn = it)) }
                Spacer(Modifier.height(12.dp))
                Text("Orientation", color = Color(0xFFBBBBC4))
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.padding(top = 6.dp)) {
                    listOf("Auto", "Portrait", "Paysage").forEachIndexed { i, label ->
                        FilterChip(selected = s.orientation == i, onClick = { vm.updateSettings(s.copy(orientation = i)) }, label = { Text(label) })
                    }
                }
                Spacer(Modifier.height(14.dp))
                Text(
                    "Les couleurs du boîtier se choisissent sur le PC (Réglages > Thème).",
                    color = Color(0xFF8D8D99), fontSize = 12.sp,
                )
                Spacer(Modifier.height(10.dp))
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text("Version ${Updater.currentVersion(LocalContext.current)} (build ${Updater.currentBuild(LocalContext.current)})", color = Color(0xFF8D8D99), fontSize = 12.sp, modifier = Modifier.weight(1f))
                    TextButton(onClick = { vm.checkForUpdate(manual = true) }) { Text("Vérifier les mises à jour") }
                }
                TextButton(onClick = { vm.showSettings = false; vm.showUsbGuide = true }) { Text("Connexion par câble USB") }
                TextButton(onClick = { vm.showSettings = false; vm.forget() }) { Text("Changer de PC") }
            }
        },
        confirmButton = { TextButton(onClick = { vm.showSettings = false }) { Text("Fermer") } },
    )
}

@Composable
private fun SettingSwitch(label: String, checked: Boolean, onChange: (Boolean) -> Unit) {
    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
        Text(label, modifier = Modifier.weight(1f))
        Switch(checked = checked, onCheckedChange = onChange)
    }
}

/** Bandeau « Mise à jour disponible » : un appui lance le téléchargement puis l'installation. */
@Composable
private fun BoxScope.UpdateBanner(vm: DeckViewModel) {
    val info = vm.update ?: return
    val progress = vm.updateProgress
    Row(
        Modifier
            .align(Alignment.BottomCenter)
            .navigationBarsPadding()
            .padding(16.dp)
            .clip(RoundedCornerShape(24.dp))
            .background(LocalTheme.current.accent)
            .padding(start = 18.dp, end = 6.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(
            if (progress != null) "Téléchargement… $progress %" else "Mise à jour disponible (version ${info.version ?: info.build})",
            color = LocalTheme.current.onAccent,
        )
        if (progress == null) {
            TextButton(onClick = { vm.installUpdate() }) { Text("Installer", color = LocalTheme.current.onAccent, fontWeight = FontWeight.Bold) }
            TextButton(onClick = { vm.dismissUpdate() }) { Text("Plus tard", color = LocalTheme.current.onAccent.copy(alpha = 0.8f)) }
        } else {
            Spacer(Modifier.width(12.dp))
        }
    }
}
