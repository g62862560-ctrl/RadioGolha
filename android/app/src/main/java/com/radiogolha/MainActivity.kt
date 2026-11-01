package com.radiogolha

import android.Manifest
import android.app.Activity
import android.app.AlertDialog
import android.content.ActivityNotFoundException
import android.content.ComponentName
import android.content.Intent
import android.content.ServiceConnection
import android.content.pm.PackageManager
import android.graphics.Color
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import android.text.InputType
import android.view.Menu
import android.view.MenuItem
import android.view.View
import android.widget.AdapterView
import android.widget.ArrayAdapter
import android.widget.Button
import android.widget.CheckBox
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.ProgressBar
import android.widget.SeekBar
import android.widget.Spinner
import android.widget.TextView
import android.widget.Toast

/** Main screen - Android port of RadioApp.cs. Playback lives in [RadioService]. */
class MainActivity : Activity() {

    private val prefs by lazy { getSharedPreferences("radio_golha", MODE_PRIVATE) }
    private lateinit var stations: MutableList<Station>

    private lateinit var stationSpinner: Spinner
    private lateinit var favoriteButton: Button
    private lateinit var favoritesOnly: CheckBox
    private lateinit var urlField: EditText
    private lateinit var volume: SeekBar
    private lateinit var progress: ProgressBar
    private lateinit var progressLabel: TextView
    private lateinit var status: TextView
    private lateinit var autoPlaySelect: CheckBox
    private lateinit var autoPlayStartup: CheckBox

    private var service: RadioService? = null
    private var pendingAutoStart = false
    private var programmaticPos = -1 // Spinner reports setSelection() asynchronously; ignore that one callback
    private var defaultStatusColor = 0

    private val handler = Handler(Looper.getMainLooper())
    private val poll = object : Runnable {
        override fun run() {
            service?.snapshot()?.let(::render)
            handler.postDelayed(this, 500)
        }
    }

    private val connection = object : ServiceConnection {
        override fun onServiceConnected(name: ComponentName?, binder: IBinder?) {
            val s = (binder as RadioService.LocalBinder).service
            service = s
            if (pendingAutoStart && !s.isActive) play()
            pendingAutoStart = false
        }

        override fun onServiceDisconnected(name: ComponentName?) {
            service = null
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)

        stationSpinner = findViewById(R.id.station)
        favoriteButton = findViewById(R.id.favorite)
        favoritesOnly = findViewById(R.id.favorites_only)
        urlField = findViewById(R.id.url)
        volume = findViewById(R.id.volume)
        progress = findViewById(R.id.progress)
        progressLabel = findViewById(R.id.progress_label)
        status = findViewById(R.id.status)
        autoPlaySelect = findViewById(R.id.autoplay_select)
        autoPlayStartup = findViewById(R.id.autoplay_startup)
        defaultStatusColor = status.currentTextColor

        stations = StationStore.load(this)
        urlField.setText(prefs.getString(KEY_URL, null) ?: DEFAULT_URL)
        volume.progress = prefs.getInt(KEY_VOLUME, 80)
        autoPlaySelect.isChecked = prefs.getBoolean(KEY_AUTO_SELECT, false)
        autoPlayStartup.isChecked = prefs.getBoolean(KEY_AUTO_STARTUP, false)
        pendingAutoStart = savedInstanceState == null && autoPlayStartup.isChecked

        findViewById<Button>(R.id.play).setOnClickListener { play() }
        findViewById<Button>(R.id.stop).setOnClickListener { service?.stop() }
        favoriteButton.setOnClickListener { toggleFavorite() }
        favoritesOnly.setOnCheckedChangeListener { _, _ -> refreshStations() }
        autoPlaySelect.setOnCheckedChangeListener { _, on -> prefs.edit().putBoolean(KEY_AUTO_SELECT, on).apply() }
        autoPlayStartup.setOnCheckedChangeListener { _, on -> prefs.edit().putBoolean(KEY_AUTO_STARTUP, on).apply() }

        volume.setOnSeekBarChangeListener(object : SeekBar.OnSeekBarChangeListener {
            override fun onProgressChanged(bar: SeekBar, value: Int, fromUser: Boolean) {
                service?.setVolume(value)
            }
            override fun onStartTrackingTouch(bar: SeekBar) {}
            override fun onStopTrackingTouch(bar: SeekBar) {
                prefs.edit().putInt(KEY_VOLUME, bar.progress).apply()
            }
        })

        stationSpinner.onItemSelectedListener = object : AdapterView.OnItemSelectedListener {
            override fun onItemSelected(parent: AdapterView<*>, view: View?, position: Int, id: Long) {
                updateFavoriteButton()
                if (position == programmaticPos) {
                    programmaticPos = -1
                    return
                }
                val station = parent.getItemAtPosition(position) as Station
                if (station.url == urlField.text.toString().trim()) return
                urlField.setText(station.url)
                if (autoPlaySelect.isChecked) play()
            }
            override fun onNothingSelected(parent: AdapterView<*>) = updateFavoriteButton()
        }
        refreshStations()

        if (Build.VERSION.SDK_INT >= 33 &&
            checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED
        ) {
            requestPermissions(arrayOf(Manifest.permission.POST_NOTIFICATIONS), 0)
        }
    }

    override fun onStart() {
        super.onStart()
        bindService(Intent(this, RadioService::class.java), connection, BIND_AUTO_CREATE)
        handler.post(poll)
    }

    override fun onStop() {
        handler.removeCallbacks(poll)
        unbindService(connection) // the service keeps running on its own while a stream plays
        service = null
        super.onStop()
    }

    // ---------------- playback ----------------

    private fun play() {
        val url = urlField.text.toString().trim()
        if (url.isEmpty()) {
            toast("Enter a stream URL first")
            return
        }
        prefs.edit().putString(KEY_URL, url).apply()
        val name = stations.firstOrNull { it.url.equals(url, ignoreCase = true) }?.name ?: url
        service?.play(url, name, volume.progress)
    }

    private fun render(snap: RadioService.Snapshot) {
        status.text = snap.status
        status.setTextColor(
            when (snap.tone) {
                RadioService.Tone.PLAYING -> Color.rgb(0x1B, 0x7F, 0x3B)
                RadioService.Tone.ERROR -> Color.rgb(0xC0, 0x39, 0x2B)
                RadioService.Tone.NORMAL -> defaultStatusColor
            }
        )
        progress.isIndeterminate = snap.indeterminate
        progress.progress = snap.progress
        progressLabel.text = snap.label
    }

    // ---------------- station list ----------------

    /** Rebuild the spinner (favorites first) and select the station matching the URL field. */
    private fun refreshStations() {
        val visible = stations.sortedByDescending { it.favorite }
            .filter { !favoritesOnly.isChecked || it.favorite }
        val adapter = ArrayAdapter(this, android.R.layout.simple_spinner_item, visible)
        adapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item)
        stationSpinner.adapter = adapter
        val url = urlField.text.toString().trim()
        val index = visible.indexOfFirst { it.url.equals(url, ignoreCase = true) }.coerceAtLeast(0)
        programmaticPos = if (visible.isEmpty()) -1 else index
        if (visible.isNotEmpty()) stationSpinner.setSelection(index, false)
        updateFavoriteButton()
    }

    private val selectedStation get() = stationSpinner.selectedItem as Station?

    private fun updateFavoriteButton() {
        val station = selectedStation
        favoriteButton.isEnabled = station != null
        favoriteButton.text = if (station?.favorite == true) "★" else "☆"
    }

    private fun toggleFavorite() {
        val station = selectedStation ?: return
        station.favorite = !station.favorite
        StationStore.save(this, stations)
        refreshStations()
    }

    private fun addStation() {
        val name = EditText(this).apply { hint = "Name" }
        val url = EditText(this).apply { hint = "Stream URL"; inputType = InputType.TYPE_TEXT_VARIATION_URI }
        val pad = (16 * resources.displayMetrics.density).toInt()
        val form = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(pad, pad / 2, pad, 0)
            addView(name)
            addView(url)
        }
        AlertDialog.Builder(this)
            .setTitle("Add station")
            .setView(form)
            .setPositiveButton("Add") { _, _ ->
                val station = Station(name.text.toString(), url.text.toString())
                if (station.url.isBlank()) {
                    toast("A stream URL is required")
                } else if (StationStore.merge(stations, listOf(station)) == 0) {
                    toast("That URL is already in the list")
                } else {
                    StationStore.save(this, stations)
                    urlField.setText(station.url.trim())
                    refreshStations()
                }
            }
            .setNegativeButton(android.R.string.cancel, null)
            .show()
    }

    private fun removeStation() {
        val station = selectedStation ?: return
        AlertDialog.Builder(this)
            .setMessage("Remove \"${station.name}\"?")
            .setPositiveButton("Remove") { _, _ ->
                stations.remove(station)
                StationStore.save(this, stations)
                refreshStations()
            }
            .setNegativeButton(android.R.string.cancel, null)
            .show()
    }

    @Suppress("DEPRECATION") // platform Activity; avoids pulling in androidx just for the result API
    private fun importStations() {
        val intent = Intent(Intent.ACTION_OPEN_DOCUMENT)
            .addCategory(Intent.CATEGORY_OPENABLE)
            .setType("*/*")
        startActivityForResult(intent, REQUEST_IMPORT)
    }

    @Deprecated("Deprecated in Java")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        val uri = data?.data
        if (requestCode != REQUEST_IMPORT || resultCode != RESULT_OK || uri == null) return
        val incoming = runCatching {
            contentResolver.openInputStream(uri)!!.bufferedReader().use { StationStore.parse(it.readText()) }
        }.getOrElse {
            toast("Not a valid station list")
            return
        }
        val added = StationStore.merge(stations, incoming)
        StationStore.save(this, stations)
        refreshStations()
        toast("Imported $added station(s)")
    }

    private fun openInVlc() {
        val url = urlField.text.toString().trim()
        if (url.isEmpty()) return
        try {
            startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url)).setPackage("org.videolan.vlc"))
        } catch (e: ActivityNotFoundException) {
            toast("VLC for Android is not installed")
        }
    }

    // ---------------- menu ----------------

    override fun onCreateOptionsMenu(menu: Menu): Boolean {
        menuInflater.inflate(R.menu.main, menu)
        return true
    }

    override fun onOptionsItemSelected(item: MenuItem): Boolean {
        when (item.itemId) {
            R.id.action_add -> addStation()
            R.id.action_import -> importStations()
            R.id.action_remove -> removeStation()
            R.id.action_vlc -> openInVlc()
            else -> return super.onOptionsItemSelected(item)
        }
        return true
    }

    private fun toast(text: String) = Toast.makeText(this, text, Toast.LENGTH_SHORT).show()

    companion object {
        private const val DEFAULT_URL = "mms://www.radiogolha.net/RadioGolha"
        private const val KEY_URL = "url"
        private const val KEY_VOLUME = "volume"
        private const val KEY_AUTO_SELECT = "autoplay_select"
        private const val KEY_AUTO_STARTUP = "autoplay_startup"
        private const val REQUEST_IMPORT = 1
    }
}
