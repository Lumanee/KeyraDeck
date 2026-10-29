package com.phonedeck.app

import android.graphics.BitmapFactory
import android.graphics.ImageDecoder
import android.graphics.Matrix
import android.graphics.SurfaceTexture
import android.graphics.drawable.Animatable
import android.graphics.drawable.BitmapDrawable
import android.graphics.drawable.Drawable
import android.media.MediaPlayer
import android.os.Build
import android.view.Surface
import android.view.TextureView
import android.widget.ImageView
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.produceState
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.viewinterop.AndroidView
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.File

/**
 * Fond d'une zone écran : une image (GIF animé compris, Android 9 et plus) ou une vidéo en boucle, sans son,
 * qui remplit toute la zone (rognée si besoin), puis assombrie de [dim] % pour que les widgets restent lisibles.
 */
@Composable
fun ScreenBackground(file: File, video: Boolean, dim: Int) {
    Box(Modifier.fillMaxSize()) {
        if (video) ScreenVideo(file) else ScreenImage(file)
        if (dim > 0) Box(Modifier.fillMaxSize().background(Color.Black.copy(alpha = dim.coerceIn(0, 80) / 100f)))
    }
}

@Composable
private fun ScreenImage(file: File) {
    val res = LocalContext.current.resources
    val drawable by produceState<Drawable?>(null, file) {
        value = withContext(Dispatchers.IO) { loadBackground(file, res) }
    }
    AndroidView(
        modifier = Modifier.fillMaxSize(),
        factory = { ctx -> ImageView(ctx).apply { scaleType = ImageView.ScaleType.CENTER_CROP } },
        update = { view ->
            if (view.drawable !== drawable) {
                view.setImageDrawable(drawable)
                (drawable as? Animatable)?.start()
            }
        },
    )
}

/** Décode l'image en taille raisonnable (au plus 1600 px de côté). */
private fun loadBackground(file: File, res: android.content.res.Resources): Drawable? = try {
    val max = 1600
    if (Build.VERSION.SDK_INT >= 28) {
        ImageDecoder.decodeDrawable(ImageDecoder.createSource(file)) { decoder, info, _ ->
            val w = info.size.width
            val h = info.size.height
            if (w > max || h > max) {
                val f = max.toFloat() / maxOf(w, h)
                decoder.setTargetSize((w * f).toInt().coerceAtLeast(1), (h * f).toInt().coerceAtLeast(1))
            }
        }
    } else {
        val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        BitmapFactory.decodeFile(file.path, bounds)
        var sample = 1
        while (maxOf(bounds.outWidth, bounds.outHeight) / sample > max) sample *= 2
        BitmapFactory.decodeFile(file.path, BitmapFactory.Options().apply { inSampleSize = sample })?.let { BitmapDrawable(res, it) }
    }
} catch (_: Exception) {
    null
}

/** Vidéo en boucle et muette. TextureView (et non SurfaceView) pour suivre les coins arrondis de la zone. */
@Composable
private fun ScreenVideo(file: File) {
    AndroidView(
        modifier = Modifier.fillMaxSize(),
        factory = { ctx ->
            TextureView(ctx).apply {
                var player: MediaPlayer? = null
                var videoW = 0
                var videoH = 0
                // Remplit la zone sans déformer : la vidéo est agrandie puis rognée au centre.
                fun crop() {
                    if (videoW <= 0 || videoH <= 0 || width <= 0 || height <= 0) return
                    val scale = maxOf(width.toFloat() / videoW, height.toFloat() / videoH)
                    setTransform(Matrix().apply { setScale(videoW * scale / width, videoH * scale / height, width / 2f, height / 2f) })
                }
                surfaceTextureListener = object : TextureView.SurfaceTextureListener {
                    override fun onSurfaceTextureAvailable(st: SurfaceTexture, w: Int, h: Int) {
                        val mp = MediaPlayer()
                        try {
                            mp.setSurface(Surface(st))
                            mp.setDataSource(file.path)
                            mp.isLooping = true
                            mp.setVolume(0f, 0f)
                            mp.setOnVideoSizeChangedListener { _, vw, vh -> videoW = vw; videoH = vh; crop() }
                            mp.setOnPreparedListener { it.start() }
                            mp.prepareAsync()
                            player = mp
                        } catch (_: Exception) {
                            mp.release()
                        }
                    }

                    override fun onSurfaceTextureSizeChanged(st: SurfaceTexture, w: Int, h: Int) = crop()

                    override fun onSurfaceTextureDestroyed(st: SurfaceTexture): Boolean {
                        player?.release()
                        player = null
                        return true
                    }

                    override fun onSurfaceTextureUpdated(st: SurfaceTexture) {}
                }
            }
        },
    )
}
