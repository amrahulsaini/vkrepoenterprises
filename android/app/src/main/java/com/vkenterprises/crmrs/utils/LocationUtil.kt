package com.vkenterprises.crmrs.utils

import android.Manifest
import android.annotation.SuppressLint
import android.content.Context
import android.content.pm.PackageManager
import android.location.Location
import androidx.core.content.ContextCompat
import com.google.android.gms.location.LocationServices
import com.google.android.gms.location.Priority
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.withContext
import kotlin.coroutines.resume

private fun hasLocationPermission(context: Context): Boolean {
    val fine = ContextCompat.checkSelfPermission(context, Manifest.permission.ACCESS_FINE_LOCATION)
    val coarse = ContextCompat.checkSelfPermission(context, Manifest.permission.ACCESS_COARSE_LOCATION)
    return fine == PackageManager.PERMISSION_GRANTED || coarse == PackageManager.PERMISSION_GRANTED
}

@SuppressLint("MissingPermission")
suspend fun getCurrentLocation(context: Context): Location? {
    if (!hasLocationPermission(context)) return null
    return suspendCancellableCoroutine { cont ->
        val fused = LocationServices.getFusedLocationProviderClient(context)
        fused.getCurrentLocation(Priority.PRIORITY_HIGH_ACCURACY, null)
            .addOnSuccessListener { loc ->
                if (loc != null) { cont.resume(loc); return@addOnSuccessListener }
                fused.lastLocation
                    .addOnSuccessListener { last -> cont.resume(last) }
                    .addOnFailureListener { cont.resume(null) }
            }
            .addOnFailureListener {
                fused.lastLocation
                    .addOnSuccessListener { last -> cont.resume(last) }
                    .addOnFailureListener { cont.resume(null) }
            }
    }
}

suspend fun reverseGeocodeAddress(context: Context, lat: Double, lng: Double): String? {
    val fromGeocoder = withContext(Dispatchers.IO) {
        runCatching {
            if (!android.location.Geocoder.isPresent()) return@runCatching null
            val gc = android.location.Geocoder(context, java.util.Locale.getDefault())
            @Suppress("DEPRECATION")
            gc.getFromLocation(lat, lng, 1)?.firstOrNull()?.getAddressLine(0)
        }.getOrNull()
    }
    if (!fromGeocoder.isNullOrBlank()) return fromGeocoder
    return withContext(Dispatchers.IO) {
        runCatching {
            val conn = java.net.URL(
                "https://nominatim.openstreetmap.org/reverse?lat=$lat&lon=$lng&format=json&zoom=16&accept-language=en"
            ).openConnection() as java.net.HttpURLConnection
            conn.setRequestProperty("User-Agent", "VKRepoCar/1.0")
            conn.connectTimeout = 6000
            conn.readTimeout = 6000
            val json = conn.inputStream.bufferedReader().readText()
            conn.disconnect()
            org.json.JSONObject(json).optString("display_name").takeIf { it.isNotBlank() }
        }.getOrNull()
    }
}

fun googleMapsLink(lat: Double, lng: Double): String = "https://www.google.com/maps?q=$lat,$lng"
