package com.phonedeck.app

import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener
import okio.ByteString
import org.json.JSONObject
import java.net.InetAddress
import java.net.Socket
import java.util.concurrent.TimeUnit
import javax.net.SocketFactory

/** Connexion WebSocket vers l'appli PC. Les rappels arrivent sur un fil d'OkHttp. */
class DeckClient(private val listener: Listener) {

    interface Listener {
        fun onOpen()
        fun onMessage(json: JSONObject)
        fun onClosed(reason: String, canRetry: Boolean)
        /** Fichier envoyé en binaire par le PC (fond de zone écran). */
        fun onFile(name: String, data: ByteArray) {}
    }

    private val http = OkHttpClient.Builder()
        .socketFactory(NoDelaySocketFactory())
        .connectTimeout(5, TimeUnit.SECONDS)
        .pingInterval(15, TimeUnit.SECONDS)
        .build()

    @Volatile
    private var socket: WebSocket? = null

    fun connect(url: String) {
        socket?.cancel()
        val request = try {
            Request.Builder().url(url).build()
        } catch (e: IllegalArgumentException) {
            listener.onClosed("Adresse invalide", false)
            return
        }
        socket = http.newWebSocket(request, object : WebSocketListener() {
            override fun onOpen(webSocket: WebSocket, response: Response) {
                if (webSocket === socket) listener.onOpen()
            }

            override fun onMessage(webSocket: WebSocket, text: String) {
                if (webSocket !== socket) return
                try {
                    listener.onMessage(JSONObject(text))
                } catch (_: Exception) {
                }
            }

            override fun onMessage(webSocket: WebSocket, bytes: ByteString) {
                if (webSocket !== socket) return
                // Le nom du fichier, un saut de ligne, puis son contenu.
                val cut = bytes.indexOf(ByteString.of('\n'.code.toByte()))
                if (cut <= 0) return
                listener.onFile(bytes.substring(0, cut).utf8(), bytes.substring(cut + 1).toByteArray())
            }

            override fun onClosing(webSocket: WebSocket, code: Int, reason: String) {
                webSocket.close(1000, null)
            }

            override fun onClosed(webSocket: WebSocket, code: Int, reason: String) {
                if (webSocket === socket) listener.onClosed("Connexion fermée", true)
            }

            override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) {
                if (webSocket !== socket) return
                val reason = when (response?.code) {
                    401 -> "Code refusé par le PC : rescanne le QR code"
                    else -> "PC injoignable (${t.javaClass.simpleName})"
                }
                listener.onClosed(reason, response?.code != 401)
            }
        })
    }

    fun send(json: JSONObject): Boolean = socket?.send(json.toString()) ?: false

    fun close() {
        val s = socket
        socket = null
        s?.close(1000, null)
    }
}

/** Désactive l'algorithme de Nagle : sans ça, Android regroupe les petits messages (souris) et le curseur saccade. */
class NoDelaySocketFactory : SocketFactory() {
    private val base = getDefault()
    private fun cfg(s: Socket): Socket = s.apply { tcpNoDelay = true }
    override fun createSocket(): Socket = cfg(base.createSocket())
    override fun createSocket(host: String?, port: Int): Socket = cfg(base.createSocket(host, port))
    override fun createSocket(host: String?, port: Int, localAddress: InetAddress?, localPort: Int): Socket = cfg(base.createSocket(host, port, localAddress, localPort))
    override fun createSocket(host: InetAddress?, port: Int): Socket = cfg(base.createSocket(host, port))
    override fun createSocket(address: InetAddress?, port: Int, localAddress: InetAddress?, localPort: Int): Socket = cfg(base.createSocket(address, port, localAddress, localPort))
}
