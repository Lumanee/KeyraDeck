package com.phonedeck.app

import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.PickVisualMediaRequest
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.MoreVert
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Dialog

/** Écran principal : la grille de boutons de la page d'accueil, et les dossiers par-dessus. */
@Composable
fun DeckScreen(vm: DeckViewModel) {
    var editing by remember { mutableStateOf<DeckButton?>(null) }
    var pickFor by remember { mutableStateOf<String?>(null) }
    val picker = rememberLauncherForActivityResult(ActivityResultContracts.PickVisualMedia()) { uri ->
        val id = pickFor
        if (uri != null && id != null) vm.uploadIcon(id, uri)
        pickFor = null
    }

    Column(Modifier.fillMaxSize()) {
        TopBar(vm)
        val layout = vm.layout
        val home = layout?.homePage
        if (home == null) {
            Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                    if (vm.status == Status.Connecting) CircularProgressIndicator()
                    Spacer(Modifier.height(16.dp))
                    Text(vm.statusText.ifEmpty { "En attente du PC…" }, color = Color(0xFFBBBBC4), textAlign = TextAlign.Center)
                    if (vm.status == Status.Disconnected) {
                        TextButton(onClick = { vm.retryNow() }) { Text("Réessayer") }
                    }
                }
            }
        } else {
            ButtonGrid(home.buttons, onPress = vm::press, onLongPress = { editing = it })
        }
    }

    val layout = vm.layout
    if (layout != null && vm.folderStack.isNotEmpty()) {
        val page = layout.pages[vm.folderStack.last()]
        if (page != null) {
            BackHandler { vm.closeFolder() }
            FolderDialog(
                page = page,
                canGoBack = vm.folderStack.size > 1,
                onBack = vm::closeFolder,
                onDismiss = vm::closeAllFolders,
                onPress = vm::press,
                onLongPress = { editing = it },
            )
        }
    }

    editing?.let { button ->
        AlertDialog(
            onDismissRequest = { editing = null },
            title = { Text(button.label) },
            text = { Text("Changer l'image de ce bouton ?") },
            confirmButton = {
                TextButton(onClick = {
                    pickFor = button.id
                    editing = null
                    picker.launch(PickVisualMediaRequest(ActivityResultContracts.PickVisualMedia.ImageOnly))
                }) { Text("Choisir une image") }
            },
            dismissButton = {
                TextButton(onClick = {
                    vm.resetIcon(button.id)
                    editing = null
                }) { Text("Icône automatique") }
            },
        )
    }
}

@Composable
private fun TopBar(vm: DeckViewModel) {
    var menu by remember { mutableStateOf(false) }
    Row(
        Modifier
            .fillMaxWidth()
            .padding(start = 20.dp, end = 4.dp, top = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        val dot = when (vm.status) {
            Status.Connected -> Color(0xFF4CD964)
            Status.Connecting -> Color(0xFFFFC107)
            Status.Disconnected -> Color(0xFFFF5252)
        }
        Box(Modifier.size(10.dp).clip(CircleShape).background(dot))
        Spacer(Modifier.width(10.dp))
        Text(
            when (vm.status) {
                Status.Connected -> vm.layout?.pcName ?: "Connecté"
                Status.Connecting -> "Connexion…"
                Status.Disconnected -> "Déconnecté"
            },
            color = Color(0xFFBBBBC4),
            fontSize = 14.sp,
            modifier = Modifier.weight(1f),
        )
        Box {
            IconButton(onClick = { menu = true }) {
                Icon(Icons.Filled.MoreVert, contentDescription = "Menu", tint = Color(0xFFBBBBC4))
            }
            DropdownMenu(expanded = menu, onDismissRequest = { menu = false }) {
                DropdownMenuItem(text = { Text("Reconnecter") }, onClick = { menu = false; vm.retryNow() })
                DropdownMenuItem(text = { Text("Changer de PC") }, onClick = { menu = false; vm.forget() })
            }
        }
    }
}

@Composable
private fun ButtonGrid(
    buttons: List<DeckButton>,
    onPress: (DeckButton) -> Unit,
    onLongPress: (DeckButton) -> Unit,
) {
    LazyVerticalGrid(
        columns = GridCells.Adaptive(minSize = 104.dp),
        contentPadding = PaddingValues(16.dp),
        horizontalArrangement = Arrangement.spacedBy(12.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
        modifier = Modifier.fillMaxSize(),
    ) {
        items(buttons, key = { it.id }) { b ->
            DeckTile(b, onPress, onLongPress)
        }
    }
}

/** Un dossier s'ouvre en fenêtre par-dessus, comme sur l'écran d'accueil d'Android. */
@Composable
private fun FolderDialog(
    page: DeckPage,
    canGoBack: Boolean,
    onBack: () -> Unit,
    onDismiss: () -> Unit,
    onPress: (DeckButton) -> Unit,
    onLongPress: (DeckButton) -> Unit,
) {
    Dialog(onDismissRequest = onDismiss) {
        Column(
            Modifier
                .clip(RoundedCornerShape(28.dp))
                .background(Color(0xF0202026))
                .padding(20.dp),
        ) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                if (canGoBack) {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Retour", tint = Color.White)
                    }
                }
                Text(page.name, color = Color.White, fontSize = 20.sp, fontWeight = FontWeight.SemiBold)
            }
            Spacer(Modifier.height(16.dp))
            val columns = if (page.buttons.size <= 4) 2 else 3
            page.buttons.chunked(columns).forEach { row ->
                Row(horizontalArrangement = Arrangement.spacedBy(12.dp), modifier = Modifier.padding(bottom = 12.dp)) {
                    row.forEach { b ->
                        Box(Modifier.width(96.dp)) { DeckTile(b, onPress, onLongPress) }
                    }
                }
            }
        }
    }
}

@OptIn(ExperimentalFoundationApi::class)
@Composable
fun DeckTile(button: DeckButton, onPress: (DeckButton) -> Unit, onLongPress: (DeckButton) -> Unit) {
    val haptic = LocalHapticFeedback.current
    Column(
        Modifier
            .fillMaxWidth()
            .aspectRatio(1f)
            .clip(RoundedCornerShape(20.dp))
            .background(button.color ?: Tile)
            .combinedClickable(
                onClick = {
                    haptic.performHapticFeedback(HapticFeedbackType.TextHandleMove)
                    onPress(button)
                },
                onLongClick = {
                    haptic.performHapticFeedback(HapticFeedbackType.LongPress)
                    onLongPress(button)
                },
            )
            .padding(10.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.Center,
    ) {
        val icon = button.icon
        if (icon != null) {
            Image(
                bitmap = icon,
                contentDescription = null,
                contentScale = ContentScale.Fit,
                modifier = Modifier.weight(1f).fillMaxWidth(),
            )
        } else {
            Spacer(Modifier.weight(1f))
        }
        Spacer(Modifier.height(4.dp))
        Text(
            button.label,
            color = Color.White,
            fontSize = 13.sp,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
    }
}
