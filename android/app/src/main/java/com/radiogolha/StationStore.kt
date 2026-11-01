package com.radiogolha

import android.content.Context
import org.json.JSONArray
import org.json.JSONObject
import java.io.File

/** One station: display name, stream URL and whether it is a favorite. */
data class Station(var name: String, var url: String, var favorite: Boolean = false) {
    override fun toString() = if (favorite) "★ $name" else name
}

/**
 * Station list storage (port of StationStore.cs). The first run seeds the list
 * from the bundled radios.json; after that it lives in filesDir/stations.json.
 */
object StationStore {
    private const val FILE_NAME = "stations.json"

    fun load(context: Context): MutableList<Station> {
        val file = File(context.filesDir, FILE_NAME)
        if (file.exists()) {
            runCatching { return parse(file.readText()) }
            // Keep an unreadable user file around instead of silently losing it.
            runCatching { file.copyTo(File(file.path + ".bak"), overwrite = true) }
        }
        val seed = runCatching {
            parse(context.assets.open("radios.json").bufferedReader().use { it.readText() })
        }.getOrDefault(mutableListOf())
        save(context, seed)
        return seed
    }

    fun save(context: Context, stations: List<Station>) {
        runCatching { File(context.filesDir, FILE_NAME).writeText(toJson(stations)) }
    }

    fun toJson(stations: List<Station>): String = JSONArray().apply {
        stations.forEach {
            put(JSONObject().put("name", it.name).put("url", it.url).put("favorite", it.favorite))
        }
    }.toString(2)

    /** Accepts a plain JSON array or an object with a "stations" array. Throws on invalid JSON. */
    fun parse(json: String): MutableList<Station> {
        val text = json.trim()
        if (text.isEmpty()) return mutableListOf()
        val array = if (text.startsWith("{")) {
            val obj = JSONObject(text)
            val key = obj.keys().asSequence().firstOrNull { it.equals("stations", ignoreCase = true) }
                ?: return mutableListOf()
            obj.optJSONArray(key) ?: return mutableListOf()
        } else JSONArray(text)

        val parsed = (0 until array.length()).mapNotNull { i ->
            val o = array.optJSONObject(i) ?: return@mapNotNull null
            Station(o.ci("name")?.toString() ?: "", o.ci("url")?.toString() ?: "", o.ci("favorite") == true)
        }
        return normalize(parsed)
    }

    /** Append stations not in the list yet (by URL, case-insensitive); returns how many were added. */
    fun merge(target: MutableList<Station>, incoming: List<Station>): Int {
        var added = 0
        for (s in normalize(incoming)) {
            val existing = target.firstOrNull { it.url.equals(s.url, ignoreCase = true) }
            if (existing == null) {
                target.add(s); added++
            } else if (s.favorite) existing.favorite = true
        }
        return added
    }

    /** Trim values, drop empty URLs and merge duplicate stations. */
    fun normalize(stations: List<Station>): MutableList<Station> {
        val byUrl = LinkedHashMap<String, Station>()
        for (s in stations) {
            val url = s.url.trim()
            if (url.isEmpty()) continue
            val known = byUrl[url.lowercase()]
            if (known != null) {
                known.favorite = known.favorite || s.favorite
                continue
            }
            byUrl[url.lowercase()] = Station(s.name.trim().ifEmpty { url }, url, s.favorite)
        }
        return byUrl.values.toMutableList()
    }

    private fun JSONObject.ci(name: String): Any? =
        keys().asSequence().firstOrNull { it.equals(name, ignoreCase = true) }?.let { opt(it) }
}
