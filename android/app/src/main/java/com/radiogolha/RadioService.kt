package com.radiogolha

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Intent
import android.content.pm.ServiceInfo
import android.net.Uri
import android.os.Binder
import android.os.Build
import android.os.SystemClock
import org.videolan.libvlc.LibVLC
import org.videolan.libvlc.Media
import org.videolan.libvlc.MediaPlayer
import org.videolan.libvlc.interfaces.IMedia

/**
 * Owns the LibVLC player so playback survives the screen turning off and the
 * activity being recreated. Runs in the foreground (with a Stop notification)
 * only while a stream is playing.
 */
class RadioService : Service() {

    /** What the UI shows: status line + progress bar (port of PollStatus/UpdateProgress). */
    data class Snapshot(
        val status: String,
        val tone: Tone,
        val progress: Int,
        val indeterminate: Boolean,
        val label: String,
    )

    enum class Tone { NORMAL, PLAYING, ERROR }

    inner class LocalBinder : Binder() {
        val service get() = this@RadioService
    }

    private val binder = LocalBinder()
    private lateinit var libVlc: LibVLC
    private lateinit var player: MediaPlayer

    var stationName = ""
        private set

    // written from the VLC event thread
    @Volatile private var bufferPct = 0f
    @Volatile private var lastBufferAt = 0L

    private var lastTime = 0L
    private var lastDemuxBytes = 0L
    private var lastDecodedAudio = 0L
    private var lastFlowAt: Long? = null
    private var playStartedAt: Long? = null
    private var connectSince: Long? = null

    override fun onCreate() {
        super.onCreate()
        libVlc = LibVLC(this, arrayListOf("--no-video"))
        player = MediaPlayer(libVlc)
        player.setEventListener { e ->
            if (e.type == MediaPlayer.Event.Buffering) {
                bufferPct = e.buffering
                lastBufferAt = SystemClock.elapsedRealtime()
            }
        }
    }

    override fun onBind(intent: Intent?) = binder

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == ACTION_STOP) stop() else goForeground()
        return START_NOT_STICKY
    }

    override fun onDestroy() {
        player.release()
        libVlc.release()
        super.onDestroy()
    }

    fun play(url: String, name: String, volume: Int) {
        player.stop()
        resetState()
        stationName = name
        // Same rule as the Windows app: a scheme means MRL, otherwise a local path.
        val uri = Uri.parse(url)
        val media = if (uri.scheme.isNullOrEmpty()) Media(libVlc, url) else Media(libVlc, uri)
        player.media = media
        media.release()
        player.volume = volume
        player.play()
        player.volume = volume // the audio output only exists after play()

        val intent = Intent(this, RadioService::class.java)
        if (Build.VERSION.SDK_INT >= 26) startForegroundService(intent) else startService(intent)
    }

    fun stop() {
        player.stop()
        resetState()
        stopForeground(STOP_FOREGROUND_REMOVE)
        stopSelf() // stays alive while the activity is still bound
    }

    fun setVolume(volume: Int) {
        player.volume = volume
    }

    val isActive get() = player.playerState in IMedia.State.Opening..IMedia.State.Paused

    /** Call every ~500 ms from the UI thread. */
    fun snapshot(): Snapshot {
        val now = SystemClock.elapsedRealtime()
        var state = player.playerState

        // "Really playing" detection for mms live streams that stay in Opening/Buffering:
        // (a) the playback clock advances, (b) media statistics keep growing.
        val time = player.time
        val timeAdvancing = time > 0 && time != lastTime
        lastTime = time
        if (statsFlowing()) lastFlowAt = now
        val flowingRecently = lastFlowAt?.let { now - it < 2_500 } == true

        val connecting = state == IMedia.State.Opening || state == STATE_BUFFERING
        if (connecting && (timeAdvancing || flowingRecently)) state = IMedia.State.Playing

        when (state) {
            IMedia.State.Playing -> if (playStartedAt == null) playStartedAt = now
            IMedia.State.Stopped, IMedia.State.Ended, IMedia.State.Error -> {
                playStartedAt = null; lastFlowAt = null
            }
        }
        val stillConnecting = state == IMedia.State.Opening || state == STATE_BUFFERING
        connectSince = if (stillConnecting) connectSince ?: now else null

        var (status, tone) = when (state) {
            IMedia.State.Opening, STATE_BUFFERING -> "Connecting..." to Tone.NORMAL
            IMedia.State.Playing -> "Playing" to Tone.PLAYING
            IMedia.State.Paused -> "Paused" to Tone.NORMAL
            IMedia.State.Stopped -> "Stopped" to Tone.NORMAL
            IMedia.State.Ended -> "Ended" to Tone.NORMAL
            IMedia.State.Error -> "Connection error!" to Tone.ERROR
            else -> "Ready" to Tone.NORMAL
        }
        if (stillConnecting && connectSince?.let { now - it > SLOW_CONNECT_MS } == true) {
            status = "Still connecting... (slow stream)"
        }

        // A fresh buffering % means the stream is really opening; it wins over the clock.
        val pct = bufferPct
        val percent = if (pct > 0 && now - lastBufferAt < BUFFER_FRESH_MS) pct.toInt().coerceIn(0, 100) else 0
        val hasPercent = percent > 0 && (stillConnecting || (state == IMedia.State.Playing && percent < 100))

        return when {
            hasPercent -> Snapshot(status, tone, percent, false, "$percent%")
            stillConnecting -> Snapshot(status, tone, 0, true, "")
            state == IMedia.State.Playing -> {
                // Live streams report no duration, so show how long the listener has been playing.
                val label = if (player.length > 0 && time > 0) formatTime(time)
                else formatTime(now - (playStartedAt ?: now))
                Snapshot(status, tone, 100, false, label)
            }
            else -> Snapshot(status, tone, 0, false, "")
        }
    }

    private fun statsFlowing(): Boolean {
        val media = player.media ?: return false
        try {
            val stats = media.stats ?: return false
            val demux = stats.demuxReadBytes.toLong()
            val decoded = stats.decodedAudio.toLong()
            val flowed = demux > lastDemuxBytes || decoded > lastDecodedAudio
            lastDemuxBytes = maxOf(lastDemuxBytes, demux)
            lastDecodedAudio = maxOf(lastDecodedAudio, decoded)
            return flowed
        } finally {
            media.release() // getMedia() retains
        }
    }

    private fun resetState() {
        bufferPct = 0f; lastBufferAt = 0L
        lastTime = 0L; lastDemuxBytes = 0L; lastDecodedAudio = 0L
        lastFlowAt = null; playStartedAt = null; connectSince = null
    }

    private fun goForeground() {
        val nm = getSystemService(NotificationManager::class.java)
        if (Build.VERSION.SDK_INT >= 26) {
            nm.createNotificationChannel(
                NotificationChannel(CHANNEL, "Playback", NotificationManager.IMPORTANCE_LOW)
            )
        }
        val open = PendingIntent.getActivity(
            this, 0, Intent(this, MainActivity::class.java), PendingIntent.FLAG_IMMUTABLE
        )
        val stop = PendingIntent.getService(
            this, 1, Intent(this, RadioService::class.java).setAction(ACTION_STOP), PendingIntent.FLAG_IMMUTABLE
        )
        @Suppress("DEPRECATION")
        val builder = if (Build.VERSION.SDK_INT >= 26) Notification.Builder(this, CHANNEL) else Notification.Builder(this)
        val notification = builder
            .setSmallIcon(android.R.drawable.ic_media_play)
            .setContentTitle(stationName.ifEmpty { getString(R.string.app_name) })
            .setContentText(getString(R.string.app_name))
            .setContentIntent(open)
            .setOngoing(true)
            .addAction(Notification.Action.Builder(null, "Stop", stop).build())
            .build()
        if (Build.VERSION.SDK_INT >= 29) {
            startForeground(NOTIFICATION_ID, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PLAYBACK)
        } else {
            startForeground(NOTIFICATION_ID, notification)
        }
    }

    companion object {
        private const val ACTION_STOP = "com.radiogolha.STOP"
        private const val CHANNEL = "playback"
        private const val NOTIFICATION_ID = 1
        private const val BUFFER_FRESH_MS = 3_000L
        private const val SLOW_CONNECT_MS = 30_000L
        private const val STATE_BUFFERING = 2 // libvlc state 2; LibVLC for Android has no constant for it

        /** Milliseconds to mm:ss or hh:mm:ss (port of TimeText.Format). */
        fun formatTime(ms: Long): String {
            val total = maxOf(0L, ms) / 1000
            val h = total / 3600
            val m = total / 60 % 60
            val s = total % 60
            return if (h > 0) "%02d:%02d:%02d".format(h, m, s) else "%02d:%02d".format(m, s)
        }
    }
}
