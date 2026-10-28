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
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
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
import java.net.URLEncoder
import kotlin.math.abs
import kotlin.math.max
import kotlin.math.min

enum class Status { Disconnected, Connecting, Connected }

/** Réglages propres au téléphone (le thème de couleurs, lui, vient du PC). */
data class AppSettings(
    val haptics: Boolean = true,
    val keepScreenOn: Boolean = true,
    /** 0 = automatique, 1 = portrait, 2 = paysage. */
    val orientation: Int = 0,
)

/** Demande du PC : afficher cette page (appli au premier plan). Le compteur permet de répéter la même demande. */
data class FocusRequest(val page: String, val count: Int)

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
    private var volumeDragging = false
    var weather by mutableStateOf<Weather?>(null)
        private set

    /** Notifications reçues du PC : bandeau qui s'affiche quelques secondes, puis compteur dans la barre du boîtier. */
    val notifications = mutableStateListOf<Notif>()
    var banner by mutableStateOf<Notif?>(null)
    var showNotifs by mutableStateOf(false)
    fun clearNotifs() { notifications.clear(); banner = null; showNotifs = false }

    var settings by mutableStateOf(
        AppSettings(
            haptics = prefs.getBoolean("haptics", true),
            keepScreenOn = prefs.getBoolean("keepOn", true),
            orientation = prefs.getInt("orientation", 0),
        )
    )
        private set
    var showSettings by mutableStateOf(false)

    var focus by mutableStateOf<FocusRequest?>(null)
        private set

    fun updateSettings(new: AppSettings) {
        settings = new
        prefs.edit()
            .putBoolean("haptics", new.haptics)
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

    init {
        serverUrl?.let { open(it) }
        checkForUpdate()
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

    private fun open(url: String) {
        main.removeCallbacks(reconnect)
        status = Status.Connecting
        statusText = "Connexion au PC…"
        val device = URLEncoder.encode(Build.MODEL ?: "Android", "UTF-8")
        client.connect(if (url.contains("device=")) url else "$url&device=$device")
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
                post {
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
                val wj = json.optJSONObject("weather")
                val weatherNow = wj?.let { Weather(it.optInt("temp"), it.optInt("code"), it.optInt("wind"), it.optInt("humidity"), it.optString("city")) }
                post {
                    weather = weatherNow
                    active = ids
                    if (!volumeDragging) { masterVolume = volume; masterMuted = muted }
                    cpu = cpuNow
                    ram = ramNow
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
        status = Status.Disconnected
        statusText = reason
        if (canRetry && serverUrl != null) main.postDelayed(reconnect, 3000)
    }

    // ---------- Boutons ----------

    fun press(button: DeckButton) {
        when (button.kind) {
            "folder" -> button.page?.let { folderStack.add(it) }
            "mixer" -> openMixer()
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

    override fun onCleared() {
        main.removeCallbacks(reconnect)
        client.close()
    }
}
