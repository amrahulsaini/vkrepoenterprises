package com.vkenterprises.crmrs.utils

import com.vkenterprises.crmrs.data.models.SearchResult

fun String.vehicleKey(): String = uppercase().replace(Regex("[^A-Z0-9]"), "")

fun SearchResult.matchesVehicle(other: SearchResult): Boolean {
    val rc = vehicleNo.vehicleKey()
    val chassis = chassisNo.vehicleKey()
    return (rc.isNotEmpty() && rc == other.vehicleNo.vehicleKey()) ||
        (chassis.isNotEmpty() && chassis == other.chassisNo.vehicleKey())
}
