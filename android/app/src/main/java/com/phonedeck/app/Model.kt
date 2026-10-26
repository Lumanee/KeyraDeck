package com.phonedeck.app

import android.graphics.BitmapFactory
import android.util.Base64
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.asImageBitmap
import org.json.JSONArray
import org.json.JSONObject

/** Un bouton tel que le PC le décrit. kind = "action" | "folder" | "mixer". */
data class DeckButton(
    val id: String,
    val label: String,
    val color: Color?,
    val icon: ImageBitmap?,
    /** Case dans la grille (0 = en haut à gauche). */
    val slot: Int,
    val kind: String,
    val page: String?,
    val closeFolder: Boolean,
)

/** Un widget posé sur plusieurs cases : media, volume, clock ou system. */
data class DeckWidget(val id: String, val type: String, val slot: Int, val w: Int, val h: Int)

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

data class MixerChannel(
    val id: String,
    val name: String,
    val volume: Float,
    val muted: Boolean,
    val icon: ImageBitmap?,
)

object Parser {
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
            )
        }
        return DeckLayout(json.optString("pcName", "PC"), json.optString("home"), pages, theme(json.optJSONObject("theme")))
    }

    private fun widgets(arr: JSONArray?): List<DeckWidget> {
        if (arr == null) return emptyList()
        return (0 until arr.length()).map { i ->
            val w = arr.getJSONObject(i)
            DeckWidget(w.optString("id"), w.optString("type"), w.optInt("slot"), w.optInt("w", 1).coerceAtLeast(1), w.optInt("h", 1).coerceAtLeast(1))
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
        color = color(b.optStringOrNull("color")),
        icon = image(b.optStringOrNull("icon")),
        slot = b.optInt("slot", index),
        kind = b.optString("kind", "action"),
        page = b.optStringOrNull("page"),
        closeFolder = b.optBoolean("closeFolder", false),
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
