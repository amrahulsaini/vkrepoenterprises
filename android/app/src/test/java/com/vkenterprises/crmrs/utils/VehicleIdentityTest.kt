package com.vkenterprises.crmrs.utils

import com.google.gson.Gson
import com.vkenterprises.crmrs.data.models.SearchResult
import org.junit.Assert.*
import org.junit.Test

class VehicleIdentityTest {
    private fun record(rc: String, chassis: String = ""): SearchResult =
        Gson().fromJson(Gson().toJson(mapOf("id" to 1, "vehicleNo" to rc, "chassisNo" to chassis)), SearchResult::class.java)

    @Test fun financeRowsWithDifferentRcFormattingStayTogether() {
        val selected = record("HR-84--2074")
        listOf("HR-84-2074", "hr842074", "HR 84 2074").forEach {
            assertTrue("Missing finance for $it", selected.matchesVehicle(record(it)))
        }
    }

    @Test fun emptyChassisDoesNotJoinUnrelatedVehicles() {
        assertFalse(record("HR-84-2074").matchesVehicle(record("HR-85-D-2074")))
        assertFalse(record("").matchesVehicle(record("")))
    }

    @Test fun chassisOnlyAndBharatRegistrationsNormalize() {
        assertTrue(record("", "MA3-ABC-00123").matchesVehicle(record("", "ma3abc00123")))
        assertTrue(record("24-BH-9764-AB").matchesVehicle(record("24BH9764AB")))
        assertFalse(record("", "MA3ABC00123").matchesVehicle(record("", "MA3ABC99123")))
    }
}
