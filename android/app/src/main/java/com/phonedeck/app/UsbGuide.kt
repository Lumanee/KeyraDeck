package com.phonedeck.app

import android.content.Context
import android.content.Intent
import android.provider.Settings
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.delay

private fun globalFlag(context: Context, name: String): Boolean =
    try { Settings.Global.getInt(context.contentResolver, name, 0) == 1 } catch (_: Exception) { false }

private fun openSettings(context: Context, action: String) {
    try {
        context.startActivity(Intent(action))
    } catch (_: Exception) {
        context.startActivity(Intent(Settings.ACTION_SETTINGS))
    }
}

/**
 * Aide pour la connexion par câble USB : les réglages Android à activer une seule fois,
 * cochés au fur et à mesure (l'appli lit l'état des options pour les développeurs et du débogage USB).
 */
@Composable
fun UsbGuideDialog(vm: DeckViewModel) {
    val context = LocalContext.current
    var devOn by remember { mutableStateOf(false) }
    var adbOn by remember { mutableStateOf(false) }
    // Relu chaque seconde : l'utilisateur revient des Paramètres et voit l'étape cochée.
    LaunchedEffect(Unit) {
        while (true) {
            devOn = globalFlag(context, Settings.Global.DEVELOPMENT_SETTINGS_ENABLED)
            adbOn = globalFlag(context, Settings.Global.ADB_ENABLED)
            delay(1000)
        }
    }
    val viaUsb = vm.status == Status.Connected && vm.link == Link.Usb

    AlertDialog(
        onDismissRequest = { vm.showUsbGuide = false },
        title = { Text("Connexion par câble USB") },
        text = {
            Column(Modifier.verticalScroll(rememberScrollState())) {
                Text(
                    if (viaUsb) "✓ Connecté au PC par le câble USB."
                    else "Le câble sert de secours quand le Wi-Fi flanche (idéal en jeu). KeyraDeck passe tout seul du Wi-Fi au câble et inversement.",
                    color = if (viaUsb) Color(0xFF4CD964) else Color(0xFFBBBBC4),
                    fontSize = 13.sp,
                )
                Spacer(Modifier.height(12.dp))
                Step(1, devOn, "Activer les options pour les développeurs",
                    "Dans « À propos du téléphone », touche 7 fois « Numéro de build » (Samsung : Informations sur le logiciel › Numéro de version ; Xiaomi : Version de MIUI / HyperOS).") {
                    OutlinedButton(onClick = { openSettings(context, Settings.ACTION_DEVICE_INFO_SETTINGS) }) { Text("Ouvrir « À propos du téléphone »") }
                }
                Step(2, adbOn, "Activer le débogage USB",
                    "Dans « Options pour les développeurs », active « Débogage USB ».") {
                    if (devOn) OutlinedButton(onClick = { openSettings(context, Settings.ACTION_APPLICATION_DEVELOPMENT_SETTINGS) }) { Text("Ouvrir les options pour les développeurs") }
                }
                Step(3, viaUsb, "Brancher le téléphone au PC",
                    "Avec un câble de données, KeyraDeck ouvert sur le PC. Touche « Autoriser » dans la fenêtre « Autoriser le débogage USB ? » en cochant « Toujours autoriser ».") {}
                Spacer(Modifier.height(4.dp))
                Text(
                    "Rien ne se passe ? Change de câble ou de prise, choisis « Transfert de fichiers » dans la notification USB, puis regarde l'état dans KeyraDeck sur le PC (bouton « Connecter un téléphone »).",
                    color = Color(0xFF8D8D99), fontSize = 12.sp,
                )
            }
        },
        confirmButton = { TextButton(onClick = { vm.showUsbGuide = false }) { Text("Fermer") } },
    )
}

@Composable
private fun Step(n: Int, done: Boolean, title: String, detail: String, action: @Composable () -> Unit) {
    Row(Modifier.fillMaxWidth().padding(bottom = 12.dp)) {
        Text(
            if (done) "✓" else "$n",
            color = if (done) Color(0xFF4CD964) else Color.White,
            fontWeight = FontWeight.Bold,
            modifier = Modifier.width(22.dp),
        )
        Column(Modifier.weight(1f)) {
            Text(title, fontWeight = FontWeight.Bold, color = if (done) Color(0xFF8D8D99) else Color.White)
            if (!done) {
                Text(detail, color = Color(0xFFBBBBC4), fontSize = 12.sp)
                action()
            }
        }
    }
}
