package com.vkenterprises.vras.workers

import android.Manifest
import android.content.Context
import android.content.pm.PackageManager
import android.location.Location
import androidx.core.content.ContextCompat
import androidx.hilt.work.HiltWorker
import androidx.work.*
import com.google.android.gms.location.LocationServices
import com.google.android.gms.location.Priority
import com.google.android.gms.tasks.CancellationTokenSource
import com.vkenterprises.vras.data.api.ApiService
import com.vkenterprises.vras.data.models.HeartbeatRequest
import com.vkenterprises.vras.utils.PreferencesManager
import dagger.assisted.Assisted
import dagger.assisted.AssistedInject
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlin.coroutines.resume

@HiltWorker
class LocationWorker @AssistedInject constructor(
    @Assisted private val context: Context,
    @Assisted params: WorkerParameters,
    private val api: ApiService,
    private val prefs: PreferencesManager
) : CoroutineWorker(context, params) {

    override suspend fun doWork(): Result {
        val userId = prefs.userId.first()
        if (userId <= 0) return Result.success()

        var lat: Double? = null
        var lng: Double? = null

        val fineGranted = ContextCompat.checkSelfPermission(
            context, Manifest.permission.ACCESS_FINE_LOCATION
        ) == PackageManager.PERMISSION_GRANTED
        val coarseGranted = ContextCompat.checkSelfPermission(
            context, Manifest.permission.ACCESS_COARSE_LOCATION
        ) == PackageManager.PERMISSION_GRANTED

        if (fineGranted || coarseGranted) {
            runCatching {
                val loc = getLocation(fineGranted)
                if (loc != null) {
                    lat = loc.latitude
                    lng = loc.longitude
                }
            }
        }

        runCatching {
            val resp = api.heartbeat(HeartbeatRequest(userId, lat, lng))
            if (resp.isSuccessful) {
                val body = resp.body()
                when {
                    body?.isBlacklisted == true -> prefs.setBlockedReason("blacklisted")
                    body?.isStopped    == true  -> prefs.setBlockedReason("app_stopped")
                }
            }
        }

        return Result.success()
    }

    private suspend fun getLocation(highAccuracy: Boolean): Location? {
        val client = LocationServices.getFusedLocationProviderClient(context)
        val priority = if (highAccuracy)
            Priority.PRIORITY_BALANCED_POWER_ACCURACY
        else
            Priority.PRIORITY_LOW_POWER

        // Try a fresh fix first; background workers often can't warm the GPS chip
        // in time, so getCurrentLocation returns null → fall back to last known.
        val fresh = suspendCancellableCoroutine<Location?> { cont ->
            val cts = CancellationTokenSource()
            client.getCurrentLocation(priority, cts.token)
                .addOnSuccessListener { loc -> cont.resume(loc) }
                .addOnFailureListener { cont.resume(null) }
                .addOnCanceledListener  { cont.resume(null) }
            cont.invokeOnCancellation { cts.cancel() }
        }
        if (fresh != null) return fresh

        // lastLocation is the system-cached position (any app); always available
        // as long as the device has had a fix at any point.
        return suspendCancellableCoroutine { cont ->
            client.lastLocation
                .addOnSuccessListener { loc -> cont.resume(loc) }
                .addOnFailureListener { cont.resume(null) }
                .addOnCanceledListener  { cont.resume(null) }
        }
    }
}
