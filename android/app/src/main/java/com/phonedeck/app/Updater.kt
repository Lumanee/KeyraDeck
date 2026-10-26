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
    private const val RELEASE_API = "https://api.github.com/repos/Lumanee/PhoneDeck/releases/tags/derniere-version"
    private const val APK_NAME = "PhoneDeck.apk"

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
            val request = Request.Builder().url(RELEASE_API)
                .header("Accept", "application/vnd.github+json").build()
            http.newCall(request).execute().use { response ->
                if (!response.isSuccessful) return null
                val json = JSONObject(response.body?.string() ?: return null)
                val build = Regex("build:(\\d+)").find(json.optString("body"))?.groupValues?.get(1)?.toIntOrNull()
                    ?: return null
                val assets = json.optJSONArray("assets") ?: return null
                var url: String? = null
                for (i in 0 until assets.length()) {
                    val a = assets.getJSONObject(i)
                    if (a.optString("name") == APK_NAME) url = a.optString("browser_download_url")
                }
                if (url != null && build > currentBuild(context)) UpdateInfo(build, url) else null
            }
        } catch (e: Exception) {
            null
        }
    }

    /** Télécharge l'APK dans le cache de l'appli. onProgress reçoit un pourcentage de 0 à 100. */
    fun download(context: Context, info: UpdateInfo, onProgress: (Int) -> Unit): File {
        val dir = File(context.cacheDir, "updates").apply { mkdirs() }
        val file = File(dir, APK_NAME)
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
