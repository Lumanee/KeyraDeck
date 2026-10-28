package com.phonedeck.app

import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.PickVisualMediaRequest
import androidx.activity.result.contract.ActivityResultContracts
import android.os.Build
import android.view.RoundedCorner
import androidx.compose.animation.core.Animatable
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalView
import androidx.core.view.ViewCompat
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.runtime.rememberCoroutineScope
import kotlinx.coroutines.launch
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.Image
import androidx.compose.ui.res.painterResource
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.pager.HorizontalPager
import androidx.compose.foundation.pager.rememberPagerState
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
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.draw.drawWithContent
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.draw.scale
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.lerp
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.min
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Dialog

// Logement de la touche, creusé dans le métal : plus sombre en haut, la lumière accroche le bord du bas.
private val KeyWell = Brush.verticalGradient(listOf(Color(0xFF000000), Color(0xFF0A0A0C), Color(0xFF1C1D21)))
// Verre de la touche : reflet en haut, plus sombre en bas, liseré clair comme une tranche de verre.
private val GlassShade = Brush.verticalGradient(0f to Color(0x26FFFFFF), 0.5f to Color(0x08FFFFFF), 1f to Color(0x47000000))
private val GlassEdge = Brush.verticalGradient(listOf(Color(0x73FFFFFF), Color(0x14FFFFFF), Color(0x2EFFFFFF)))
private val GlassGlare = listOf(Color(0x33FFFFFF), Color(0x08FFFFFF))
private val BodyEdge = Brush.verticalGradient(listOf(Color(0x80FFFFFF), Color(0x10FFFFFF), Color(0x40FFFFFF)))
// Reflet général du métal : lumière en haut, ombre en bas.
private val MetalShade = Brush.verticalGradient(0f to Color(0x2EFFFFFF), 0.18f to Color(0x00FFFFFF), 0.7f to Color(0x00000000), 1f to Color(0x59000000))

@Composable
private fun deskBrush(): Brush {
    val t = LocalTheme.current
    // Thème OLED : noir pur, sans halo.
    if (t.pureBlack) return Brush.verticalGradient(listOf(Color.Black, Color.Black))
    return Brush.radialGradient(listOf(lerp(t.desk, Color.White, 0.12f), t.desk))
}

/** Boîtier en métal brossé : reflets en biais (aluminium anodisé) + fines stries horizontales. */
@Composable
private fun Modifier.metalBody(): Modifier {
    val b = LocalTheme.current.body
    val sheen = Brush.linearGradient(
        0f to lerp(b, Color.White, 0.24f),
        0.3f to lerp(b, Color.Black, 0.22f),
        0.52f to lerp(b, Color.White, 0.14f),
        0.75f to lerp(b, Color.Black, 0.32f),
        1f to lerp(b, Color.White, 0.18f),
        start = Offset.Zero, end = Offset.Infinite,
    )
    return this
        .background(sheen)
        .background(MetalShade)
        .drawBehind {
            var y = 0f
            var i = 0
            while (y < size.height) {
                drawLine(if (i % 2 == 0) Color(0x0AFFFFFF) else Color(0x0D000000), Offset(0f, y), Offset(size.width, y), 1f)
                y += 2f; i++
            }
        }
}

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

    BoxWithConstraints(Modifier.fillMaxSize().background(deskBrush()), contentAlignment = Alignment.Center) {
        val view = LocalView.current
        val density = LocalDensity.current.density
        LaunchedEffect(maxWidth, maxHeight) {
            // Encoche / caméra et rayon des coins : le configurateur PC dessine le vrai contour du téléphone.
            var cut = 0
            var radius = 0
            ViewCompat.getRootWindowInsets(view)?.displayCutout?.let { cut = (it.safeInsetTop / density).toInt() }
            if (Build.VERSION.SDK_INT >= 31) {
                view.rootWindowInsets?.getRoundedCorner(RoundedCorner.POSITION_TOP_LEFT)?.let { radius = (it.radius / density).toInt() }
            }
            vm.reportScreen(maxWidth.value.toInt(), maxHeight.value.toInt(), cut, radius)
        }
        val roots = vm.layout?.rootPages.orEmpty()
        if (roots.isEmpty()) {
            Device(vm, 5, 3, emptyList(), emptyList(), 0, 1) { editing = it }
            Waiting(vm)
        } else {
            val pager = rememberPagerState(pageCount = { roots.size })
            val focus = vm.focus
            LaunchedEffect(focus) {
                val index = roots.indexOfFirst { it.id == focus?.page }
                if (index >= 0 && index != pager.currentPage) pager.animateScrollToPage(index)
            }
            HorizontalPager(state = pager, modifier = Modifier.fillMaxSize()) { index ->
                val page = roots[index]
                Device(
                    vm = vm,
                    columns = page.columns ?: 5,
                    rows = page.rows ?: 3,
                    buttons = page.buttons,
                    widgets = page.widgets,
                    pageIndex = index,
                    pageCount = roots.size,
                    onLongPress = { editing = it },
                )
            }
        }
    }

    val layout = vm.layout
    if (layout != null && vm.folderStack.isNotEmpty()) {
        val page = layout.pages[vm.folderStack.last()]
        if (page != null) {
            BackHandler { vm.closeFolder() }
            FolderDialog(
                vm = vm,
                page = page,
                canGoBack = vm.folderStack.size > 1,
                active = vm.active,
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
    widgets: List<DeckWidget>,
    pageIndex: Int,
    pageCount: Int,
    onLongPress: (DeckButton) -> Unit,
) {
    val cols = columns.coerceIn(1, 12)
    val rws = rows.coerceIn(1, 12)
    // Le boîtier remplit tout l'écran ; les touches sont carrées et centrées dedans.
    // Ces mesures sont les mêmes dans le configurateur du PC (config.html : deviceMetrics), pour que l'aperçu soit fidèle.
    // Marges et épaisseur du boîtier proportionnelles à l'écran (mêmes formules que le configurateur PC).
    BoxWithConstraints(Modifier.fillMaxSize()) {
        val small = min(maxWidth, maxHeight)
        val margin = small * 0.025f
        val pad = small * 0.034f
        val strip = 26.dp
        val gapRatio = 0.14f
        val keyW = (maxWidth - margin * 2 - pad * 2) / (cols + (cols - 1) * gapRatio)
        val keyH = (maxHeight - margin * 2 - pad * 2 - strip) / (rws + (rws - 1) * gapRatio)
        val key = min(min(keyW, keyH), 140.dp)
        val gap = key * gapRatio
        val shape = RoundedCornerShape(small * 0.075f)

        Column(
            Modifier
                .fillMaxSize()
                .padding(margin)
                .shadow(24.dp, shape)
                .clip(shape)
                .metalBody()
                .border(1.dp, BodyEdge, shape)
                .padding(pad),
        ) {
            StatusStrip(vm, pageIndex, 1, Modifier.fillMaxWidth().height(strip))
            Box(Modifier.weight(1f).fillMaxWidth(), contentAlignment = Alignment.Center) {
                KeyGrid(vm, cols, rws, buttons, widgets, key, gap, vm.active, vm::press, onLongPress)
            }
        }
    }
}

/** Les touches et les widgets d'une page, placés case par case (un widget couvre plusieurs cases). */
@Composable
fun KeyGrid(
    vm: DeckViewModel,
    cols: Int,
    rows: Int,
    buttons: List<DeckButton>,
    widgets: List<DeckWidget>,
    key: Dp,
    gap: Dp,
    active: Set<String>,
    onPress: (DeckButton) -> Unit,
    onLongPress: (DeckButton) -> Unit,
) {
    val covered = widgets.flatMap { w -> (0 until w.h).flatMap { dy -> (0 until w.w).map { dx -> w.slot + dy * cols + dx } } }.toSet()
    val bySlot = buttons.associateBy { it.slot }
    Box(Modifier.size(key * cols + gap * (cols - 1), key * rows + gap * (rows - 1))) {
        for (slot in 0 until cols * rows) {
            if (slot in covered) continue
            val b = bySlot[slot]
            Box(Modifier.offset((key + gap) * (slot % cols), (key + gap) * (slot / cols))) {
                DeckKey(b, key, b != null && b.id in active, onPress, onLongPress)
            }
        }
        for (w in widgets) {
            Box(Modifier.offset((key + gap) * (w.slot % cols), (key + gap) * (w.slot / cols))) {
                DeckWidgetView(w, key * w.w + gap * (w.w - 1), key * w.h + gap * (w.h - 1), key, vm)
            }
        }
    }
}

@Composable
private fun StatusStrip(vm: DeckViewModel, pageIndex: Int, pageCount: Int, modifier: Modifier) {
    var menu by remember { mutableStateOf(false) }
    val led = when (vm.status) {
        Status.Connected -> Color(0xFF4CD964)
        Status.Connecting -> Color(0xFFFFC107)
        Status.Disconnected -> Color(0xFFFF5252)
    }
    Row(modifier, verticalAlignment = Alignment.CenterVertically) {
        Box(Modifier.size(7.dp).shadow(4.dp, CircleShape).clip(CircleShape).background(led))
        Spacer(Modifier.width(8.dp))
        // Connecté par le câble : petit repère « USB » à côté de la LED (touche = aide sur le câble).
        if (vm.status == Status.Connected && vm.link == Link.Usb) {
            Text(
                "USB",
                color = led,
                fontSize = 8.sp,
                fontWeight = FontWeight.Bold,
                letterSpacing = 1.sp,
                modifier = Modifier.clip(RoundedCornerShape(3.dp)).background(Color(0x224CD964))
                    .clickable { vm.showUsbGuide = true }.padding(horizontal = 4.dp, vertical = 1.dp),
            )
            Spacer(Modifier.width(8.dp))
        }
        Image(painterResource(R.drawable.ic_logo), contentDescription = null, modifier = Modifier.height(14.dp))
        Spacer(Modifier.width(6.dp))
        Text(
            "KEYRADECK",
            color = Color(0xFF6E6E78),
            fontSize = 10.sp,
            fontWeight = FontWeight.Bold,
            letterSpacing = 3.sp,
        )
        // Seulement la LED (verte = connecté, orange = connexion, rouge = déconnecté) et « USB » par câble : pas de nom de PC.
        Spacer(Modifier.weight(1f))
        NotificationBadge(vm)
        Spacer(Modifier.width(6.dp))
        // Accès à la page souris / clavier, depuis n'importe quelle page.
        Box(
            Modifier.clip(RoundedCornerShape(50)).background(Color(0x22FFFFFF)).clickable { vm.showPad = true }.padding(horizontal = 8.dp, vertical = 3.dp),
        ) { Text("🖱 Souris", color = Color(0xFFDDDDE6), fontSize = 10.sp, fontWeight = FontWeight.Bold) }
        Spacer(Modifier.width(6.dp))
        if (pageCount > 1) {
            // Un petit point par page ; le point clair est la page affichée.
            Row(horizontalArrangement = Arrangement.spacedBy(5.dp), verticalAlignment = Alignment.CenterVertically) {
                repeat(pageCount) { i ->
                    Box(Modifier.size(if (i == pageIndex) 7.dp else 5.dp).clip(CircleShape)
                        .background(if (i == pageIndex) Color(0xFFDDDDE6) else Color(0xFF55555E)))
                }
            }
            Spacer(Modifier.width(6.dp))
        }
        Box {
            IconButton(onClick = { menu = true }, modifier = Modifier.size(26.dp)) {
                Icon(Icons.Filled.MoreVert, contentDescription = "Menu", tint = Color(0xFF6E6E78), modifier = Modifier.size(16.dp))
            }
            DropdownMenu(expanded = menu, onDismissRequest = { menu = false }) {
                DropdownMenuItem(text = { Text("Réglages") }, onClick = { menu = false; vm.showSettings = true })
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
    active: Boolean,
    onPress: (DeckButton) -> Unit,
    onLongPress: (DeckButton) -> Unit,
) {
    val touch = LocalTouch.current
    val interaction = remember { MutableInteractionSource() }
    val pressed by interaction.collectIsPressedAsState()
    val scale by animateFloatAsState(if (pressed) 0.92f else 1f, label = "press")
    // Éclair de couleur à chaque appui : on voit tout de suite que la touche a réagi.
    val flash = remember { Animatable(0f) }
    val scope = rememberCoroutineScope()
    val flashColor = LocalTheme.current.accent
    val capShape = RoundedCornerShape(size * 0.2f)
    val screenShape = RoundedCornerShape(size * 0.14f)

    var mod = Modifier
        .size(size)
        .scale(scale)
        .shadow(if (pressed) 1.dp else 5.dp, capShape)
        .clip(capShape)
        .background(KeyWell)
    if (button != null) {
        mod = mod.combinedClickable(
            interactionSource = interaction,
            indication = null,
            onClick = {
                touch.touch(button.vibrate, button.sound, false)
                scope.launch { flash.snapTo(1f); flash.animateTo(0f, tween(500)) }
                onPress(button)
            },
            onLongClick = {
                touch.touch(button.vibrate, "off", true)
                onLongPress(button)
            },
        )
    }

    Box(mod.padding(size * 0.05f)) {
        Column(
            Modifier
                .fillMaxSize()
                .clip(screenShape)
                .background(if (active) LocalTheme.current.active else (button?.color ?: LocalTheme.current.key))
                .background(GlassShade)
                .background(if (pressed) Color(0x22FFFFFF) else Color.Transparent)
                .background(flashColor.copy(alpha = 0.6f * flash.value))
                // Reflet du verre par-dessus l'icône : la touche a l'air vitrée.
                .drawWithContent {
                    drawContent()
                    val h = size.height * 0.46f
                    drawRoundRect(
                        Brush.verticalGradient(GlassGlare, startY = 0f, endY = h),
                        topLeft = Offset(size.width * 0.05f, size.height * 0.03f),
                        size = Size(size.width * 0.9f, h),
                        cornerRadius = CornerRadius(size.width * 0.12f),
                    )
                }
                .border(1.dp, GlassEdge, screenShape)
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
            if (size >= 56.dp && !button.hideLabel) {
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
    vm: DeckViewModel,
    page: DeckPage,
    canGoBack: Boolean,
    active: Set<String>,
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
                .metalBody()
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
            val count = maxOf(
                (page.buttons.maxOfOrNull { it.slot } ?: -1) + 1,
                page.widgets.maxOfOrNull { it.slot + (it.h - 1) * columns + 1 } ?: 0,
            )
            val rows = page.rows ?: ((count + columns - 1) / columns).coerceAtLeast(1)
            KeyGrid(vm, columns, rows, page.buttons, page.widgets, 84.dp, 10.dp, active, onPress, onLongPress)
        }
    }
}
