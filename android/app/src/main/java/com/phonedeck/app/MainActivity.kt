package com.phonedeck.app

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
import androidx.compose.material3.Switch
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
    }
}

val Background = Color(0xFF111114)
val Tile = Color(0xFF26262C)

val LocalTheme = compositionLocalOf { DeckTheme.Default }
val LocalSettings = compositionLocalOf { AppSettings() }

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
    CompositionLocalProvider(LocalTheme provides theme, LocalSettings provides settings) {
        MaterialTheme(colorScheme = darkColorScheme(primary = theme.accent, background = Background, surface = Background)) {
            Box(Modifier.fillMaxSize()) {
                Surface(Modifier.fillMaxSize(), color = Background) {
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
            Column {
                SettingSwitch("Vibration au toucher", s.haptics) { vm.updateSettings(s.copy(haptics = it)) }
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
                    Text("Version ${Updater.currentBuild(LocalContext.current)}", color = Color(0xFF8D8D99), fontSize = 12.sp, modifier = Modifier.weight(1f))
                    TextButton(onClick = { vm.checkForUpdate(manual = true) }) { Text("Vérifier les mises à jour") }
                }
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
            if (progress != null) "Téléchargement… $progress %" else "Mise à jour disponible (version ${info.build})",
            color = Color.White,
        )
        if (progress == null) {
            TextButton(onClick = { vm.installUpdate() }) { Text("Installer", color = Color.White, fontWeight = FontWeight.Bold) }
            TextButton(onClick = { vm.dismissUpdate() }) { Text("Plus tard", color = Color(0xCCFFFFFF)) }
        } else {
            Spacer(Modifier.width(12.dp))
        }
    }
}
