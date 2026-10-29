package com.phonedeck.app

import android.graphics.BitmapFactory
import android.util.Base64
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.luminance
import org.json.JSONArray
import org.json.JSONObject

/** Un bouton tel que le PC le décrit. kind = "action" | "folder" | "mixer". */
data class DeckButton(
    val id: String,
    val label: String,
    val hideLabel: Boolean,
    val color: Color?,
    val icon: ImageBitmap?,
    /** Case dans la grille (0 = en haut à gauche). */
    val slot: Int,
    val kind: String,
    val page: String?,
    val closeFolder: Boolean,
    /** Vibration propre à ce bouton : null = réglage général, 0 = aucune, 1 à 3 = force. */
    val vibrate: Int? = null,
    /** Son propre à ce bouton : null = réglage général, "off" = aucun. */
    val sound: String? = null,
    /** Grille séparée qui contient ce bouton (null = grille de la page) ; [slot] compte alors dans cette grille. */
    val grid: String? = null,
)

/** Un widget posé sur plusieurs cases : media, nowplaying, volume, clock, system ou weather. */
data class DeckWidget(val id: String, val type: String, val model: String, val slot: Int, val w: Int, val h: Int)

/**
 * Zone « écran » : rectangle de cases sans touches, où les widgets forment un seul écran.
 * [bg] : image ou vidéo de fond (nom du fichier sur le PC), [dim] : assombrissement du fond en %.
 */
data class DeckScreenArea(val slot: Int, val w: Int, val h: Int, val bg: String? = null, val dim: Int = 30) {
    val video: Boolean get() = bg?.endsWith(".mp4") == true || bg?.endsWith(".webm") == true

    fun cells(cols: Int): List<Int> = (0 until h).flatMap { dy -> (0 until w).map { dx -> slot + dy * cols + dx } }

    /** Le widget est entièrement posé sur la zone écran. */
    fun holds(widget: DeckWidget, cols: Int): Boolean {
        val c = slot % cols; val r = slot / cols
        val wc = widget.slot % cols; val wr = widget.slot / cols
        return wc >= c && wr >= r && wc + widget.w <= c + w && wr + widget.h <= r + h
    }
}

/**
 * Grille séparée : un rectangle de cases de la page (comme un widget) découpé en sa propre grille de boutons,
 * [columns] × [rows], avec un titre facultatif.
 */
data class DeckGridBlock(val id: String, val name: String, val slot: Int, val w: Int, val h: Int, val columns: Int, val rows: Int) {
    fun cells(cols: Int): List<Int> = (0 until h).flatMap { dy -> (0 until w).map { dx -> slot + dy * cols + dx } }
}

/** columns / rows : taille de la grille, ou null pour un dossier mis en page automatiquement. */
data class DeckPage(
    val id: String,
    val name: String,
    /** Page d'accueil (on passe de l'une à l'autre en glissant). */
    val root: Boolean,
    val columns: Int?,
    val rows: Int?,
    val buttons: List<DeckButton>,
    val widgets: List<DeckWidget> = emptyList(),
    val screen: DeckScreenArea? = null,
    val grids: List<DeckGridBlock> = emptyList(),
)

/** Couleurs du boîtier, choisies dans le configurateur du PC. */
data class DeckTheme(
    val name: String,
    val body: Color,
    val desk: Color,
    val key: Color,
    val accent: Color,
    val active: Color,
) {
    companion object {
        val Default = DeckTheme(
            "Anthracite", Color(0xFF2C2C34), Color(0xFF0B0B0E), Color(0xFF1B1B21), Color(0xFF7C6CF2), Color(0xFFC62828),
        )
    }

    /** Texte posé sur la couleur d'accent : noir si l'accent est clair (thème Noir OLED, accent platine), sinon blanc. */
    val onAccent: Color get() = if (accent.luminance() > 0.5f) Color.Black else Color.White

    /** Fond noir pur (thème OLED) : pas de halo gris derrière le boîtier. */
    val pureBlack: Boolean get() = desk.luminance() < 0.004f
}

data class DeckLayout(
    val pcName: String,
    val home: String,
    val pages: Map<String, DeckPage>,
    val theme: DeckTheme = DeckTheme.Default,
) {
    val homePage: DeckPage? get() = pages[home] ?: pages.values.firstOrNull()

    /** Les pages d'accueil, dans l'ordre. */
    val rootPages: List<DeckPage> get() = pages.values.filter { it.root }.ifEmpty { listOfNotNull(homePage) }
}

data class Weather(val temp: Int, val code: Int, val wind: Int, val humidity: Int, val city: String)

/** Salon vocal Discord : qui est là, qui parle (envoyé par le PC s'il est connecté à Discord). */
data class DiscordMember(val name: String, val speaking: Boolean, val muted: Boolean, val deafened: Boolean)
data class DiscordVoice(
    val connected: Boolean,
    val status: String,
    val channel: String,
    val muted: Boolean,
    val deafened: Boolean,
    val members: List<DiscordMember>,
    /** Dans un salon vocal ou un appel (même privé, sans nom). */
    val inCall: Boolean = false,
)

/** Morceau en cours sur le PC. [at] = moment de réception, pour faire avancer la barre entre deux messages. */
/** Charge de l'appli au premier plan sur le PC (widget « Appli active ») ; fps = null si le PC ne peut pas les mesurer. */
data class AppPerf(val name: String, val cpu: Int, val ram: Int, val gpu: Int, val fps: Int?)

data class NowPlayingInfo(val title: String, val artist: String, val app: String, val playing: Boolean, val pos: Int, val dur: Int, val at: Long)

data class Notif(val id: String, val app: String, val title: String, val text: String)

/** Morceau d'un message du chat Twitch : du texte, ou une emote ([emote] = son id Twitch, [text] = son nom). */
data class TwitchPart(val text: String, val emote: String?)
/** Message du chat Twitch relayé par le PC. [action] = message « /me » (en italique, de la couleur du pseudo). */
data class TwitchMsg(val id: String, val user: String, val userId: String, val color: String, val action: Boolean, val parts: List<TwitchPart>)

data class MixerChannel(
    val id: String,
    val name: String,
    val volume: Float,
    val muted: Boolean,
    val icon: ImageBitmap?,
)

object Parser {
    fun twitchMsg(j: JSONObject): TwitchMsg {
        val arr = j.optJSONArray("parts") ?: JSONArray()
        val parts = (0 until arr.length()).mapNotNull { arr.optJSONObject(it) }.map {
            TwitchPart(it.optString("t"), if (it.isNull("e")) null else it.optString("e").ifEmpty { null })
        }
        return TwitchMsg(j.optString("id"), j.optString("user"), j.optString("userId"), j.optString("color"), j.optBoolean("action"), parts)
    }

    fun layout(json: JSONObject): DeckLayout {
        val pages = LinkedHashMap<String, DeckPage>()
        val arr = json.optJSONArray("pages") ?: JSONArray()
        for (i in 0 until arr.length()) {
            val p = arr.getJSONObject(i)
            val buttons = p.optJSONArray("buttons") ?: JSONArray()
            val list = (0 until buttons.length()).map { j -> button(buttons.getJSONObject(j), j) }
            val id = p.optString("id")
            pages[id] = DeckPage(
                id = id,
                name = p.optString("name"),
                root = p.optBoolean("root", false),
                columns = p.optIntOrNull("columns"),
                rows = p.optIntOrNull("rows"),
                buttons = list,
                widgets = widgets(p.optJSONArray("widgets")),
                screen = p.optJSONObject("screen")?.let {
                    DeckScreenArea(
                        it.optInt("slot"), it.optInt("w", 1).coerceAtLeast(1), it.optInt("h", 1).coerceAtLeast(1),
                        bg = it.optString("bg").takeIf { b -> b.isNotEmpty() && b != "null" },
                        dim = it.optInt("dim", 30).coerceIn(0, 80),
                    )
                },
                grids = grids(p.optJSONArray("grids")),
            )
        }
        return DeckLayout(json.optString("pcName", "PC"), json.optString("home"), pages, theme(json.optJSONObject("theme")))
    }

    private fun widgets(arr: JSONArray?): List<DeckWidget> {
        if (arr == null) return emptyList()
        return (0 until arr.length()).map { i ->
            val w = arr.getJSONObject(i)
            DeckWidget(w.optString("id"), w.optString("type"), w.optString("model"), w.optInt("slot"), w.optInt("w", 1).coerceAtLeast(1), w.optInt("h", 1).coerceAtLeast(1))
        }
    }

    private fun grids(arr: JSONArray?): List<DeckGridBlock> {
        if (arr == null) return emptyList()
        return (0 until arr.length()).map { i ->
            val g = arr.getJSONObject(i)
            DeckGridBlock(
                g.optString("id"), g.optStringOrNull("name").orEmpty(), g.optInt("slot"),
                g.optInt("w", 1).coerceAtLeast(1), g.optInt("h", 1).coerceAtLeast(1),
                g.optInt("columns", 1).coerceIn(1, 12), g.optInt("rows", 1).coerceIn(1, 24),
            )
        }
    }

    private fun theme(t: JSONObject?): DeckTheme {
        if (t == null) return DeckTheme.Default
        val d = DeckTheme.Default
        return DeckTheme(
            name = t.optString("name", d.name),
            body = color(t.optString("body")) ?: d.body,
            desk = color(t.optString("desk")) ?: d.desk,
            key = color(t.optString("key")) ?: d.key,
            accent = color(t.optString("accent")) ?: d.accent,
            active = color(t.optString("active")) ?: d.active,
        )
    }

    private fun button(b: JSONObject, index: Int) = DeckButton(
        id = b.optString("id"),
        label = b.optString("label"),
        hideLabel = b.optBoolean("hideLabel", false),
        color = color(b.optStringOrNull("color")),
        icon = image(b.optStringOrNull("icon")),
        slot = b.optInt("slot", index),
        kind = b.optString("kind", "action"),
        page = b.optStringOrNull("page"),
        closeFolder = b.optBoolean("closeFolder", false),
        vibrate = b.optIntOrNull("vibrate"),
        sound = b.optStringOrNull("sound"),
        grid = b.optStringOrNull("grid"),
    )

    /** Liste des canaux du mélangeur. Si le PC n'envoie pas les icônes, on garde les anciennes. */
    fun mixer(json: JSONObject, previous: List<MixerChannel>): List<MixerChannel> {
        val oldIcons = previous.associate { it.id to it.icon }
        val arr = json.optJSONArray("channels") ?: JSONArray()
        return (0 until arr.length()).map { i ->
            val c = arr.getJSONObject(i)
            val id = c.optString("id")
            MixerChannel(
                id = id,
                name = c.optString("name"),
                volume = c.optDouble("volume", 0.0).toFloat(),
                muted = c.optBoolean("muted", false),
                icon = image(c.optStringOrNull("icon")) ?: oldIcons[id],
            )
        }
    }

    fun discord(d: JSONObject?): DiscordVoice? {
        if (d == null) return null
        val arr = d.optJSONArray("members") ?: JSONArray()
        val members = (0 until arr.length()).map { i ->
            val m = arr.getJSONObject(i)
            DiscordMember(m.optString("name"), m.optBoolean("speaking"), m.optBoolean("muted"), m.optBoolean("deafened"))
        }
        return DiscordVoice(d.optBoolean("connected"), d.optString("status"), d.optString("channel"), d.optBoolean("muted"), d.optBoolean("deafened"), members,
            d.optBoolean("inCall", d.optString("channel").isNotEmpty()))
    }

    fun image(base64: String?): ImageBitmap? {
        if (base64.isNullOrEmpty()) return null
        return try {
            val bytes = Base64.decode(base64, Base64.DEFAULT)
            BitmapFactory.decodeByteArray(bytes, 0, bytes.size)?.asImageBitmap()
        } catch (e: Exception) {
            null
        }
    }

    private fun color(hex: String?): Color? {
        if (hex.isNullOrBlank()) return null
        return try {
            Color(android.graphics.Color.parseColor(hex))
        } catch (e: Exception) {
            null
        }
    }

    private fun JSONObject.optIntOrNull(key: String): Int? =
        if (has(key) && !isNull(key)) optInt(key) else null

    private fun JSONObject.optStringOrNull(key: String): String? =
        if (has(key) && !isNull(key)) optString(key) else null
}
