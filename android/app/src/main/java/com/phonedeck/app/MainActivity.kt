package com.phonedeck.app

import android.os.Bundle
import android.view.WindowManager
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.activity.compose.BackHandler
import androidx.activity.compose.setContent
import androidx.activity.viewModels
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import androidx.compose.foundation.layout.Box
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
val Accent = Color(0xFF7C6CF2)

@Composable
fun App(vm: DeckViewModel) {
    val context = LocalContext.current
    LaunchedEffect(Unit) {
        vm.toasts.collect { Toast.makeText(context, it, Toast.LENGTH_SHORT).show() }
    }

    MaterialTheme(colorScheme = darkColorScheme(primary = Accent, background = Background, surface = Background)) {
        Box(Modifier.fillMaxSize()) {
        Surface(Modifier.fillMaxSize(), color = Background) {
            when {
                vm.serverUrl == null -> ConnectScreen(vm)
                vm.mixerOpen -> {
                    BackHandler { vm.closeMixer() }
                    MixerScreen(vm)
                }
                else -> DeckScreen(vm)
            }
        }
        UpdateBanner(vm)
        }
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
            .background(Accent)
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
