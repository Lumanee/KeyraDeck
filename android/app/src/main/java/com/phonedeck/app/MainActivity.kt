package com.phonedeck.app

import android.os.Bundle
import android.view.WindowManager
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.activity.compose.BackHandler
import androidx.activity.compose.setContent
import androidx.activity.viewModels
import androidx.compose.foundation.layout.fillMaxSize
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
    }
}
