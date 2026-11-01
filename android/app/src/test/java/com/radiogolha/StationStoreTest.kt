package com.radiogolha

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class StationStoreTest {
    @Test
    fun parsesArrayAndWrappedObjectCaseInsensitively() {
        val a = StationStore.parse("""[{"Name":" A ","URL":"http://a","Favorite":true},{"name":"","url":"http://b"},{"url":"  "}]""")
        assertEquals(listOf(Station("A", "http://a", true), Station("http://b", "http://b")), a)
        assertEquals(1, StationStore.parse("""{"Stations":[{"name":"x","url":"u"}]}""").size)
        assertEquals(0, StationStore.parse("""{"other":1}""").size)
    }

    @Test
    fun mergeSkipsDuplicatesAndPromotesFavorites() {
        val list = mutableListOf(Station("A", "http://a"))
        val added = StationStore.merge(list, listOf(Station("a2", "HTTP://A", true), Station("B", "http://b")))
        assertEquals(1, added)
        assertTrue(list[0].favorite)
        assertEquals(list, StationStore.parse(StationStore.toJson(list)))
    }
}
