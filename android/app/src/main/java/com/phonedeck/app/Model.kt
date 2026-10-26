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
    val kind: String,
    val page: String?,
    val closeFolder: Boolean,
)

data class DeckPage(val id: String, val name: String, val buttons: List<DeckButton>)

data class DeckLayout(val pcName: String, val home: String, val pages: Map<String, DeckPage>) {
    val homePage: DeckPage? get() = pages[home] ?: pages.values.firstOrNull()
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
            val list = (0 until buttons.length()).map { j -> button(buttons.getJSONObject(j)) }
            val id = p.optString("id")
            pages[id] = DeckPage(id, p.optString("name"), list)
        }
        return DeckLayout(json.optString("pcName", "PC"), json.optString("home"), pages)
    }

    private fun button(b: JSONObject) = DeckButton(
        id = b.optString("id"),
        label = b.optString("label"),
        color = color(b.optStringOrNull("color")),
        icon = image(b.optStringOrNull("icon")),
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

    private fun JSONObject.optStringOrNull(key: String): String? =
        if (has(key) && !isNull(key)) optString(key) else null
}
