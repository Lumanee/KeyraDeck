package com.phonedeck.app

import android.content.Context
import android.media.AudioAttributes
import android.media.SoundPool
import android.os.Build
import android.os.VibrationEffect
import android.os.Vibrator
import android.os.VibratorManager
import java.io.File
import java.io.FileOutputStream
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.util.concurrent.ConcurrentHashMap
import kotlin.math.PI
import kotlin.math.exp
import kotlin.math.sin
import kotlin.random.Random

/** Sons proposés au toucher (le nom est celui envoyé par le PC). */
val TouchSounds = listOf("click" to "Clic", "tick" to "Tic", "pop" to "Pop", "bip" to "Bip")

/** Intensités de vibration : 1 = léger, 2 = moyen, 3 = fort. */
val VibrationLevels = listOf(1 to "Léger", 2 to "Moyen", 3 to "Fort")

/**
 * Vibration et petit son joués quand on touche une touche.
 * Les sons sont fabriqués par l'appli (aucun fichier à télécharger) puis gardés prêts à jouer.
 */
class TouchFeedback(context: Context) {
    private val vibrator: Vibrator? =
        if (Build.VERSION.SDK_INT >= 31) (context.getSystemService(Context.VIBRATOR_MANAGER_SERVICE) as? VibratorManager)?.defaultVibrator
        else @Suppress("DEPRECATION") (context.getSystemService(Context.VIBRATOR_SERVICE) as? Vibrator)

    private val pool = SoundPool.Builder()
        .setMaxStreams(4)
        .setAudioAttributes(
            AudioAttributes.Builder()
                .setUsage(AudioAttributes.USAGE_GAME)
                .setContentType(AudioAttributes.CONTENT_TYPE_SONIFICATION)
                .build()
        )
        .build()
    private val sounds = ConcurrentHashMap<String, Int>()

    init {
        // Fabriqué en arrière-plan : un son demandé avant la fin est simplement ignoré.
        Thread {
            val dir = File(context.cacheDir, "sons").apply { mkdirs() }
            for ((name, _) in TouchSounds) {
                runCatching {
                    val file = File(dir, "$name.wav")
                    writeWav(file, synth(name))
                    sounds[name] = pool.load(file.path, 1)
                }
            }
        }.start()
    }

    /**
     * Retour au toucher. [vibrate] et [sound] viennent du bouton (null = réglage général ;
     * vibrate 0 = pas de vibration, sound "off" = pas de son). [long] = appui long (vibration plus marquée).
     */
    fun touch(settings: AppSettings, vibrate: Int? = null, sound: String? = null, long: Boolean = false) {
        val level = vibrate ?: if (settings.haptics) settings.hapticStrength else 0
        if (level > 0) vibrate(level, long)
        val name = sound ?: settings.sound
        if (name != "off") play(name, settings.soundVolume)
    }

    fun vibrate(level: Int, long: Boolean = false) {
        val v = vibrator ?: return
        if (!v.hasVibrator()) return
        val (ms, amp) = when (level.coerceIn(1, 3)) {
            1 -> 12L to 70
            2 -> 22L to 160
            else -> 38L to 255
        }
        val duration = if (long) ms * 2 else ms
        val amplitude = if (v.hasAmplitudeControl()) amp else VibrationEffect.DEFAULT_AMPLITUDE
        runCatching { v.vibrate(VibrationEffect.createOneShot(duration, amplitude)) }
    }

    fun play(name: String, volume: Int) {
        val id = sounds[name] ?: return
        val vol = (volume.coerceIn(0, 100) / 100f)
        if (vol > 0f) pool.play(id, vol, vol, 1, 0, 1f)
    }

    fun release() = pool.release()

    private companion object {
        const val RATE = 44100

        /** Fabrique le son : quelques millisecondes d'onde avec une décroissance rapide. */
        fun synth(name: String): FloatArray {
            val rnd = Random(7)
            fun gen(seconds: Double, f: (Double) -> Double) = FloatArray((RATE * seconds).toInt()) { i -> f(i.toDouble() / RATE).toFloat() }
            return when (name) {
                // Clic de touche mécanique : un petit bruit sec + un léger « toc » grave.
                "click" -> gen(0.035) { t -> (rnd.nextDouble(-1.0, 1.0) * exp(-t * 900) * 0.7 + sin(2 * PI * 180 * t) * exp(-t * 120) * 0.5) }
                // Tic aigu et très court.
                "tick" -> gen(0.025) { t -> sin(2 * PI * 3200 * t) * exp(-t * 260) * 0.8 }
                // Pop : une note qui descend vite.
                "pop" -> gen(0.06) { t -> sin(2 * PI * (900 * t - 5000 * t * t)) * exp(-t * 70) * 0.9 }
                // Bip électronique doux.
                else -> gen(0.07) { t -> sin(2 * PI * 1250 * t) * minOf(1.0, t * 400) * exp(-t * 45) * 0.6 }
            }
        }

        fun writeWav(file: File, samples: FloatArray) {
            val data = ByteBuffer.allocate(samples.size * 2).order(ByteOrder.LITTLE_ENDIAN)
            for (s in samples) data.putShort((s.coerceIn(-1f, 1f) * 32767).toInt().toShort())
            val header = ByteBuffer.allocate(44).order(ByteOrder.LITTLE_ENDIAN).apply {
                put("RIFF".toByteArray()); putInt(36 + data.capacity()); put("WAVE".toByteArray())
                put("fmt ".toByteArray()); putInt(16); putShort(1); putShort(1); putInt(RATE); putInt(RATE * 2); putShort(2); putShort(16)
                put("data".toByteArray()); putInt(data.capacity())
            }
            FileOutputStream(file).use { it.write(header.array()); it.write(data.array()) }
        }
    }
}
