package com.phonedeck.app

import android.app.Application
import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.net.Uri
import android.os.Build
import android.os.Handler
import android.os.Looper
import android.util.Base64
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.graphics.ImageBitmap
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import org.json.JSONObject
import java.io.ByteArrayOutputStream
import java.io.File
import java.net.URLEncoder
import kotlin.math.abs
import kotlin.math.max
import kotlin.math.min

enum class Status { Disconnected, Connecting, Connected }

/** Chemin de la connexion : le Wi-Fi, ou le câble USB (adb reverse, le PC est alors joignable sur 127.0.0.1). */
enum class Link { Wifi, Usb }

/** Réglages propres au téléphone (le thème de couleurs, lui, vient du PC). */
data class AppSettings(
    val haptics: Boolean = true,
    /** Force de la vibration : 1 = léger, 2 = moyen, 3 = fort. */
    val hapticStrength: Int = 2,
    /** Son au toucher : "off" ou un nom de [TouchSounds]. */
    val sound: String = "off",
    /** Volume du son au toucher, de 0 à 100. */
    val soundVolume: Int = 60,
    val keepScreenOn: Boolean = true,
    /** 0 = automatique, 1 = portrait, 2 = paysage. */
    val orientation: Int = 0,
)

/** Demande du PC : afficher cette page (appli au premier plan). Le compteur permet de répéter la même demande. */
data class FocusRequest(val page: String, val count: Int)

private const val USB_BACKOFF_MS = 20_000L

class DeckViewModel(app: Application) : AndroidViewModel(app), DeckClient.Listener {

    private val prefs = app.getSharedPreferences("deck", Context.MODE_PRIVATE)
    private val main = Handler(Looper.getMainLooper())
    private val client = DeckClient(this)

    /** Adresse du PC (ws://IP:port/ws?token=CODE), ou null tant qu'on n'est pas appairé. */
    var serverUrl by mutableStateOf(prefs.getString("url", null))
        private set
    var status by mutableStateOf(Status.Disconnected)
        private set
    var statusText by mutableStateOf("")
        private set
    /** Par où passe la connexion actuelle (affiché à côté de la LED du boîtier). */
    var link by mutableStateOf(Link.Wifi)
        private set
    /** Un PC branché en USB propose de remplacer le PC actuel : on demande avant. */
    var pendingUsbPair by mutableStateOf<String?>(null)
    var showUsbGuide by mutableStateOf(false)
    var layout by mutableStateOf<DeckLayout?>(null)
        private set

    /** Données des widgets (volume général, charge du PC), envoyées par le PC. */
    var masterVolume by mutableStateOf<Int?>(null)
        private set
    var masterMuted by mutableStateOf(false)
        private set
    var cpu by mutableStateOf<Int?>(null)
        private set
    var ram by mutableStateOf<Int?>(null)
        private set
    var gpu by mutableStateOf<Int?>(null)
        private set
    var disk by mutableStateOf<Int?>(null)
        private set
    var appPerf by mutableStateOf<AppPerf?>(null)
        private set
    private var volumeDragging = false
    var weather by mutableStateOf<Weather?>(null)
        private set
    var discord by mutableStateOf<DiscordVoice?>(null)
        private set
    /** Bouton Discord dont le menu (micro, sourdine, partage, raccrocher) est ouvert. */
    var discordPanel by mutableStateOf<String?>(null)
    var nowPlaying by mutableStateOf<NowPlayingInfo?>(null)
        private set
    var nowPlayingArt by mutableStateOf<ImageBitmap?>(null)
        private set

    /** Fonds des zones écran (images, vidéos) déjà reçus du PC, par nom de fichier. Gardés sur le téléphone. */
    val screenBgs = mutableStateMapOf<String, File>()
    private val screenBgAsked = mutableSetOf<String>()
    private val screenBgDir = File(app.filesDir, "ecrans").apply { mkdirs() }

    /** Demande au PC les fonds qui manquent et efface ceux qui ne servent plus. */
    private fun syncScreenBgs(parsed: DeckLayout) {
        val wanted = parsed.pages.values.mapNotNull { it.screen?.bg }.toSet()
        screenBgDir.listFiles()?.forEach { if (it.name !in wanted && !it.name.endsWith(".part")) it.delete() }
        screenBgs.keys.retainAll(wanted)
        for (name in wanted) {
            val file = File(screenBgDir, name)
            if (file.exists()) screenBgs[name] = file
            else if (screenBgAsked.add(name)) send(JSONObject().put("type", "getScreenBg").put("file", name))
        }
    }

    override fun onFile(name: String, data: ByteArray) {
        if (!Regex("^[a-f0-9]{16}\\.(png|jpg|gif|webp|mp4|webm)$").matches(name)) return
        // Écrit à côté puis renommé : une vidéo à moitié écrite n'est jamais lue.
        val file = File(screenBgDir, name)
        val part = File(screenBgDir, "$name.part")
        try {
            part.writeBytes(data)
            part.renameTo(file)
        } catch (_: Exception) {
            part.delete()
            return
        }
        post { screenBgs[name] = file }
    }

    /**
     * Rotation choisie dans le configurateur du PC ("auto", "portrait", "landscape"). Appliquée seulement quand elle
     * change sur le PC : un réglage fait ensuite sur le téléphone reste en place.
     */
    private fun applyPcOrientation(value: String) {
        val last = prefs.getString("pcOrientation", null)
        if (value == last) return
        prefs.edit().putString("pcOrientation", value).apply()
        // Première fois (mise à jour de l'appli) avec le réglage par défaut : on garde celui du téléphone.
        if (last == null && value == "auto") return
        updateSettings(settings.copy(orientation = when (value) { "portrait" -> 1; "landscape" -> 2; else -> 0 }))
    }

    /** Notifications reçues du PC : bandeau qui s'affiche quelques secondes, puis compteur dans la barre du boîtier. */
    val notifications = mutableStateListOf<Notif>()
    var banner by mutableStateOf<Notif?>(null)
    var showNotifs by mutableStateOf(false)
    fun clearNotifs() { notifications.clear(); banner = null; showNotifs = false }

    var settings by mutableStateOf(
        AppSettings(
            haptics = prefs.getBoolean("haptics", true),
            hapticStrength = prefs.getInt("hapticStrength", 2),
            sound = prefs.getString("sound", "off") ?: "off",
            soundVolume = prefs.getInt("soundVolume", 60),
            keepScreenOn = prefs.getBoolean("keepOn", true),
            orientation = prefs.getInt("orientation", 0),
        )
    )
        private set
    var showSettings by mutableStateOf(false)

    /** Vibration et son au toucher. */
    val feedback = TouchFeedback(app)

    /**
     * Réglages de vibration et de son choisis dans le configurateur du PC. On ne les applique que s'ils ont changé
     * depuis la dernière fois : un réglage fait ensuite sur le téléphone reste en place.
     */
    private fun applyPcFeedback(json: JSONObject?) {
        if (json == null) return
        val key = json.toString()
        val last = prefs.getString("pcFeedback", null)
        if (key == last) return
        prefs.edit().putString("pcFeedback", key).apply()
        // Première fois (mise à jour de l'appli) : on garde les réglages actuels du téléphone.
        if (last == null) return
        val s = settings
        updateSettings(
            s.copy(
                haptics = json.optBoolean("vibrate", s.haptics),
                hapticStrength = json.optInt("strength", s.hapticStrength).coerceIn(1, 3),
                sound = json.optString("sound", s.sound).ifEmpty { "off" },
                soundVolume = json.optInt("volume", s.soundVolume).coerceIn(0, 100),
            )
        )
    }

    var focus by mutableStateOf<FocusRequest?>(null)
        private set

    fun updateSettings(new: AppSettings) {
        settings = new
        prefs.edit()
            .putBoolean("haptics", new.haptics)
            .putInt("hapticStrength", new.hapticStrength)
            .putString("sound", new.sound)
            .putInt("soundVolume", new.soundVolume)
            .putBoolean("keepOn", new.keepScreenOn)
            .putInt("orientation", new.orientation)
            .apply()
    }

    /** Mise à jour de l'appli disponible sur GitHub (null = rien à faire). */
    var update by mutableStateOf<UpdateInfo?>(null)
        private set
    var updateProgress by mutableStateOf<Int?>(null)
        private set

    /** Boutons « activés » (affichés en rouge : micro coupé, etc.). */
    var active by mutableStateOf<Set<String>>(emptySet())
        private set

    /** Dossiers ouverts (le dernier est affiché par-dessus la page). */
    val folderStack = mutableStateListOf<String>()

    var mixerOpen by mutableStateOf(false)
        private set
    var mixer by mutableStateOf<List<MixerChannel>>(emptyList())
        private set

    private val _toasts = MutableSharedFlow<String>(extraBufferCapacity = 8)
    val toasts: SharedFlow<String> = _toasts

    private val dragging = mutableSetOf<String>()
    private val lastSentVolume = mutableMapOf<String, Float>()
    private var mixerJob: Job? = null
    private val reconnect = Runnable { serverUrl?.let { if (status == Status.Disconnected) open(it) } }

    /** Chemin de la tentative en cours, et si elle a abouti. */
    private var tryingLink = Link.Wifi
    private var opened = false
    /** Dernier échec par le câble : on laisse le Wi-Fi tranquille un moment avant de retenter l'USB. */
    private var usbFailedAt = 0L
    private var openSeq = 0

    init {
        serverUrl?.let { open(it) }
        checkForUpdate()
        watchUsb()
    }

    // ---------- Mise à jour de l'appli ----------

    fun checkForUpdate(manual: Boolean = false) {
        viewModelScope.launch(Dispatchers.IO) {
            val found = Updater.check(getApplication())
            post { update = found }
            if (manual) toast(if (found == null) "KeyraDeck est à jour ✓" else "Mise à jour disponible")
        }
    }

    fun dismissUpdate() {
        update = null
    }

    fun installUpdate() {
        val info = update ?: return
        val app = getApplication<Application>()
        if (!Updater.canInstall(app)) {
            toast("Autorise KeyraDeck à installer des applis, puis reviens et réessaie")
            Updater.openInstallPermission(app)
            return
        }
        if (updateProgress != null) return
        updateProgress = 0
        viewModelScope.launch(Dispatchers.IO) {
            try {
                val apk = Updater.download(app, info) { p -> post { updateProgress = p } }
                post { updateProgress = null }
                Updater.install(app, apk)
            } catch (e: Exception) {
                post { updateProgress = null }
                toast("Mise à jour impossible : ${e.message}")
            }
        }
    }

    // ---------- Connexion ----------

    /** Appelé après le scan du QR code ou la saisie manuelle. */
    fun pair(url: String) {
        val clean = url.trim()
        if (!clean.startsWith("ws://") && !clean.startsWith("wss://")) {
            toast("Ce QR code ne vient pas de KeyraDeck")
            return
        }
        prefs.edit().putString("url", clean).apply()
        serverUrl = clean
        open(clean)
    }

    /** Le PC a ouvert l'appli par le câble USB et donne son adresse (même contenu que le QR code). */
    fun pairFromUsb(url: String) {
        val clean = url.trim()
        val current = serverUrl
        when {
            current == null -> { pair(clean); toast("Connexion au PC par le câble USB") }
            current == clean -> if (status != Status.Connected) open(clean)
            // Même code : c'est le même PC dont l'adresse Wi-Fi a changé.
            tokenOf(current) != null && tokenOf(current) == tokenOf(clean) -> pair(clean)
            else -> pendingUsbPair = clean
        }
    }

    fun pairManually(host: String, port: String, code: String) {
        val p = port.trim().ifEmpty { "8765" }
        pair("ws://${host.trim()}:$p/ws?token=${code.trim().uppercase()}")
    }

    /** Oublie le PC actuel et revient à l'écran d'appairage. */
    fun forget() {
        main.removeCallbacks(reconnect)
        client.close()
        prefs.edit().remove("url").apply()
        serverUrl = null
        layout = null
        folderStack.clear()
        mixerOpen = false
        status = Status.Disconnected
    }

    fun retryNow() {
        serverUrl?.let { open(it) }
    }

    /**
     * Se connecte au PC : par le câble USB s'il est branché et prêt (plus fiable en jeu), sinon par le Wi-Fi.
     * Le câble se teste en une fraction de seconde (127.0.0.1 répond tout de suite, ou refuse).
     */
    private fun open(url: String) {
        main.removeCallbacks(reconnect)
        status = Status.Connecting
        statusText = "Connexion au PC…"
        val seq = ++openSeq
        val port = portOf(url)
        viewModelScope.launch(Dispatchers.IO) {
            val usb = isLoopback(url) || (System.currentTimeMillis() - usbFailedAt > USB_BACKOFF_MS && usbReachable(port))
            post { if (seq == openSeq && serverUrl == url) connectVia(url, if (usb) Link.Usb else Link.Wifi) }
        }
    }

    private fun connectVia(url: String, via: Link) {
        tryingLink = via
        opened = false
        val target = if (via == Link.Usb) usbUrl(url) else url
        val device = URLEncoder.encode(Build.MODEL ?: "Android", "UTF-8")
        client.connect(if (target.contains("device=")) target else "$target&device=$device")
    }

    /**
     * Pendant qu'on est en Wi-Fi, on regarde toutes les 3 s si le câble USB est prêt : si oui, on passe dessus.
     * (Le retour au Wi-Fi, lui, se fait tout seul quand le câble est débranché : la connexion USB se coupe.)
     */
    private fun watchUsb() {
        viewModelScope.launch(Dispatchers.IO) {
            while (isActive) {
                delay(3000)
                val url = serverUrl ?: continue
                if (status != Status.Connected || link != Link.Wifi || isLoopback(url)) continue
                if (System.currentTimeMillis() - usbFailedAt < USB_BACKOFF_MS) continue
                if (usbReachable(portOf(url))) post {
                    if (status == Status.Connected && link == Link.Wifi && serverUrl == url) {
                        main.removeCallbacks(reconnect)
                        connectVia(url, Link.Usb)
                    }
                }
            }
        }
    }

    /** Taille de l'écran en dp, envoyée au PC pour que le configurateur affiche les mêmes proportions. */
    private var screenW = 0
    private var screenCut = 0
    private var screenRadius = 0
    private var screenH = 0

    fun reportScreen(widthDp: Int, heightDp: Int, cutDp: Int = 0, radiusDp: Int = 0) {
        if (widthDp == screenW && heightDp == screenH && cutDp == screenCut && radiusDp == screenRadius) return
        screenW = widthDp
        screenH = heightDp
        screenCut = cutDp
        screenRadius = radiusDp
        sendScreen()
    }

    private fun sendScreen() {
        if (screenW <= 0 || screenH <= 0) return
        send(JSONObject().put("type", "device").put("w", screenW).put("h", screenH).put("cut", screenCut).put("r", screenRadius).put("name", Build.MODEL ?: "Android"))
    }

    override fun onOpen() = post {
        opened = true
        screenBgAsked.clear()
        link = tryingLink
        status = Status.Connected
        sendScreen()
        statusText = ""
        send(JSONObject().put("type", "getLayout"))
        if (mixerOpen) send(JSONObject().put("type", "getMixer").put("full", true))
    }

    override fun onMessage(json: JSONObject) {
        // Le décodage des images se fait ici, hors du fil d'affichage.
        when (json.optString("type")) {
            "layout" -> {
                val parsed = Parser.layout(json)
                val fb = json.optJSONObject("feedback")
                val orientation = json.optString("orientation", "auto")
                post {
                    applyPcFeedback(fb)
                    applyPcOrientation(orientation)
                    syncScreenBgs(parsed)
                    layout = parsed
                    // Si un dossier ouvert a disparu de la config, on le referme.
                    folderStack.removeAll { it !in parsed.pages }
                }
            }
            "mixer" -> post {
                val fresh = Parser.mixer(json, mixer)
                mixer = fresh.map { c ->
                    if (c.id in dragging) mixer.firstOrNull { it.id == c.id } ?: c else c
                }
            }
            "states" -> {
                val arr = json.optJSONArray("active")
                val ids = (0 until (arr?.length() ?: 0)).map { arr!!.getString(it) }.toSet()
                val volume = if (json.has("volume") && !json.isNull("volume")) json.optInt("volume") else null
                val muted = json.optBoolean("muted", false)
                val cpuNow = if (json.has("cpu") && !json.isNull("cpu")) json.optInt("cpu") else null
                val ramNow = if (json.has("ram") && !json.isNull("ram")) json.optInt("ram") else null
                val gpuNow = if (json.has("gpu") && !json.isNull("gpu")) json.optInt("gpu") else null
                val diskNow = if (json.has("disk") && !json.isNull("disk")) json.optInt("disk") else null
                val appNow = json.optJSONObject("app")?.let {
                    AppPerf(it.optString("name"), it.optInt("cpu"), it.optInt("ram"), it.optInt("gpu"), if (it.isNull("fps")) null else it.optInt("fps"))
                }
                val wj = json.optJSONObject("weather")
                val weatherNow = wj?.let { Weather(it.optInt("temp"), it.optInt("code"), it.optInt("wind"), it.optInt("humidity"), it.optString("city")) }
                val discordNow = Parser.discord(json.optJSONObject("discord"))
                post {
                    weather = weatherNow
                    discord = discordNow
                    active = ids
                    if (!volumeDragging) { masterVolume = volume; masterMuted = muted }
                    cpu = cpuNow
                    ram = ramNow
                    gpu = gpuNow
                    disk = diskNow
                    appPerf = appNow
                }
            }
            "nowPlaying" -> {
                val title = if (json.isNull("title")) null else json.optString("title")
                val info = title?.let {
                    NowPlayingInfo(it, json.optString("artist"), json.optString("app"), json.optBoolean("playing"), json.optInt("pos"), json.optInt("dur"), System.currentTimeMillis())
                }
                val artKey = json.optString("artKey")
                // La pochette n'est envoyée que quand elle change : sinon on garde celle qu'on a.
                val art = if (json.isNull("art")) null else Parser.image(json.optString("art"))
                post {
                    nowPlaying = info
                    if (artKey.isEmpty()) nowPlayingArt = null
                    else if (art != null) nowPlayingArt = art
                }
            }
            "focus" -> post { focus = FocusRequest(json.optString("page"), (focus?.count ?: 0) + 1) }
            "toast" -> toast(json.optString("message"))
            "notify" -> {
                val n = Notif(json.optString("id"), json.optString("app"), json.optString("title"), json.optString("text"))
                post {
                    notifications.add(0, n)
                    while (notifications.size > 50) notifications.removeAt(notifications.size - 1)
                    banner = n
                }
            }
        }
    }

    override fun onClosed(reason: String, canRetry: Boolean) = post {
        val url = serverUrl
        // Câble débranché ou USB pas prêt : on repasse tout de suite par le Wi-Fi.
        if (canRetry && url != null && tryingLink == Link.Usb && !isLoopback(url)) {
            if (!opened) usbFailedAt = System.currentTimeMillis()
            if (opened) toast("Câble USB débranché : passage au Wi-Fi")
            open(url)
            return@post
        }
        status = Status.Disconnected
        statusText = reason
        if (canRetry && url != null) main.postDelayed(reconnect, 3000)
    }

    // ---------- Boutons ----------

    fun press(button: DeckButton) {
        when (button.kind) {
            "folder" -> button.page?.let { folderStack.add(it) }
            "mixer" -> openMixer()
            "discordPanel" -> discordPanel = if (discordPanel == button.id) null else button.id
            "page" -> button.page?.let { target ->
                folderStack.clear()
                focus = FocusRequest(target, (focus?.count ?: 0) + 1)
            }
            else -> {
                if (!send(JSONObject().put("type", "press").put("buttonId", button.id))) {
                    toast("Pas connecté au PC")
                } else if (button.closeFolder) {
                    folderStack.clear()
                }
            }
        }
    }

    fun closeFolder() {
        if (folderStack.isNotEmpty()) folderStack.removeAt(folderStack.lastIndex)
    }

    fun closeAllFolders() = folderStack.clear()

    // ---------- Mélangeur de volume ----------

    fun openMixer() {
        folderStack.clear()
        mixerOpen = true
        send(JSONObject().put("type", "getMixer").put("full", true))
        mixerJob?.cancel()
        mixerJob = viewModelScope.launch {
            // Rafraîchit la liste (nouvelles applis, volume changé sur le PC…).
            var tick = 0
            while (isActive && mixerOpen) {
                delay(1500)
                tick++
                // Toutes les ~10 s on redemande les icônes pour les nouvelles applis.
                send(JSONObject().put("type", "getMixer").put("full", tick % 7 == 0))
            }
        }
    }

    fun closeMixer() {
        mixerOpen = false
        mixerJob?.cancel()
    }

    fun setVolume(id: String, volume: Float, final: Boolean = false) {
        if (final) dragging.remove(id) else dragging.add(id)
        mixer = mixer.map { if (it.id == id) it.copy(volume = volume) else it }
        val last = lastSentVolume[id]
        if (final || last == null || abs(last - volume) >= 0.01f) {
            lastSentVolume[id] = volume
            send(JSONObject().put("type", "setVolume").put("id", id).put("volume", volume.toDouble()))
        }
    }

    // ---------- Page souris / clavier ----------

    var showPad by mutableStateOf(false)
    private var moveX = 0f
    private var moveY = 0f
    private var lastMoveAt = 0L

    /** Déplace le curseur du PC ; les petits mouvements sont regroupés (un message toutes les ~12 ms). */
    fun mouseMove(dx: Float, dy: Float) {
        moveX += dx
        moveY += dy
        val now = android.os.SystemClock.uptimeMillis()
        if (now - lastMoveAt >= 8) flushMouse()
    }

    fun flushMouse() {
        val ix = moveX.toInt()
        val iy = moveY.toInt()
        if (ix != 0 || iy != 0) {
            moveX -= ix
            moveY -= iy
            send(JSONObject().put("type", "mouse").put("action", "move").put("dx", ix).put("dy", iy))
        }
        lastMoveAt = android.os.SystemClock.uptimeMillis()
    }

    fun mouseButton(button: String, down: Boolean) {
        flushMouse()
        send(JSONObject().put("type", "mouse").put("action", if (down) "down" else "up").put("button", button))
    }

    fun mouseClick(button: String) {
        mouseButton(button, true)
        mouseButton(button, false)
    }

    /** dy > 0 : vers le haut. */
    fun mouseScroll(dy: Int) {
        send(JSONObject().put("type", "mouse").put("action", "scroll").put("dy", dy))
    }

    fun typeText(text: String) {
        send(JSONObject().put("type", "type").put("text", text))
    }

    /** Clavier PC complet : une touche (code de touche virtuelle Windows) enfoncée ou relâchée. */
    fun pcKey(vk: Int, down: Boolean) {
        send(JSONObject().put("type", "key").put("vk", vk).put("down", down))
    }

    /** Touche ou raccourci simple, ex. "Enter" ou "Ctrl+C". */
    fun typeKey(spec: String) {
        send(JSONObject().put("type", "type").put("key", spec))
    }

    /** Micro / sourdine Discord depuis le widget « Salon vocal », ou partage / raccrocher depuis le menu d'un bouton Discord. */
    fun sendDiscord(command: String) {
        send(JSONObject().put("type", "discord").put("command", command))
    }

    /** Précédent / lecture-pause / suivant pour le widget « Morceau en cours ». */
    fun nowPlayingCommand(action: String) {
        if (action == "toggle") nowPlaying = nowPlaying?.let { it.copy(playing = !it.playing, at = System.currentTimeMillis()) }
        send(JSONObject().put("type", "nowPlaying").put("action", action))
    }

    /** Touches média (lecture, suivant…) pour le widget « Pavé média ». */
    fun sendMedia(key: String) {
        send(JSONObject().put("type", "media").put("key", key))
    }

    fun setMasterVolume(fraction: Float, final: Boolean) {
        val f = fraction.coerceIn(0f, 1f)
        volumeDragging = !final
        masterVolume = (f * 100).toInt()
        val last = lastSentVolume["master"]
        if (final || last == null || abs(last - f) >= 0.01f) {
            lastSentVolume["master"] = f
            send(JSONObject().put("type", "setVolume").put("id", "master").put("volume", f.toDouble()))
        }
    }

    fun toggleMasterMute() {
        masterMuted = !masterMuted
        send(JSONObject().put("type", "setMute").put("id", "master").put("muted", masterMuted))
    }

    fun toggleMute(channel: MixerChannel) {
        val muted = !channel.muted
        mixer = mixer.map { if (it.id == channel.id) it.copy(muted = muted) else it }
        send(JSONObject().put("type", "setMute").put("id", channel.id).put("muted", muted))
    }

    // ---------- Images personnalisées ----------

    /** Envoie une image de la galerie au PC pour remplacer l'icône d'un bouton. */
    fun uploadIcon(buttonId: String, uri: Uri) {
        viewModelScope.launch(Dispatchers.IO) {
            try {
                val png = squarePng(uri, 192) ?: run { toast("Image illisible"); return@launch }
                val b64 = Base64.encodeToString(png, Base64.NO_WRAP)
                if (send(JSONObject().put("type", "setIcon").put("buttonId", buttonId).put("png", b64))) {
                    toast("Image envoyée au PC")
                } else {
                    toast("Pas connecté au PC")
                }
            } catch (e: Exception) {
                toast("Erreur image : ${e.message}")
            }
        }
    }

    fun resetIcon(buttonId: String) {
        send(JSONObject().put("type", "resetIcon").put("buttonId", buttonId))
    }

    private fun squarePng(uri: Uri, size: Int): ByteArray? {
        val resolver = getApplication<Application>().contentResolver
        val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        resolver.openInputStream(uri)?.use { BitmapFactory.decodeStream(it, null, bounds) }
        if (bounds.outWidth <= 0 || bounds.outHeight <= 0) return null

        // Réduit les grosses photos dès la lecture pour ne pas saturer la mémoire.
        var sample = 1
        while (min(bounds.outWidth, bounds.outHeight) / (sample * 2) >= size) sample *= 2
        val opts = BitmapFactory.Options().apply { inSampleSize = sample }
        val src = resolver.openInputStream(uri)?.use { BitmapFactory.decodeStream(it, null, opts) } ?: return null

        val side = min(src.width, src.height)
        val cropped = Bitmap.createBitmap(src, (src.width - side) / 2, (src.height - side) / 2, side, side)
        val scaled = Bitmap.createScaledBitmap(cropped, max(1, min(size, side)), max(1, min(size, side)), true)
        val out = ByteArrayOutputStream()
        scaled.compress(Bitmap.CompressFormat.PNG, 100, out)
        return out.toByteArray()
    }

    // ---------- Utilitaires ----------

    private fun send(json: JSONObject): Boolean = status == Status.Connected && client.send(json)

    private fun toast(message: String) {
        _toasts.tryEmit(message)
    }

    private fun post(block: () -> Unit) {
        main.post(block)
    }

    private fun portOf(url: String): Int = Uri.parse(url).port.takeIf { it > 0 } ?: 8765

    private fun tokenOf(url: String): String? = Uri.parse(url).getQueryParameter("token")?.uppercase()

    private fun isLoopback(url: String): Boolean = Uri.parse(url).host.let { it == "127.0.0.1" || it == "localhost" }

    /** Même adresse, mais par le câble : le PC est joignable sur 127.0.0.1 grâce à adb reverse. */
    private fun usbUrl(url: String): String {
        val uri = Uri.parse(url)
        return uri.buildUpon().encodedAuthority("127.0.0.1:${portOf(url)}").build().toString()
    }

    /** Le câble est prêt si quelque chose répond sur 127.0.0.1 au port du PC (le tunnel d'adb). */
    private fun usbReachable(port: Int): Boolean = try {
        java.net.Socket().use { it.connect(java.net.InetSocketAddress("127.0.0.1", port), 300); true }
    } catch (_: Exception) {
        false
    }

    override fun onCleared() {
        main.removeCallbacks(reconnect)
        client.close()
        feedback.release()
    }
}
