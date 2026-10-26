package com.phonedeck.app

import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.PickVisualMediaRequest
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
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
import androidx.compose.ui.draw.scale
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.min
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Dialog

private val Desk = Brush.radialGradient(listOf(Color(0xFF2A2A33), Color(0xFF0B0B0E)))
private val BodyBrush = Brush.verticalGradient(listOf(Color(0xFF34343C), Color(0xFF17171B)))
private val BodyEdge = Brush.verticalGradient(listOf(Color(0x55FFFFFF), Color(0x08FFFFFF)))
private val KeyCap = Color(0xFF060608)
private val KeyScreen = Color(0xFF1B1B21)
private val Gloss = Brush.verticalGradient(listOf(Color(0x1FFFFFFF), Color(0x00FFFFFF)))

/** Écran principal : la page d'accueil dessinée comme un boîtier Stream Deck, et les dossiers par-dessus. */
@Composable
fun DeckScreen(vm: DeckViewModel) {
    var editing by remember { mutableStateOf<DeckButton?>(null) }
    var pickFor by remember { mutableStateOf<String?>(null) }
    val picker = rememberLauncherForActivityResult(ActivityResultContracts.PickVisualMedia()) { uri ->
        val id = pickFor
        if (uri != null && id != null) vm.uploadIcon(id, uri)
        pickFor = null
    }

    Box(Modifier.fillMaxSize().background(Desk), contentAlignment = Alignment.Center) {
        val home = vm.layout?.homePage
        Device(
            vm = vm,
            columns = home?.columns ?: 5,
            rows = home?.rows ?: 3,
            buttons = home?.buttons.orEmpty(),
            onLongPress = { editing = it },
        )
        if (home == null) Waiting(vm)
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
private fun Waiting(vm: DeckViewModel) {
    Column(
        Modifier
            .clip(RoundedCornerShape(20.dp))
            .background(Color(0xE0101014))
            .padding(24.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        if (vm.status == Status.Connecting) CircularProgressIndicator()
        Spacer(Modifier.height(12.dp))
        Text(vm.statusText.ifEmpty { "En attente du PC…" }, color = Color(0xFFBBBBC4), textAlign = TextAlign.Center)
        if (vm.status == Status.Disconnected) {
            TextButton(onClick = { vm.retryNow() }) { Text("Réessayer") }
        }
    }
}

/**
 * Le boîtier : un corps sombre aux bords arrondis, une petite barre avec le voyant de connexion,
 * et une grille de touches carrées dont la taille s'adapte à l'écran et à la grille choisie.
 */
@Composable
private fun Device(
    vm: DeckViewModel,
    columns: Int,
    rows: Int,
    buttons: List<DeckButton>,
    onLongPress: (DeckButton) -> Unit,
) {
    val cols = columns.coerceIn(1, 12)
    val rws = rows.coerceIn(1, 12)
    BoxWithConstraints(Modifier.fillMaxSize().padding(12.dp), contentAlignment = Alignment.Center) {
        val strip = 26.dp
        // Les marges et l'espace entre touches sont proportionnels à la taille d'une touche.
        val keyW = maxWidth / (cols + (cols - 1) * 0.14f + 2 * 0.32f)
        val keyH = (maxHeight - strip) / (rws + (rws - 1) * 0.14f + 2 * 0.32f)
        val key = min(keyW, keyH)
        val gap = key * 0.14f
        val bezel = key * 0.32f
        val shape = RoundedCornerShape(key * 0.42f)

        Column(
            Modifier
                .shadow(28.dp, shape)
                .clip(shape)
                .background(BodyBrush)
                .border(1.dp, BodyEdge, shape)
                .padding(start = bezel, end = bezel, bottom = bezel),
        ) {
            StatusStrip(vm, Modifier.height(strip).width(key * cols + gap * (cols - 1)))
            val bySlot = buttons.associateBy { it.slot }
            Column(verticalArrangement = Arrangement.spacedBy(gap)) {
                for (r in 0 until rws) {
                    Row(horizontalArrangement = Arrangement.spacedBy(gap)) {
                        for (c in 0 until cols) {
                            DeckKey(bySlot[r * cols + c], key, vm::press, onLongPress)
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun StatusStrip(vm: DeckViewModel, modifier: Modifier) {
    var menu by remember { mutableStateOf(false) }
    val led = when (vm.status) {
        Status.Connected -> Color(0xFF4CD964)
        Status.Connecting -> Color(0xFFFFC107)
        Status.Disconnected -> Color(0xFFFF5252)
    }
    Row(modifier, verticalAlignment = Alignment.CenterVertically) {
        Box(Modifier.size(7.dp).shadow(4.dp, CircleShape).clip(CircleShape).background(led))
        Spacer(Modifier.width(8.dp))
        Text(
            "PHONEDECK",
            color = Color(0xFF6E6E78),
            fontSize = 10.sp,
            fontWeight = FontWeight.Bold,
            letterSpacing = 3.sp,
        )
        Spacer(Modifier.width(10.dp))
        Text(
            if (vm.status == Status.Connected) vm.layout?.pcName.orEmpty() else vm.statusText,
            color = Color(0xFF55555E),
            fontSize = 10.sp,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.weight(1f),
        )
        Box {
            IconButton(onClick = { menu = true }, modifier = Modifier.size(26.dp)) {
                Icon(Icons.Filled.MoreVert, contentDescription = "Menu", tint = Color(0xFF6E6E78), modifier = Modifier.size(16.dp))
            }
            DropdownMenu(expanded = menu, onDismissRequest = { menu = false }) {
                DropdownMenuItem(text = { Text("Reconnecter") }, onClick = { menu = false; vm.retryNow() })
                DropdownMenuItem(text = { Text("Changer de PC") }, onClick = { menu = false; vm.forget() })
            }
        }
    }
}

/** Une touche : un capuchon noir avec un petit écran dedans, qui s'enfonce quand on appuie. */
@OptIn(ExperimentalFoundationApi::class)
@Composable
fun DeckKey(
    button: DeckButton?,
    size: Dp,
    onPress: (DeckButton) -> Unit,
    onLongPress: (DeckButton) -> Unit,
) {
    val haptic = LocalHapticFeedback.current
    val interaction = remember { MutableInteractionSource() }
    val pressed by interaction.collectIsPressedAsState()
    val scale by animateFloatAsState(if (pressed) 0.92f else 1f, label = "press")
    val capShape = RoundedCornerShape(size * 0.2f)
    val screenShape = RoundedCornerShape(size * 0.14f)

    var mod = Modifier
        .size(size)
        .scale(scale)
        .shadow(if (pressed) 1.dp else 5.dp, capShape)
        .clip(capShape)
        .background(KeyCap)
    if (button != null) {
        mod = mod.combinedClickable(
            interactionSource = interaction,
            indication = null,
            onClick = {
                haptic.performHapticFeedback(HapticFeedbackType.TextHandleMove)
                onPress(button)
            },
            onLongClick = {
                haptic.performHapticFeedback(HapticFeedbackType.LongPress)
                onLongPress(button)
            },
        )
    }

    Box(mod.padding(size * 0.05f)) {
        Column(
            Modifier
                .fillMaxSize()
                .clip(screenShape)
                .background(button?.color ?: KeyScreen)
                .background(Gloss)
                .background(if (pressed) Color(0x22FFFFFF) else Color.Transparent)
                .padding(size * 0.07f),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.Center,
        ) {
            if (button == null) return@Column
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
            if (size >= 56.dp) {
                Text(
                    button.label,
                    color = Color.White,
                    fontSize = (size.value * 0.12f).coerceIn(9f, 15f).sp,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
        }
    }
}

/** Un dossier s'ouvre par-dessus, comme sur l'écran d'accueil d'Android, avec les mêmes touches. */
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
        val shape = RoundedCornerShape(28.dp)
        Column(
            Modifier
                .shadow(24.dp, shape)
                .clip(shape)
                .background(BodyBrush)
                .border(1.dp, BodyEdge, shape)
                .padding(18.dp),
        ) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                if (canGoBack) {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Retour", tint = Color.White)
                    }
                }
                Text(page.name, color = Color.White, fontSize = 18.sp, fontWeight = FontWeight.SemiBold)
            }
            Spacer(Modifier.height(14.dp))
            val columns = page.columns ?: if (page.buttons.size <= 4) 2 else 3
            val slots = page.buttons.associateBy { it.slot }
            val count = (page.buttons.maxOfOrNull { it.slot } ?: -1) + 1
            val rows = page.rows ?: ((count + columns - 1) / columns).coerceAtLeast(1)
            Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                for (r in 0 until rows) {
                    Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                        for (c in 0 until columns) {
                            DeckKey(slots[r * columns + c], 84.dp, onPress, onLongPress)
                        }
                    }
                }
            }
        }
    }
}
