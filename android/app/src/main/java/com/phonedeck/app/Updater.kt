package com.phonedeck.app

import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.provider.Settings
import androidx.core.content.FileProvider
import okhttp3.OkHttpClient
import okhttp3.Request
import org.json.JSONObject
import java.io.File
import java.util.concurrent.TimeUnit

/** Une version plus récente est disponible sur GitHub. */
data class UpdateInfo(val build: Int, val apkUrl: String)

/** Mise à jour automatique : compare avec la dernière version publiée, télécharge l'APK et lance l'installation. */
object Updater {
    // La nouvelle adresse (KeyraDeck) d'abord, puis l'ancienne : GitHub redirige aussi l'ancienne après un renommage.
    private val RELEASE_APIS = listOf(
        "https://api.github.com/repos/Lumanee/KeyraDeck/releases/tags/derniere-version",
        "https://api.github.com/repos/Lumanee/PhoneDeck/releases/tags/derniere-version",
    )
    private val APK_NAMES = listOf("KeyraDeck.apk", "PhoneDeck.apk")
    private const val APK_FILE = "KeyraDeck.apk"

    private val http = OkHttpClient.Builder()
        .connectTimeout(10, TimeUnit.SECONDS)
        .readTimeout(60, TimeUnit.SECONDS)
        .build()

    fun currentBuild(context: Context): Int {
        val info = context.packageManager.getPackageInfo(context.packageName, 0)
        @Suppress("DEPRECATION")
        return if (Build.VERSION.SDK_INT >= 28) info.longVersionCode.toInt() else info.versionCode
    }

    /** Renvoie la mise à jour si elle est plus récente que l'appli installée, sinon null (ou en cas d'erreur réseau). */
    fun check(context: Context): UpdateInfo? {
        return try {
            var text: String? = null
            for (api in RELEASE_APIS) {
                val request = Request.Builder().url(api).header("Accept", "application/vnd.github+json").build()
                val body = try {
                    http.newCall(request).execute().use { r -> if (r.isSuccessful) r.body?.string() else null }
                } catch (e: Exception) { null }
                if (body != null) { text = body; break }
            }
            val t = text ?: return null
            run {
                val json = JSONObject(t)
                val build = Regex("build:(\\d+)").find(json.optString("body"))?.groupValues?.get(1)?.toIntOrNull()
                    ?: return null
                val assets = json.optJSONArray("assets") ?: return null
                val urls = HashMap<String, String>()
                for (i in 0 until assets.length()) {
                    val a = assets.getJSONObject(i)
                    urls[a.optString("name")] = a.optString("browser_download_url")
                }
                val url = APK_NAMES.firstNotNullOfOrNull { urls[it] }
                if (url != null && build > currentBuild(context)) UpdateInfo(build, url) else null
            }
        } catch (e: Exception) {
            null
        }
    }

    /** Télécharge l'APK dans le cache de l'appli. onProgress reçoit un pourcentage de 0 à 100. */
    fun download(context: Context, info: UpdateInfo, onProgress: (Int) -> Unit): File {
        val dir = File(context.cacheDir, "updates").apply { mkdirs() }
        val file = File(dir, APK_FILE)
        http.newCall(Request.Builder().url(info.apkUrl).build()).execute().use { response ->
            if (!response.isSuccessful) throw IllegalStateException("Téléchargement refusé (${response.code})")
            val body = response.body ?: throw IllegalStateException("Fichier vide")
            val total = body.contentLength()
            body.byteStream().use { input ->
                file.outputStream().use { output ->
                    val buffer = ByteArray(64 * 1024)
                    var done = 0L
                    while (true) {
                        val n = input.read(buffer)
                        if (n < 0) break
                        output.write(buffer, 0, n)
                        done += n
                        if (total > 0) onProgress((done * 100 / total).toInt())
                    }
                }
            }
        }
        return file
    }

    /** Android demande une autorisation la première fois (« installer des applis inconnues »). */
    fun canInstall(context: Context): Boolean = context.packageManager.canRequestPackageInstalls()

    fun openInstallPermission(context: Context) {
        val intent = Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:${context.packageName}"))
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        context.startActivity(intent)
    }

    fun install(context: Context, apk: File) {
        val uri = FileProvider.getUriForFile(context, "${context.packageName}.updates", apk)
        val intent = Intent(Intent.ACTION_VIEW)
            .setDataAndType(uri, "application/vnd.android.package-archive")
            .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_ACTIVITY_NEW_TASK)
        context.startActivity(intent)
    }
}
